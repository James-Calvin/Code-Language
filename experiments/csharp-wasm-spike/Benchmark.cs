using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeWasmSpike;

public static partial class Benchmark
{
    [JSExport]
    public static string Run(double startupMilliseconds)
    {
        try
        {
            var reports = new List<WorkloadReport>();
            foreach (string workload in new[] { "runtime_cpu", "verlet_kernel" })
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"benchmarks.{workload}.bytecode")
                    ?? throw new InvalidOperationException($"Missing embedded benchmark '{workload}'.");
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                reports.Add(RunWorkload(workload, memory.ToArray()));
            }

            var report = new SpikeReport(startupMilliseconds, GC.GetTotalMemory(forceFullCollection: true), reports);
            return JsonSerializer.Serialize(report, SpikeJsonContext.Default.SpikeReport);
        }
        catch (Exception error)
        {
            return JsonSerializer.Serialize(new ErrorReport(error.ToString()), SpikeJsonContext.Default.ErrorReport);
        }
    }

    private static WorkloadReport RunWorkload(string name, byte[] bytecode)
    {
        const int warmupRuns = 5;
        const int sampleRuns = 20;
        for (int run = 0; run < warmupRuns; run++) new TaggedBytecodeVm(bytecode).Run();
        var samples = new double[sampleRuns];
        for (int run = 0; run < sampleRuns; run++)
        {
            long started = Stopwatch.GetTimestamp();
            new TaggedBytecodeVm(bytecode).Run();
            samples[run] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        Array.Sort(samples);
        double average = samples.Average();
        double variance = samples.Select(value => (value - average) * (value - average)).Average();
        return new WorkloadReport(name, samples[(sampleRuns - 1) / 2], samples[(int)Math.Ceiling(sampleRuns * 0.95) - 1], Math.Sqrt(variance) / average);
    }

    public sealed record WorkloadReport(string Workload, double MedianMs, double P95Ms, double CoefficientOfVariation);
    public sealed record SpikeReport(double StartupMs, long ManagedMemoryBytes, IReadOnlyList<WorkloadReport> Results);
    public sealed record ErrorReport(string Error);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Benchmark.SpikeReport))]
[JsonSerializable(typeof(Benchmark.ErrorReport))]
internal sealed partial class SpikeJsonContext : JsonSerializerContext { }
