using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

static class CapabilityCatalog
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "std.time",
        "std.time.sleep_ms",
        "std.io",
        "std.io.read_line",
        "std.fs",
        "engine.window",
        "engine.input",
        "engine.gfx",
        "engine.audio"
    };

    private static readonly IReadOnlySet<string> VmWeb = new HashSet<string>(StringComparer.Ordinal)
    {
        "std.time",
        "std.io",
        "engine.window",
        "engine.input",
        "engine.gfx",
        "engine.audio"
    };

    public static bool IsKnown(string capability) => Known.Contains(capability);

    public static bool IsSupported(CompileTarget target, string capability)
    {
        return target switch
        {
            CompileTarget.VmNative => true,
            CompileTarget.VmWeb => VmWeb.Contains(capability),
            _ => false
        };
    }

    public static string? Normalize(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            return null;

        var normalized = capability.Trim().ToLowerInvariant();
        return IsKnown(normalized) ? normalized : null;
    }
}
