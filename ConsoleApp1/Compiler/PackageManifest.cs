using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConsoleApp1.Compiler;

sealed class PackageManifest
{
    public const string FileName = "code.package.json";

    public string Path { get; }
    public string PackageRoot { get; }
    public int SchemaVersion { get; }
    public string Name { get; }
    public string Version { get; }
    public string Kind { get; }
    public string Entry { get; }
    public IReadOnlyDictionary<string, string> Exports { get; }
    public IReadOnlyList<CompileTarget> Targets { get; }
    public IReadOnlyDictionary<string, string> Dependencies { get; }
    public IReadOnlyDictionary<string, string> DevDependencies { get; }
    public IReadOnlyDictionary<CompileTarget, string> TargetEntryOverrides { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }

    public PackageManifest(
        string path,
        int schemaVersion,
        string name,
        string version,
        string kind,
        string entry,
        IReadOnlyDictionary<string, string> exports,
        IReadOnlyList<CompileTarget> targets,
        IReadOnlyDictionary<string, string> dependencies,
        IReadOnlyDictionary<string, string> devDependencies,
        IReadOnlyDictionary<CompileTarget, string> targetEntryOverrides,
        IReadOnlyList<string> requiredCapabilities)
    {
        Path = System.IO.Path.GetFullPath(path);
        PackageRoot = Directory.GetParent(Path)?.FullName
            ?? throw new InvalidOperationException($"Could not resolve package root for '{path}'.");
        SchemaVersion = schemaVersion;
        Name = name;
        Version = version;
        Kind = kind;
        Entry = entry;
        Exports = exports;
        Targets = targets;
        Dependencies = dependencies;
        DevDependencies = devDependencies;
        TargetEntryOverrides = targetEntryOverrides;
        RequiredCapabilities = requiredCapabilities;
    }

    public string GetEntryForTarget(CompileTarget target)
    {
        return TargetEntryOverrides.TryGetValue(target, out var overrideEntry)
            ? overrideEntry
            : Entry;
    }
}

static class PackageManifestLoader
{
    private static readonly Regex SemVerRegex = new(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled);

