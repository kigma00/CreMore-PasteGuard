using System;
using System.Diagnostics;
using System.Reflection;
using PasteGuard;

// Resolves only the foreground executable name; never reads the clipboard or sends input.
static class NativePerformance
{
    static int Main()
    {
        AppDomain.MonitoringIsEnabled = true;
        using (var guard = new WindowsGuard())
        {
            var method = typeof(WindowsGuard).GetMethod("ForegroundProcess", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            var resolve = (Func<string>)(method.IsStatic ? Delegate.CreateDelegate(typeof(Func<string>), method)
                : Delegate.CreateDelegate(typeof(Func<string>), guard, method));
            for (int i = 0; i < 200; i++) resolve();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            int collections = GC.CollectionCount(0), resolved = 0;
            const int iterations = 10000;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) if (resolve() != null) resolved++;
            watch.Stop();
            Console.WriteLine("Foreground lookup: {0:F3} us/call, {1:F0} bytes/call, Gen0={2}, resolved={3}/{4}",
                watch.Elapsed.TotalMilliseconds * 1000 / iterations,
                (AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - allocated) / (double)iterations,
                GC.CollectionCount(0) - collections, resolved, iterations);
            return resolved == iterations ? 0 : 1;
        }
    }
}
