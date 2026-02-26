using CodeEvo.Core;
using CodeEvo.Core.Models;
using Xunit;

namespace CodeEvo.Tests;

public class EntropyCalculatorTests
{
    // Creates a FileMetrics with specified values; defaults produce an all-zero-metric file.
    private static FileMetrics MakeFile(string path, double cc = 0, int sloc = 0,
        double mi = 0, int smellsHigh = 0, int smellsMed = 0, int smellsLow = 0,
        double coupling = 0) =>
        new("hash", path, "CSharp", sloc, cc, mi, smellsHigh, smellsMed, smellsLow, coupling, 0);

    // ── ComputeEntropy ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeEntropy_EmptyList_ReturnsZero()
    {
        var result = EntropyCalculator.ComputeEntropy(Array.Empty<FileMetrics>());
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ComputeEntropy_SingleFile_ReturnsZero()
    {
        // Single file: all features normalize to 0 (max==min), M̂=0 → b=WM=1>ε.
        // Active N=1 → spec step 6 returns 0.
        var files = new[] { MakeFile("a.cs", cc: 10) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(0.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_TwoIdenticalFiles_ReturnsWM()
    {
        // CC equal → Ĉ=0 (max==min). MI equal → M̂=0 → (1-M̂)=1.
        // b=[WM, WM]=[1,1]. N=2, p=[0.5,0.5], H=1, H_norm=1, mean=1 → score=1.0.
        var files = new[] { MakeFile("a.cs", cc: 5), MakeFile("b.cs", cc: 5) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(EntropyCalculator.WM, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_FourIdenticalFiles_ReturnsWM()
    {
        // b=[WM,WM,WM,WM]. p=0.25 each, H=log2(4)=2, H_norm=1, mean=WM=1 → 1.0.
        var files = new[]
        {
            MakeFile("a.cs", cc: 1), MakeFile("b.cs", cc: 1),
            MakeFile("c.cs", cc: 1), MakeFile("d.cs", cc: 1),
        };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(EntropyCalculator.WM, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_EpsilonFilterReducesNToOne_ReturnsZero()
    {
        // In this two-file scenario MI=[100,0]: MI=100 is max → M̂=1 → (1-M̂)=0 → b=0 ≤ ε → excluded.
        // File b: MI=0 → M̂=0 → (1-M̂)=1 → b=WM=1 > ε → only active file.
        // N=1 → return 0.
        var files = new[] { MakeFile("a.cs", mi: 100), MakeFile("b.cs", mi: 0) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(0.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_UnequalBadness_ScoreIsNonNegative()
    {
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
    public void ComputeEntropy_TwoFiles_DifferentSloc_ExactValue()
    {
        // File a: SLOC=1 → L'=ln(2); File b: SLOC=0 → L'=0. All other metrics zero/equal.
        // L̂'=[1,0]. M̂=[0,0] → (1-M̂)=[1,1].
        // b=[WL+WM, WM]=[2,1]. N=2, total=3, p=[2/3,1/3].
        // H = -(2/3·log2(2/3) + 1/3·log2(1/3)) = log2(3) - 2/3.
        // H_norm = H/log2(2) = H. meanBadness = 3/2.
        // score = (log2(3) - 2/3) * 3/2.
        var files = new[] { MakeFile("a.cs", sloc: 1), MakeFile("b.cs", sloc: 0) };
        double expected = (Math.Log2(3) - 2.0 / 3.0) * 1.5;
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(expected, result, precision: 10);
    }

    // ── ComputeBadness ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeBadness_EmptyList_ReturnsEmptyArray()
    {
        var result = EntropyCalculator.ComputeBadness(Array.Empty<FileMetrics>());
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeBadness_SingleFile_AllFeaturesNormalizeToZeroMContributesWM()
    {
        // With 1 file every feature has max==min → all normalized to 0.
        // M̂=0 → (1-M̂)=1 → b = WM * 1.
        var files = new[] { MakeFile("a.cs", cc: 5, sloc: 100, mi: 80, coupling: 2.5) };
        var result = EntropyCalculator.ComputeBadness(files);
        Assert.Single(result);
        Assert.Equal(EntropyCalculator.WM, result[0], precision: 10);
    }

    [Fact]
    public void ComputeBadness_TwoFiles_CyclomaticNormalizationCorrect()
    {
        // C=[10,2], min=2, max=10, range=8. Ĉ=[(10-2)/8,(2-2)/8]=[1,0].
        // M equal → M̂=0 → (1-M̂)=1 for both.
        // b=[WC+WM, WC*0+WM]=[WC+WM, WM]=[2,1].
        var files = new[] { MakeFile("a.cs", cc: 10), MakeFile("b.cs", cc: 2) };
        var result = EntropyCalculator.ComputeBadness(files);
        Assert.Equal(2, result.Length);
        Assert.Equal(EntropyCalculator.WC + EntropyCalculator.WM, result[0], precision: 10);
        Assert.Equal(EntropyCalculator.WM, result[1], precision: 10);
    }

    [Fact]
    public void ComputeBadness_TwoFiles_MaintainabilityIndexInverted()
    {
        // High MI = good = lower badness.  M=[80,20], M̂=[1,0], (1-M̂)=[0,1].
        // All other features equal → b=[WM*0, WM*1]=[0, WM]=[0,1].
        var files = new[] { MakeFile("a.cs", mi: 80), MakeFile("b.cs", mi: 20) };
        var result = EntropyCalculator.ComputeBadness(files);
        Assert.Equal(0.0, result[0], precision: 10);
        Assert.Equal(EntropyCalculator.WM, result[1], precision: 10);
    }

    [Fact]
    public void ComputeBadness_SlocIsLogTransformed()
    {
        // L'=[ln(1+0), ln(1+1)]=[0, ln2]. L̂'=[0,1].
        // M equal → (1-M̂)=1 for both.
        // b=[WM, WL+WM]=[1, 2].
        var files = new[] { MakeFile("a.cs", sloc: 0), MakeFile("b.cs", sloc: 1) };
        var result = EntropyCalculator.ComputeBadness(files);
        Assert.Equal(EntropyCalculator.WM, result[0], precision: 10);
        Assert.Equal(EntropyCalculator.WL + EntropyCalculator.WM, result[1], precision: 10);
    }

    [Fact]
    public void ComputeBadness_SmellsWeightedBeforeNormalization()
    {
        // File a: H=1,M=1,L=1 → raw S=6; File b: H=0,M=0,L=1 → raw S=1.
        // S=[6,1], min=1, max=6, range=5. Ŝ=[(6-1)/5,(1-1)/5]=[1,0].
        // M equal → (1-M̂)=1. b=[WS+WM, WM]=[2,1].
        var files = new[]
        {
            MakeFile("a.cs", smellsHigh: 1, smellsMed: 1, smellsLow: 1),
            MakeFile("b.cs", smellsLow: 1),
        };
        var result = EntropyCalculator.ComputeBadness(files);
        Assert.Equal(EntropyCalculator.WS + EntropyCalculator.WM, result[0], precision: 10);
        Assert.Equal(EntropyCalculator.WM, result[1], precision: 10);
    }

    [Fact]
    public void ComputeBadness_EqualFeatureValues_ContributeZeroForThatFeature()
    {
        // Both files have same CC and same coupling → those features normalize to 0.
        // Only M term contributes: b=[WM,WM]=[1,1].
        var files = new[] { MakeFile("a.cs", cc: 5, coupling: 3), MakeFile("b.cs", cc: 5, coupling: 3) };
        var result = EntropyCalculator.ComputeBadness(files);
        Assert.Equal(EntropyCalculator.WM, result[0], precision: 10);
        Assert.Equal(EntropyCalculator.WM, result[1], precision: 10);
    }
}
