if (args.Length == 0) throw new ArgumentException("Pass one or more bytecode-v10 benchmark files.");
foreach (string path in args)
{
    new TaggedBytecodeVm(File.ReadAllBytes(path)).Run();
    Console.WriteLine($"[PASS] tagged VM {Path.GetFileName(path)}");
}
