using System;
using System.Diagnostics;
using System.Reflection;
using PasteGuard;

// Synthetic inputs only. Run an optimized build outside the UI for comparable timings.
internal static class CorePerformance
{
    static readonly Func<long> Allocated = AllocationCounter();

    static Func<long> AllocationCounter()
    {
        MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        if (method != null) return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
        AppDomain.MonitoringIsEnabled = true;
        return delegate { return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize; };
    }

    static void Measure(string name, string text, string pattern, int iterations)
    {
        for (int i = 0; i < 5; i++) PolicyEngine.MatchText(text, pattern);
        var timer = new Stopwatch();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = Allocated();
        int matches = 0;
        timer.Start();
        for (int i = 0; i < iterations; i++) if (PolicyEngine.MatchText(text, pattern)) matches++;
        timer.Stop();
        long bytes = Allocated() - before;
        Console.WriteLine("{0}: {1:F3} us/call; {2:F0} allocated bytes/call; {3}/{4} matches",
            name, timer.Elapsed.TotalMilliseconds * 1000 / iterations, (double)bytes / iterations, matches, iterations);
    }

    static void Main()
    {
        const string pattern = "PGTEST_[A-Z0-9]{16}";
        Measure("64 KiB ordinary", new string('x', 65536), pattern, 300);
        Measure("short synthetic match", "prefix PGTEST_ABCD1234EFGH5678 suffix", pattern, 30000);
        Measure("64 KiB partial prefixes", new string('P', 65536), pattern, 100);
        Measure("optional states", new string('a', 512), "^a{0,64}b$", 500);
        Measure("adversarial budget", new string('a', 65536), Repeat("a*", 64) + "b", 10);
    }

    static string Repeat(string value, int count)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++) result.Append(value);
        return result.ToString();
    }
}
