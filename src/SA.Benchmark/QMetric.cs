using SA.Core;

namespace SA.Benchmark;

/// <summary>Єдина метрика якості (методичка, розділ 4): Q = α·S + β·T, Q = 0 при втраті даних.</summary>
public static class QMetric
{
    /// <summary>S = B_orig / (B_orig + B_arc).</summary>
    public static double S(long originalBytes, long archiveBytes)
    {
        long sum = originalBytes + archiveBytes;
        return sum == 0 ? 0.0 : (double)originalBytes / sum;
    }

    /// <summary>T = t_ref / (t_ref + t).</summary>
    public static double T(double tRefSeconds, double seconds) => tRefSeconds / (tRefSeconds + seconds);

    public static double Q(QConfig q, long originalBytes, long archiveBytes, double seconds, bool allRestored)
        => allRestored
            ? q.Alpha * S(originalBytes, archiveBytes) + q.Beta * T(q.TRefSeconds, seconds)
            : 0.0;
}
