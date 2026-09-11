using System.Diagnostics;
using System.Reflection;
using Eizo.Metadata.Core;

namespace Eizo.Metadata.Providers;

public sealed record MetadataRuntimeModuleInfo(
    string AssemblyName,
    string Version,
    string AssemblyPath);

public sealed record MetadataRuntimeProbe(
    string Version,
    bool IsConsistent,
    IReadOnlyList<MetadataRuntimeModuleInfo> Modules);

public static class MetadataRuntimeDiagnostics
{
    public static MetadataRuntimeProbe Probe()
    {
        var modules = new[]
        {
            ReadModule(typeof(MetadataResolver).Assembly),
            ReadModule(typeof(BangumiMetadataProvider).Assembly),
        };

        var versions = modules
            .Select(static module => module.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MetadataRuntimeProbe(
            versions.Length == 1 ? versions[0] : string.Join("/", versions),
            versions.Length == 1,
            modules);
    }

    private static MetadataRuntimeModuleInfo ReadModule(Assembly assembly)
    {
        var path = assembly.Location;
        var version = ReadProductVersion(path, assembly.GetName().Version);
        return new MetadataRuntimeModuleInfo(
            assembly.GetName().Name ?? string.Empty,
            version,
            path);
    }

    private static string ReadProductVersion(
        string path,
        Version? fallback)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var value = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (!string.IsNullOrWhiteSpace(value) &&
                Version.TryParse(value, out var parsed))
            {
                return $"{parsed.Major}.{Math.Max(0, parsed.Minor)}.{Math.Max(0, parsed.Build)}";
            }
        }

        fallback ??= new Version(0, 0, 0);
        return $"{fallback.Major}.{Math.Max(0, fallback.Minor)}.{Math.Max(0, fallback.Build)}";
    }
}
