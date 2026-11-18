using System.Runtime.CompilerServices;

namespace MauSysInfo.Services;

/// <summary>
/// A short CPU benchmark: a fixed mix of floating-point (Mandelbrot) and integer (hashing, branching) work,
/// counted in batches per second. Scores only compare with other Mau SysInfo results.
/// </summary>
public static class Benchmark
{
    /// <summary>Scales batches/second to a friendlier number.</summary>
    private const double ScoreScale = 0.1;

    /// <summary>Runs the workload on <paramref name="threads"/> threads for <paramref name="duration"/>; returns the score.</summary>
    public static double Run(int threads, TimeSpan duration, CancellationToken token)
    {
        // Warm up so the first batches don't include JIT time.
        Kernel(1);

        long[] counts = new long[threads * 16]; // padded so threads don't share cache lines
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var workers = new Thread[threads];
        var start = new ManualResetEventSlim(false);
        for (int t = 0; t < threads; t++)
        {
            int slot = t * 16;
            workers[t] = new Thread(() =>
            {
                start.Wait();
                uint seed = (uint)slot + 1;
                double sink = 0;
                while (!stop.IsCancellationRequested)
                {
                    sink += Kernel(seed++);
                    counts[slot]++;
                }
                GC.KeepAlive(sink);
            }) { IsBackground = true, Name = $"Bench {t}" };
            workers[t].Start();
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        start.Set();
        stop.CancelAfter(duration);
        foreach (var w in workers) w.Join();
        clock.Stop();
        token.ThrowIfCancellationRequested();

        long total = 0;
        for (int t = 0; t < threads; t++) total += counts[t * 16];
        return total / clock.Elapsed.TotalSeconds * ScoreScale;
    }

    /// <summary>Keeps every thread busy until cancelled (stability / thermal check).</summary>
    public static Task StressAsync(int threads, CancellationToken token) => Task.Run(() =>
    {
        try { Run(threads, Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) { }
    });

    /// <summary>One batch: a 48×48 Mandelbrot tile plus an integer hash loop. Returns a value so nothing is optimised away.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    internal static double Kernel(uint seed)
    {
        const int Size = 48, MaxIter = 64;
        double offsetX = -0.75 + (seed % 7) * 0.001, offsetY = 0.1 + (seed % 5) * 0.001;
        int inside = 0;
        for (int py = 0; py < Size; py++)
        {
            double y0 = offsetY + (py - Size / 2) * (2.4 / Size);
            for (int px = 0; px < Size; px++)
            {
                double x0 = offsetX + (px - Size / 2) * (3.0 / Size);
                double x = 0, y = 0;
                int i = 0;
                while (i < MaxIter && x * x + y * y <= 4)
                {
                    double xt = x * x - y * y + x0;
                    y = 2 * x * y + y0;
                    x = xt;
                    i++;
                }
                if (i == MaxIter) inside++;
            }
        }

        uint h = seed * 2654435761u;
        for (int i = 0; i < 4096; i++)
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            if ((h & 3) == 0) h += (uint)i;
            else h = (h * 31) ^ (uint)inside;
        }
        return inside + (h & 0xFF);
    }
}
