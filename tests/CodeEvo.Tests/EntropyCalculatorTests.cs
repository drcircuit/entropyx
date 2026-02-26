using CodeEvo.Core;
using CodeEvo.Core.Models;
using Xunit;

namespace CodeEvo.Tests;

public class EntropyCalculatorTests
{
    // Creates a file whose badness == cc (all other metrics zero)
    private static FileMetrics MakeFile(string path, double cc = 0) =>
        new("hash", path, "CSharp", 0, cc, 0, 0, 0, 0, 0, 0);

    // Creates a file with all badness components specified
    private static FileMetrics MakeFileDetailed(string path,
        double cc = 0, int smellsHigh = 0, int smellsMed = 0, int smellsLow = 0,
        double coupling = 0, double maintProxy = 0) =>
        new("hash", path, "CSharp", 0, cc, 0, smellsHigh, smellsMed, smellsLow, coupling, maintProxy);

    // ── ComputeEntropy ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeEntropy_EmptyList_ReturnsZero()
    {
        var result = EntropyCalculator.ComputeEntropy(Array.Empty<FileMetrics>());
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ComputeEntropy_AllZeroBadness_ReturnsZero()
    {
        // total == 0 → return 0 regardless of file count
        var files = new[] { MakeFile("a.cs"), MakeFile("b.cs"), MakeFile("c.cs") };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ComputeEntropy_SingleFile_ReturnsZero()
    {
        // N == 1 → H_norm = 0 → result = 0 regardless of badness magnitude
        var files = new[] { MakeFile("a.cs", cc: 10) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(0.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_TwoEqualFiles_ReturnsOne()
    {
        // badness = [1, 1], total = 2, p = [0.5, 0.5]
        // H = -(2 × 0.5×log2(0.5)) = 1
        // H_norm = 1 / log2(2) = 1
        // meanBadness = 2 / 2 = 1  →  result = 1 × 1 = 1.0
        var files = new[] { MakeFile("a.cs", cc: 1), MakeFile("b.cs", cc: 1) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(1.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_AllBadnessInOneFile_ReturnsZero()
    {
        // Only one file has non-zero badness → p = [1, 0, 0], H = 0 → result = 0
        var files = new[] { MakeFile("a.cs", cc: 10), MakeFile("b.cs"), MakeFile("c.cs") };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(0.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_UnequalBadness_ScoreIsNonNegative()
    {
        // Unequal badness → 0 < H_norm < 1, meanBadness > 0 → score > 0
        var files = new[]
        {
            MakeFile("a.cs", cc: 10),
            MakeFile("b.cs", cc: 5),
            MakeFile("c.cs", cc: 2),
            MakeFile("d.cs", cc: 1),
        };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.True(result >= 0.0);
    }

    [Fact]
    public void ComputeEntropy_FourEqualFiles_ReturnsOne()
    {
        // badness = [1, 1, 1, 1], p = [0.25, ...], H = log2(4) = 2
        // H_norm = 2 / log2(4) = 1, meanBadness = 1 → result = 1.0
        var files = new[]
        {
            MakeFile("a.cs", cc: 1), MakeFile("b.cs", cc: 1),
            MakeFile("c.cs", cc: 1), MakeFile("d.cs", cc: 1),
        };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(1.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_MagnitudeScaling_ScalesWithMeanBadness()
    {
        // Two equal files with cc=2 each: H_norm=1, meanBadness=2 → result=2.0
        var files = new[] { MakeFile("a.cs", cc: 2), MakeFile("b.cs", cc: 2) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(2.0, result, precision: 10);
    }

    // ── ComputeBadness ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeBadness_AllZero_ReturnsZero()
    {
        var result = EntropyCalculator.ComputeBadness(MakeFile("a.cs"));
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ComputeBadness_CyclomaticComplexity_IsIncluded()
    {
        var result = EntropyCalculator.ComputeBadness(MakeFile("a.cs", cc: 5));
        Assert.Equal(5.0, result, precision: 10);
    }

    [Theory]
    [InlineData(1, 0, 0, 3.0)]  // High smell = weight 3
    [InlineData(0, 1, 0, 2.0)]  // Medium smell = weight 2
    [InlineData(0, 0, 1, 1.0)]  // Low smell = weight 1
    [InlineData(1, 1, 1, 6.0)]  // All severities combined
    public void ComputeBadness_SmellsWeighted(int high, int med, int low, double expected)
    {
        var file = MakeFileDetailed("a.cs", smellsHigh: high, smellsMed: med, smellsLow: low);
        Assert.Equal(expected, EntropyCalculator.ComputeBadness(file), precision: 10);
    }

    [Fact]
    public void ComputeBadness_CouplingAndMaintainabilityProxy_AreIncluded()
    {
        var file = MakeFileDetailed("a.cs", coupling: 2.5, maintProxy: 1.5);
        Assert.Equal(4.0, EntropyCalculator.ComputeBadness(file), precision: 10);
    }

    [Fact]
    public void ComputeBadness_AllComponents_Summed()
    {
        // cc=1, high=1(×3), med=1(×2), low=1(×1), coupling=1, maint=1 → 1+3+2+1+1+1 = 9
        var file = MakeFileDetailed("a.cs", cc: 1, smellsHigh: 1, smellsMed: 1, smellsLow: 1,
            coupling: 1, maintProxy: 1);
        Assert.Equal(9.0, EntropyCalculator.ComputeBadness(file), precision: 10);
    }
}
