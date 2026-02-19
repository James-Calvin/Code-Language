using System;

namespace ConsoleApp1.Compiler;

enum CompileTarget
{
    VmNative,
    VmWeb
}

static class CompileTargetExtensions
{
    public static bool TryParse(string? value, out CompileTarget target)
    {
        target = CompileTarget.VmNative;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "vm-native" => SetTarget(CompileTarget.VmNative, out target),
            "vm-web" => SetTarget(CompileTarget.VmWeb, out target),
            _ => false
        };
    }

    public static string ToCliValue(this CompileTarget target) => target switch
    {
        CompileTarget.VmNative => "vm-native",
        CompileTarget.VmWeb => "vm-web",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported compile target.")
    };

    private static bool SetTarget(CompileTarget value, out CompileTarget target)
    {
        target = value;
        return true;
    }
}
