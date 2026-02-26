using CodeEvo.Core.Models;

namespace CodeEvo.Core;

public static class EntropyCalculator
{
    public static double ComputeEntropy(IReadOnlyList<FileMetrics> files)
    {
        if (files.Count == 0)
            return 0.0;

        int totalSloc = files.Sum(f => f.Sloc);
        if (totalSloc == 0)
            return 0.0;

        double entropy = 0.0;
        foreach (var file in files)
        {
            if (file.Sloc <= 0)
                continue;
            double p = (double)file.Sloc / totalSloc;
            entropy -= p * Math.Log2(p);
        }

        if (files.Count > 1)
            entropy /= Math.Log2(files.Count);

        return entropy;
    }
}
