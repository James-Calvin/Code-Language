using System;
using System.Collections.Generic;

namespace ConsoleApp1.Compiler;

sealed record HostAbiSymbol(string Symbol, int Arity, string Capability);

static class HostAbiCatalog
{
    private static readonly Dictionary<string, HostAbiSymbol> Symbols = new(StringComparer.Ordinal)
    {
        ["std.io.print"] = new HostAbiSymbol("std.io.print", 1, "std.io"),
        ["std.time.unix_ms"] = new HostAbiSymbol("std.time.unix_ms", 0, "std.time"),
        ["std.time.unix_us"] = new HostAbiSymbol("std.time.unix_us", 0, "std.time"),
        ["std.time.mono_ns"] = new HostAbiSymbol("std.time.mono_ns", 0, "std.time"),
        ["std.time.mono_ticks"] = new HostAbiSymbol("std.time.mono_ticks", 0, "std.time"),
        ["std.time.mono_ticks_per_second"] = new HostAbiSymbol("std.time.mono_ticks_per_second", 0, "std.time")
    };

    private static readonly Dictionary<string, HostAbiSymbol> Intrinsics = new(StringComparer.Ordinal)
    {
        ["unix_ms"] = Symbols["std.time.unix_ms"],
        ["unix_us"] = Symbols["std.time.unix_us"],
        ["mono_ns"] = Symbols["std.time.mono_ns"],
        ["mono_ticks"] = Symbols["std.time.mono_ticks"],
        ["mono_ticks_per_second"] = Symbols["std.time.mono_ticks_per_second"]
    };

    public static HostAbiSymbol StdIoPrint => Symbols["std.io.print"];

    public static bool TryGetSymbol(string symbol, out HostAbiSymbol hostSymbol)
        => Symbols.TryGetValue(symbol, out hostSymbol!);

    public static bool TryGetIntrinsic(string intrinsicName, out HostAbiSymbol hostSymbol)
        => Intrinsics.TryGetValue(intrinsicName, out hostSymbol!);

    public static IEnumerable<string> IntrinsicNames => Intrinsics.Keys;
}