    public static PackageManifest? TryLoadNearest(string entryPath, CompileTarget target)
    {
        string fullEntryPath = System.IO.Path.GetFullPath(entryPath);
        string? directory = System.IO.Path.GetDirectoryName(fullEntryPath);
        while (!string.IsNullOrEmpty(directory))
        {
            string candidate = System.IO.Path.Combine(directory, PackageManifest.FileName);
            if (File.Exists(candidate))
            {
                return LoadFromPath(candidate, target);
            }
            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    public static PackageManifest LoadFromPath(string manifestPath, CompileTarget target)
    {
        var manifest = Parse(manifestPath);
        ValidateForTarget(manifest, target);
        return manifest;
    }

    private static PackageManifest Parse(string manifestPath)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (JsonException ex)
        {
            int line = (int?)ex.LineNumber + 1 ?? 1;
            int column = (int?)ex.BytePositionInLine + 1 ?? 1;
            throw ManifestError(manifestPath, $"Invalid JSON ({ex.Message})", line, column);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw ManifestError(manifestPath, "Root value must be a JSON object.");

            int schemaVersion = GetRequiredInt(root, manifestPath, "schemaVersion");
            if (schemaVersion != 1)
                throw ManifestError(manifestPath, $"Unsupported schemaVersion '{schemaVersion}'. Expected 1.");

            string name = GetRequiredString(root, manifestPath, "name");
            string version = GetRequiredString(root, manifestPath, "version");
            string kind = GetRequiredString(root, manifestPath, "kind").ToLowerInvariant();
            string entry = GetRequiredString(root, manifestPath, "entry");

            if (!SemVerRegex.IsMatch(version))
                throw ManifestError(manifestPath, $"Field 'version' must be semver (got '{version}').");
            if (kind is not ("library" or "application"))
                throw ManifestError(manifestPath, "Field 'kind' must be either 'library' or 'application'.");
            ValidatePathLike(manifestPath, "entry", entry);

            var exports = ReadStringMap(root, manifestPath, "exports");
            var targets = ReadTargets(root, manifestPath);
            var dependencies = ReadStringMap(root, manifestPath, "dependencies");
            var devDependencies = ReadStringMap(root, manifestPath, "devDependencies");
            var targetOverrides = ReadTargetOverrides(root, manifestPath);
            var requiredCapabilities = ReadCapabilities(root, manifestPath);

            ValidateEntryFileExists(manifestPath, entry, "entry");
            foreach (var pair in targetOverrides)
                ValidateEntryFileExists(manifestPath, pair.Value, $"targetOverrides.{pair.Key.ToCliValue()}.entry");

            return new PackageManifest(
                manifestPath,
                schemaVersion,
                name,
                version,
                kind,
                entry,
                exports,
                targets,
                dependencies,
                devDependencies,
                targetOverrides,
                requiredCapabilities);
        }
    }

    private static void ValidateForTarget(PackageManifest manifest, CompileTarget target)
    {
        if (manifest.Targets.Count > 0 && !manifest.Targets.Contains(target))
        {
            throw ManifestError(
                manifest.Path,
                $"Package '{manifest.Name}' does not support target '{target.ToCliValue()}'.");
        }

        for (int i = 0; i < manifest.RequiredCapabilities.Count; i++)
        {
            string capability = manifest.RequiredCapabilities[i];
            if (!CapabilityCatalog.IsSupported(target, capability))
            {
                throw ManifestError(
                    manifest.Path,
                    $"Capability '{capability}' is not available for target '{target.ToCliValue()}' (hostAbi.requires).");
            }
        }
    }

    private static IReadOnlyList<CompileTarget> ReadTargets(JsonElement root, string manifestPath)
    {
        if (!root.TryGetProperty("targets", out var targetsElement))
            return Array.Empty<CompileTarget>();
        if (targetsElement.ValueKind != JsonValueKind.Array)
            throw ManifestError(manifestPath, "Field 'targets' must be an array.");

        var targets = new List<CompileTarget>();
        foreach (var value in targetsElement.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
                throw ManifestError(manifestPath, "Field 'targets' must contain strings.");
            string targetText = value.GetString() ?? string.Empty;
            if (!CompileTargetExtensions.TryParse(targetText, out var target))
                throw ManifestError(manifestPath, $"Unknown target '{targetText}' in field 'targets'.");
            if (!targets.Contains(target))
                targets.Add(target);
        }

        return targets;
    }

    private static IReadOnlyDictionary<CompileTarget, string> ReadTargetOverrides(JsonElement root, string manifestPath)
    {
        if (!root.TryGetProperty("targetOverrides", out var overridesElement))
            return new Dictionary<CompileTarget, string>();
        if (overridesElement.ValueKind != JsonValueKind.Object)
            throw ManifestError(manifestPath, "Field 'targetOverrides' must be an object.");

        var map = new Dictionary<CompileTarget, string>();
        foreach (var prop in overridesElement.EnumerateObject())
        {
            if (!CompileTargetExtensions.TryParse(prop.Name, out var target))
                throw ManifestError(manifestPath, $"Unknown target '{prop.Name}' in field 'targetOverrides'.");
            if (prop.Value.ValueKind != JsonValueKind.Object)
                throw ManifestError(manifestPath, $"Field 'targetOverrides.{prop.Name}' must be an object.");

            if (prop.Value.TryGetProperty("entry", out var entryElement))
            {
                if (entryElement.ValueKind != JsonValueKind.String)
                    throw ManifestError(manifestPath, $"Field 'targetOverrides.{prop.Name}.entry' must be a string.");
                string value = entryElement.GetString() ?? string.Empty;
                ValidatePathLike(manifestPath, $"targetOverrides.{prop.Name}.entry", value);
                map[target] = value;
            }
        }

        return map;
    }

    private static IReadOnlyList<string> ReadCapabilities(JsonElement root, string manifestPath)
    {
        if (!root.TryGetProperty("hostAbi", out var hostAbi))
            return Array.Empty<string>();
        if (hostAbi.ValueKind != JsonValueKind.Object)
            throw ManifestError(manifestPath, "Field 'hostAbi' must be an object.");

        if (!hostAbi.TryGetProperty("requires", out var requiresElement))
            return Array.Empty<string>();
        if (requiresElement.ValueKind != JsonValueKind.Array)
            throw ManifestError(manifestPath, "Field 'hostAbi.requires' must be an array.");

        var capabilities = new List<string>();
        foreach (var item in requiresElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw ManifestError(manifestPath, "Field 'hostAbi.requires' must contain strings.");
            string raw = item.GetString() ?? string.Empty;
            string? capability = CapabilityCatalog.Normalize(raw);
            if (capability is null)
                throw ManifestError(manifestPath, $"Unknown capability '{raw}' in field 'hostAbi.requires'.");
            if (!capabilities.Contains(capability, StringComparer.Ordinal))
                capabilities.Add(capability);
        }

        return capabilities;
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement root, string manifestPath, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var mapElement))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        if (mapElement.ValueKind != JsonValueKind.Object)
            throw ManifestError(manifestPath, $"Field '{propertyName}' must be an object.");

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in mapElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
                throw ManifestError(manifestPath, $"Field '{propertyName}.{prop.Name}' must be a string.");
            string value = prop.Value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                throw ManifestError(manifestPath, $"Field '{propertyName}.{prop.Name}' cannot be empty.");
            map[prop.Name] = value.Trim();
        }

        return map;
    }

    private static int GetRequiredInt(JsonElement root, string manifestPath, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var valueElement))
            throw ManifestError(manifestPath, $"Missing required field '{propertyName}'.");
        if (valueElement.ValueKind != JsonValueKind.Number || !valueElement.TryGetInt32(out int value))
            throw ManifestError(manifestPath, $"Field '{propertyName}' must be an integer.");
        return value;
    }

    private static string GetRequiredString(JsonElement root, string manifestPath, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var valueElement))
            throw ManifestError(manifestPath, $"Missing required field '{propertyName}'.");
        if (valueElement.ValueKind != JsonValueKind.String)
            throw ManifestError(manifestPath, $"Field '{propertyName}' must be a string.");

        string value = valueElement.GetString()?.Trim() ?? string.Empty;
        if (value.Length == 0)
            throw ManifestError(manifestPath, $"Field '{propertyName}' cannot be empty.");
        return value;
    }

    private static void ValidatePathLike(string manifestPath, string propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw ManifestError(manifestPath, $"Field '{propertyName}' cannot be empty.");
        if (!value.EndsWith(".code", StringComparison.OrdinalIgnoreCase))
            throw ManifestError(manifestPath, $"Field '{propertyName}' must point to a .code file.");
    }

    private static void ValidateEntryFileExists(string manifestPath, string entry, string fieldName)
    {
        string manifestDir = Directory.GetParent(System.IO.Path.GetFullPath(manifestPath))?.FullName
            ?? throw new InvalidOperationException("Manifest directory not found.");
        string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(manifestDir, entry));
        if (!File.Exists(fullPath))
            throw ManifestError(manifestPath, $"Field '{fieldName}' points to missing file '{entry}'.");
    }

    private static CompilerException ManifestError(string manifestPath, string message, int line = 1, int column = 1)
        => new($"Manifest '{System.IO.Path.GetFileName(manifestPath)}': {message}", line, column);
}
