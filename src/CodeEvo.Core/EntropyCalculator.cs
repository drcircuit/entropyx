using CodeEvo.Core.Models;

namespace CodeEvo.Core;

public static class EntropyCalculator
{
    // Smell severity weights applied before normalization
    public const double SmellHighWeight = 3.0;
    public const double SmellMediumWeight = 2.0;
    public const double SmellLowWeight = 1.0;

    // Feature weights (equal by default; public for testability)
    public const double WL = 1.0; // log-SLOC
    public const double WC = 1.0; // cyclomatic complexity
    public const double WS = 1.0; // smells
    public const double WU = 1.0; // coupling
    public const double WM = 1.0; // inverse maintainability

    private const double Epsilon = 1e-9;

    /// <summary>
    /// Computes the EntropyX score for a commit following the exact spec:
    ///   1. L' = ln(1 + SLOC)
    ///   2. Min-max normalize {L', C, S, U, M} within the commit (max==min → 0)
    ///   3. b_i = wL·L̂' + wC·Ĉ + wS·Ŝ + wU·Û + wM·(1 − M̂)
    ///   4–6. Filter files with b_i > ε; if N ≤ 1 or Σb ≤ ε → 0
    ///   7–12. Shannon H base-2, normalize by log₂(N), scale by meanBadness
    ///   13. Clamp ≥ 0
    /// High score = problems spread evenly at high magnitude.
    /// </summary>
    public static double ComputeEntropy(IReadOnlyList<FileMetrics> files)
    {
        if (files.Count == 0)
            return 0.0;

        double[] badness = ComputeBadness(files);

        // Step 5: keep only files whose badness exceeds epsilon
        double[] active = badness.Where(b => b > Epsilon).ToArray();
        int n = active.Length;
        double total = active.Sum();

        // Step 6: degenerate cases
        if (n <= 1 || total <= Epsilon)
            return 0.0;

        // Steps 7–8: Shannon entropy
        double h = 0.0;
        foreach (var b in active)
        {
            double p = b / total;
            h -= p * Math.Log2(p);
        }

        // Steps 9–10: normalize to [0, 1]
        double hNorm = h / Math.Log2(n);

        // Steps 11–12: magnitude scaling
        double meanBadness = total / n;

        // Step 13: clamp
        return Math.Max(0.0, hNorm * meanBadness);
    }

    /// <summary>
    /// Computes per-file badness for a commit using commit-level min-max normalization.
    /// Features: L' = ln(1+SLOC), C = CyclomaticComplexity,
    ///           S = weighted smells, U = CouplingProxy, M = MaintainabilityIndex.
    /// b_i = wL·L̂'_i + wC·Ĉ_i + wS·Ŝ_i + wU·Û_i + wM·(1 − M̂_i)
    /// </summary>
    public static double[] ComputeBadness(IReadOnlyList<FileMetrics> files)
    {
        if (files.Count == 0)
            return Array.Empty<double>();

        double[] lPrime = files.Select(f => Math.Log(1.0 + f.Sloc)).ToArray();
        double[] c      = files.Select(f => f.CyclomaticComplexity).ToArray();
        double[] s      = files.Select(f => RawSmells(f)).ToArray();
        double[] u      = files.Select(f => f.CouplingProxy).ToArray();
        double[] m      = files.Select(f => f.MaintainabilityIndex).ToArray();

        double[] lHat = MinMaxNormalize(lPrime);
        double[] cHat = MinMaxNormalize(c);
        double[] sHat = MinMaxNormalize(s);
        double[] uHat = MinMaxNormalize(u);
        double[] mHat = MinMaxNormalize(m);

        var result = new double[files.Count];
        for (int i = 0; i < files.Count; i++)
            result[i] = WL * lHat[i] + WC * cHat[i] + WS * sHat[i]
                      + WU * uHat[i] + WM * (1.0 - mHat[i]);
        return result;
    }

    /// <summary>
    /// Computes per-file refactor priority scores based on the specified focus metrics.
    /// <paramref name="focus"/> may be <c>"overall"</c> (default, uses the full badness formula) or
    /// any comma-separated combination of <c>sloc</c>, <c>cc</c>, <c>mi</c>, <c>smells</c>, <c>coupling</c>.
    /// Returns scores in the same order as <paramref name="files"/>; higher score = higher refactor priority.
    /// </summary>
    public static double[] ComputeRefactorScores(IReadOnlyList<FileMetrics> files, string focus = "overall")
    {
        if (files.Count == 0)
            return Array.Empty<double>();

        var focusMetrics = focus
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToLowerInvariant())
            .ToHashSet();

        if (focusMetrics.Count == 0 || focusMetrics.Contains("overall"))
            return ComputeBadness(files);

        var components = new List<double[]>();

        if (focusMetrics.Contains("sloc"))
            components.Add(MinMaxNormalize(files.Select(f => (double)f.Sloc).ToArray()));
        if (focusMetrics.Contains("cc"))
            components.Add(MinMaxNormalize(files.Select(f => f.CyclomaticComplexity).ToArray()));
        if (focusMetrics.Contains("mi"))
        {
            // Lower MI = worse maintainability → invert so high score = bad
            var normalized = MinMaxNormalize(files.Select(f => f.MaintainabilityIndex).ToArray());
            components.Add(normalized.Select(v => 1.0 - v).ToArray());
        }
        if (focusMetrics.Contains("smells"))
            components.Add(MinMaxNormalize(files.Select(RawSmells).ToArray()));
        if (focusMetrics.Contains("coupling"))
            components.Add(MinMaxNormalize(files.Select(f => f.CouplingProxy).ToArray()));

        // Fallback to overall if no recognised metric was specified
        if (components.Count == 0)
            return ComputeBadness(files);

        var result = new double[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            double sum = 0.0;
            foreach (var comp in components)
                sum += comp[i];
            result[i] = sum / components.Count;
        }
        return result;
    }

    // Weighted smells score used as raw input before normalization
    private static double RawSmells(FileMetrics f) =>
        f.SmellsHigh * SmellHighWeight + f.SmellsMedium * SmellMediumWeight + f.SmellsLow * SmellLowWeight;

    // Min-max normalize to [0, 1]; all zeros when max == min (per spec)
    private static double[] MinMaxNormalize(double[] values)
    {
        double min = values.Min();
        double max = values.Max();
        double range = max - min;
        if (range == 0.0)
            return new double[values.Length];
        return values.Select(v => (v - min) / range).ToArray();
    }
}
