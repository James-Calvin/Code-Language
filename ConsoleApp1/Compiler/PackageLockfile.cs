using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConsoleApp1.Compiler;

sealed class PackageLockfile
{
    public const string FileName = "code.lock.json";
    public const int SchemaVersion = 1;

    public CompileTarget Target { get; }
    public IReadOnlyList<PackageLockfilePackage> Packages { get; }

    public PackageLockfile(CompileTarget target, IReadOnlyList<PackageLockfilePackage> packages)
    {
        Target = target;
        Packages = packages;
    }

    public string ToJsonString(bool indented = true)
    {
        var payload = new
        {
            schemaVersion = SchemaVersion,
            target = Target.ToCliValue(),
            packages = Packages.Select(package => new
            {
                name = package.Name,
                version = package.Version,
                resolved = package.Resolved,
                integrity = package.Integrity
            })
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = indented });
    }
}

sealed record PackageLockfilePackage(
    string Name,
    string Version,
    string Resolved,
    string Integrity);

static class PackageDependencyResolver
{
    public static PackageLockfile ResolveAndWriteLockfile(
        PackageManifest rootManifest,
        CompileTarget target,
        Action<string>? traceWriter = null)
    {
        var resolver = new Resolver(rootManifest, target, traceWriter);
        var lockfile = resolver.Resolve();
        string lockPath = Path.Combine(rootManifest.PackageRoot, PackageLockfile.FileName);
        File.WriteAllText(lockPath, lockfile.ToJsonString());
        traceWriter?.Invoke($"Wrote lockfile {PackageLockfile.FileName} ({lockfile.Packages.Count} package(s))");
        return lockfile;
    }

    private sealed class Resolver
    {
        private readonly PackageManifest _rootManifest;
        private readonly CompileTarget _target;
        private readonly Action<string>? _traceWriter;
        private readonly Dictionary<string, PackageManifest> _resolved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);

        public Resolver(PackageManifest rootManifest, CompileTarget target, Action<string>? traceWriter)
        {
            _rootManifest = rootManifest;
            _target = target;
            _traceWriter = traceWriter;
        }

        public PackageLockfile Resolve()
        {
            ResolveManifest(_rootManifest);
            var packages = _resolved.Values
                .OrderBy(manifest => manifest.Name, StringComparer.Ordinal)
                .Select(ToLockfilePackage)
                .ToList();

            return new PackageLockfile(_target, packages);
        }

        private void ResolveManifest(PackageManifest manifest)
        {
            if (_resolved.TryGetValue(manifest.Name, out var existing))
            {
                if (!string.Equals(existing.Path, manifest.Path, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.Version, manifest.Version, StringComparison.Ordinal))
                {
                    throw ManifestError(
                        manifest.Path,
                        $"Dependency conflict for package '{manifest.Name}'. Already resolved '{existing.Version}' at '{existing.Path}'.");
                }
                return;
            }

            if (_resolving.Contains(manifest.Name))
            {
                throw ManifestError(
                    manifest.Path,
                    $"Cyclic dependency detected while resolving package '{manifest.Name}'.");
            }

            _resolving.Add(manifest.Name);
            _resolved[manifest.Name] = manifest;

            foreach (var dependency in manifest.Dependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var dependencyManifest = ResolveDependencyManifest(manifest, dependency.Key, dependency.Value);
                ResolveManifest(dependencyManifest);
            }

            _resolving.Remove(manifest.Name);
        }

        private PackageManifest ResolveDependencyManifest(PackageManifest requester, string dependencyName, string versionRange)
        {
            string? dependencyManifestPath = FindDependencyManifest(requester.PackageRoot, dependencyName);
            if (dependencyManifestPath is null)
            {
                throw ManifestError(
                    requester.Path,
                    $"Could not resolve dependency '{dependencyName}' declared by package '{requester.Name}'.");
            }

            var dependencyManifest = PackageManifestLoader.LoadFromPath(dependencyManifestPath, _target);
            if (!string.Equals(dependencyManifest.Name, dependencyName, StringComparison.Ordinal))
            {
                throw ManifestError(
                    dependencyManifest.Path,
                    $"Resolved dependency '{dependencyName}' to package '{dependencyManifest.Name}'. Names must match.");
            }

            if (!VersionSatisfies(versionRange, dependencyManifest.Version))
            {
                throw ManifestError(
                    requester.Path,
                    $"Dependency '{dependencyName}' version '{dependencyManifest.Version}' does not satisfy version range '{versionRange}'.");
            }

            _traceWriter?.Invoke(
                $"Resolve package dependency {requester.Name} -> {dependencyManifest.Name}@{dependencyManifest.Version} ({Path.GetFullPath(dependencyManifest.Path)})");

            return dependencyManifest;
        }

