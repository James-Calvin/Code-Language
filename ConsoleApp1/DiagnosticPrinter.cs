using System;

namespace ConsoleApp1;

internal static class DiagnosticPrinter
{
    public static void PrintSnippet(string path, string source, int line, int col)
    {
        if (line <= 0) return;
        var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (line - 1 >= lines.Length) return;
        string text = lines[line - 1];
        Console.Error.WriteLine($"--> {path}:{line}:{col}");
        Console.Error.WriteLine($"{line,4} | {text}");
        string caretPad = col > 1 ? new string(' ', col - 1) : string.Empty;
        Console.Error.WriteLine($"     | {caretPad}^");
    }
}
