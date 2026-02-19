using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ConsoleApp1.Compiler;

sealed class CodeLibraryArtifact
{
    public const string Extension = ".codelib";
    public const int SchemaVersion = 1;

    public string Name { get; }
    public string Version { get; }
    public string Kind { get; }
    public CompileTarget Target { get; }
    public string Entry { get; }
    public IReadOnlyDictionary<string, string> Exports { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }
    public byte[] Bytecode { get; }

    public CodeLibraryArtifact(
        string name,
        string version,
        string kind,
        CompileTarget target,
        string entry,
        IReadOnlyDictionary<string, string> exports,
        IReadOnlyList<string> requiredCapabilities,
        byte[] bytecode)
    {
        Name = name;
        Version = version;
        Kind = kind;
        Target = target;
        Entry = entry;
        Exports = exports;
        RequiredCapabilities = requiredCapabilities;
        Bytecode = bytecode;
    }
}

static class CodeLibraryArtifactFormat
{
    public static string GetFileName(string packageName, string version, CompileTarget target)
    {
        string safeName = packageName
            .Trim()
            .Replace(".", "-", StringComparison.Ordinal)
            .Replace("/", "-", StringComparison.Ordinal)
            .Replace("\\", "-", StringComparison.Ordinal);

        return $"{safeName}-{version}-{target.ToCliValue()}{CodeLibraryArtifact.Extension}";
    }

    public static void Write(string path, CodeLibraryArtifact artifact)
    {
        var payload = new
        {
            schemaVersion = CodeLibraryArtifact.SchemaVersion,
            name = artifact.Name,
            version = artifact.Version,
            kind = artifact.Kind,
            target = artifact.Target.ToCliValue(),
            entry = artifact.Entry,
            exports = artifact.Exports,
            requiredCapabilities = artifact.RequiredCapabilities,
            bytecode = Convert.ToBase64String(artifact.Bytecode)
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static CodeLibraryArtifact Read(string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw ArtifactError(path, $"Invalid JSON ({ex.Message})");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw ArtifactError(path, "Root value must be an object.");

            int schemaVersion = GetRequiredInt(root, "schemaVersion", path);
            if (schemaVersion != CodeLibraryArtifact.SchemaVersion)
                throw ArtifactError(path, $"Unsupported schemaVersion '{schemaVersion}'.");

            string name = GetRequiredString(root, "name", path);
            string version = GetRequiredString(root, "version", path);
            string kind = GetRequiredString(root, "kind", path);
            string targetText = GetRequiredString(root, "target", path);
            string entry = GetRequiredString(root, "entry", path);

            if (!CompileTargetExtensions.TryParse(targetText, out var target))
                throw ArtifactError(path, $"Unknown target '{targetText}'.");

            var exports = ReadStringMap(root, "exports", path);
            var capabilities = ReadCapabilities(root, "requiredCapabilities", path);
            byte[] bytecode = ReadBytecode(root, "bytecode", path);

            return new CodeLibraryArtifact(name, version, kind, target, entry, exports, capabilities, bytecode);
        }
    }

    private static byte[] ReadBytecode(JsonElement root, string name, string path)
    {
        string text = GetRequiredString(root, name, path);
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw ArtifactError(path, $"Field '{name}' must be valid base64.");
        }
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement root, string name, string path)
    {
        if (!root.TryGetProperty(name, out var element))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
            throw ArtifactError(path, $"Field '{name}' must be an object.");

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
                throw ArtifactError(path, $"Field '{name}.{prop.Name}' must be a string.");
            map[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }

        return map;
    }

    private static IReadOnlyList<string> ReadCapabilities(JsonElement root, string name, string path)
    {
        if (!root.TryGetProperty(name, out var element))
            return Array.Empty<string>();
        if (element.ValueKind != JsonValueKind.Array)
            throw ArtifactError(path, $"Field '{name}' must be an array.");

        var capabilities = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw ArtifactError(path, $"Field '{name}' must contain strings.");
            string raw = item.GetString() ?? string.Empty;
            string? capability = CapabilityCatalog.Normalize(raw);
            if (capability is null)
                throw ArtifactError(path, $"Unknown capability '{raw}' in field '{name}'.");
            if (!capabilities.Contains(capability, StringComparer.Ordinal))
                capabilities.Add(capability);
        }

        return capabilities;
    }

    private static int GetRequiredInt(JsonElement root, string name, string path)
    {
        if (!root.TryGetProperty(name, out var element))
            throw ArtifactError(path, $"Missing required field '{name}'.");
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int value))
            throw ArtifactError(path, $"Field '{name}' must be an integer.");
        return value;
    }

    private static string GetRequiredString(JsonElement root, string name, string path)
    {
        if (!root.TryGetProperty(name, out var element))
            throw ArtifactError(path, $"Missing required field '{name}'.");
        if (element.ValueKind != JsonValueKind.String)
            throw ArtifactError(path, $"Field '{name}' must be a string.");

        string value = element.GetString()?.Trim() ?? string.Empty;
        if (value.Length == 0)
            throw ArtifactError(path, $"Field '{name}' cannot be empty.");
        return value;
    }

    private static CompilerException ArtifactError(string path, string message)
        => new($"Library artifact '{Path.GetFileName(path)}': {message}", 1, 1);
}
