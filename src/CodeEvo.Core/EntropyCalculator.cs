using CodeEvo.Core.Models;

namespace CodeEvo.Core;

public static class EntropyCalculator
{
    // Smell severity weights
    private const double HighWeight = 3.0;
    private const double MediumWeight = 2.0;
    private const double LowWeight = 1.0;

    /// <summary>
    /// Computes a magnitude-scaled entropy score measuring how evenly "badness"
    /// (complexity + smells + coupling + inverse maintainability) is distributed
    /// across files. High score = problems spread evenly at high magnitude;
    /// low score = problems absent or concentrated in few files.
    /// </summary>
    public static double ComputeEntropy(IReadOnlyList<FileMetrics> files)
    {
        if (files.Count == 0)
            return 0.0;

        int n = files.Count;
        double[] badness = files.Select(ComputeBadness).ToArray();
        double total = badness.Sum();

        if (total == 0)
            return 0.0;

        // Normalize badness into probability distribution and compute Shannon entropy
        double h = 0.0;
        foreach (var b in badness)
        {
            double p = b / total;
            if (p > 0) h -= p * Math.Log2(p);
        }

        // Normalize entropy to [0, 1]
        double hNorm = n <= 1 ? 0.0 : h / Math.Log2(n);

        // Scale by mean badness so that higher absolute "badness" raises the score
        double meanBadness = total / n;
        return Math.Max(0.0, hNorm * meanBadness);
    }

    /// <summary>
    /// Computes the badness score for a single file.
    /// badness = CyclomaticComplexity
    ///         + (SmellsHigh × 3 + SmellsMedium × 2 + SmellsLow × 1)
    ///         + CouplingProxy
    ///         + MaintainabilityProxy
    /// </summary>
    public static double ComputeBadness(FileMetrics file)
    {
        double smells = file.SmellsHigh * HighWeight
                      + file.SmellsMedium * MediumWeight
                      + file.SmellsLow * LowWeight;
        return file.CyclomaticComplexity + smells + file.CouplingProxy + file.MaintainabilityProxy;
    }
}