        private string? FindDependencyManifest(string startDirectory, string dependencyName)
        {
            string segmentedName = dependencyName.Replace('.', Path.DirectorySeparatorChar);
            var relativeCandidates = new[]
            {
                Path.Combine("packages", dependencyName, PackageManifest.FileName),
                Path.Combine("packages", segmentedName, PackageManifest.FileName),
                Path.Combine("lib", "packages", dependencyName, PackageManifest.FileName),
                Path.Combine("lib", "packages", segmentedName, PackageManifest.FileName)
            };

            string? cursor = Path.GetFullPath(startDirectory);
            while (!string.IsNullOrEmpty(cursor))
            {
                for (int i = 0; i < relativeCandidates.Length; i++)
                {
                    string candidate = Path.GetFullPath(Path.Combine(cursor, relativeCandidates[i]));
                    if (File.Exists(candidate))
                        return candidate;
                }

                cursor = Directory.GetParent(cursor)?.FullName;
            }

            return null;
        }

        private PackageLockfilePackage ToLockfilePackage(PackageManifest manifest)
        {
            string resolved = Path.GetRelativePath(_rootManifest.PackageRoot, manifest.Path).Replace('\\', '/');
            string integrity = ComputeIntegrity(manifest.Path);
            return new PackageLockfilePackage(manifest.Name, manifest.Version, resolved, integrity);
        }

        private static string ComputeIntegrity(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] hash = SHA256.HashData(bytes);
            return "sha256-" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static readonly Regex ExactVersionRegex = new(
            @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
            RegexOptions.Compiled);

        private static readonly Regex CaretVersionRegex = new(
            @"^\^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$",
            RegexOptions.Compiled);

        private static bool VersionSatisfies(string range, string actualVersion)
        {
            if (!TryParseSemVersion(actualVersion, out var actual))
                return false;

            var exact = ExactVersionRegex.Match(range.Trim());
            if (exact.Success)
            {
                var required = new SemVersion(
                    int.Parse(exact.Groups["major"].Value),
                    int.Parse(exact.Groups["minor"].Value),
                    int.Parse(exact.Groups["patch"].Value));
                return actual.CompareTo(required) == 0;
            }

            var caret = CaretVersionRegex.Match(range.Trim());
            if (caret.Success)
            {
                var minimum = new SemVersion(
                    int.Parse(caret.Groups["major"].Value),
                    int.Parse(caret.Groups["minor"].Value),
                    int.Parse(caret.Groups["patch"].Value));

                if (actual.Major != minimum.Major)
                    return false;
                return actual.CompareTo(minimum) >= 0;
            }

            return false;
        }

        private static bool TryParseSemVersion(string text, out SemVersion version)
        {
            version = default;
            var match = ExactVersionRegex.Match(text.Trim());
            if (!match.Success)
                return false;

            version = new SemVersion(
                int.Parse(match.Groups["major"].Value),
                int.Parse(match.Groups["minor"].Value),
                int.Parse(match.Groups["patch"].Value));
            return true;
        }

        private static CompilerException ManifestError(string manifestPath, string message, int line = 1, int column = 1)
            => new($"Manifest '{Path.GetFileName(manifestPath)}': {message}", line, column);
    }

    private readonly record struct SemVersion(int Major, int Minor, int Patch) : IComparable<SemVersion>
    {
        public int CompareTo(SemVersion other)
        {
            int majorCompare = Major.CompareTo(other.Major);
            if (majorCompare != 0) return majorCompare;
            int minorCompare = Minor.CompareTo(other.Minor);
            if (minorCompare != 0) return minorCompare;
            return Patch.CompareTo(other.Patch);
        }
    }
}
