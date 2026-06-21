using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

static class CapabilityCatalog
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "std.time",
        "std.time.sleep_ms",
        "std.math",
        "standard.input_output",
        "standard.input_output.read_line",
        "std.io",
        "std.io.read_line",
        "std.fs",
        "engine.window",
        "engine.input",
        "engine.gfx",
        "engine.diagnostics",
        "engine.runtime",
        "engine.audio"
    };

    private static readonly IReadOnlySet<string> VmWeb = new HashSet<string>(StringComparer.Ordinal)
    {
        "std.time",
        "std.math",
        "standard.input_output",
        "engine.window",
        "engine.input",
        "engine.gfx",
        "engine.diagnostics",
        "engine.runtime",
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
        normalized = normalized switch
        {
            "std.io" => "standard.input_output",
            "std.io.read_line" => "standard.input_output.read_line",
            _ => normalized
        };
        return IsKnown(normalized) ? normalized : null;
    }
}
