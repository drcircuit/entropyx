using CodeEvo.Core;
using CodeEvo.Core.Models;
using Xunit;

namespace CodeEvo.Tests;

public class EntropyCalculatorTests
{
    private static FileMetrics MakeFile(string path, int sloc) =>
        new("hash", path, "CSharp", sloc, 0, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void ComputeEntropy_EmptyList_ReturnsZero()
    {
        var result = EntropyCalculator.ComputeEntropy(Array.Empty<FileMetrics>());
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ComputeEntropy_SingleFile_ReturnsZero()
    {
        // With one file, p=1 => -1*log2(1)=0; no normalization (count<=1)
        var files = new[] { MakeFile("a.cs", 100) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(0.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_TwoEqualFiles_ReturnsOne()
    {
        // Two files with equal SLOC: p=0.5 each
        // entropy = -(0.5*log2(0.5) + 0.5*log2(0.5)) = 1.0
        // normalized by log2(2) = 1 => result = 1.0
        var files = new[] { MakeFile("a.cs", 50), MakeFile("b.cs", 50) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(1.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_AllSlocInOneFile_ReturnsZero()
    {
        // One file has all the SLOC, others have 0
        var files = new[] { MakeFile("a.cs", 100), MakeFile("b.cs", 0), MakeFile("c.cs", 0) };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(0.0, result, precision: 10);
    }

    [Fact]
    public void ComputeEntropy_MultipleFiles_NormalizedBetweenZeroAndOne()
    {
        var files = new[]
        {
            MakeFile("a.cs", 100),
            MakeFile("b.cs", 50),
            MakeFile("c.cs", 25),
            MakeFile("d.cs", 25)
        };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.InRange(result, 0.0, 1.0);
    }

    [Fact]
    public void ComputeEntropy_FourEqualFiles_ReturnsOne()
    {
        // Four files with equal SLOC => max entropy => normalized = 1
        var files = new[]
        {
            MakeFile("a.cs", 25),
            MakeFile("b.cs", 25),
            MakeFile("c.cs", 25),
            MakeFile("d.cs", 25)
        };
        var result = EntropyCalculator.ComputeEntropy(files);
        Assert.Equal(1.0, result, precision: 10);
    }
}
