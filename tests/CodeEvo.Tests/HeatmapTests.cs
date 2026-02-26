using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using Xunit;

namespace CodeEvo.Tests;

public class HeatmapTests
{
    private static FileMetrics MakeFile(string path, double cc = 0, int sloc = 0,
        double mi = 0, int smellsHigh = 0, int smellsMed = 0, int smellsLow = 0,
        double coupling = 0) =>
        new("hash", path, "CSharp", sloc, cc, mi, smellsHigh, smellsMed, smellsLow, coupling, 0);

    // ── TrafficLightHex ───────────────────────────────────────────────────────

    [Fact]
    public void TrafficLightHex_ZeroIsGreen()
    {
        // t=0 should produce pure green (#00C800)
        var hex = ConsoleReporter.TrafficLightHex(0.0);
        Assert.Equal("#00C800", hex, ignoreCase: true);
    }

    [Fact]
    public void TrafficLightHex_OneIsRed()
    {
        // t=1 should produce pure red (#C80000)
        var hex = ConsoleReporter.TrafficLightHex(1.0);
        Assert.Equal("#C80000", hex, ignoreCase: true);
    }

    [Fact]
    public void TrafficLightHex_HalfIsYellow()
    {
        // t=0.5 should be yellow (#C8C800)
        var hex = ConsoleReporter.TrafficLightHex(0.5);
        Assert.Equal("#C8C800", hex, ignoreCase: true);
    }

    [Fact]
    public void TrafficLightHex_ClampsBelowZero()
    {
        // Negative values should be treated as 0 (green)
        var hex = ConsoleReporter.TrafficLightHex(-5.0);
        Assert.Equal("#00C800", hex, ignoreCase: true);
    }

    [Fact]
    public void TrafficLightHex_ClampsAboveOne()
    {
        // Values > 1 should be treated as 1 (red)
        var hex = ConsoleReporter.TrafficLightHex(99.0);
        Assert.Equal("#C80000", hex, ignoreCase: true);
    }

    // ── IrColor (HeatmapImageGenerator) ──────────────────────────────────────

    [Fact]
    public void IrColor_ZeroIsBlack()
    {
        var color = HeatmapImageGenerator.IrColor(0f);
        var px = (SixLabors.ImageSharp.PixelFormats.Rgba32)color;
        Assert.Equal(0, px.R);
        Assert.Equal(0, px.G);
        Assert.Equal(0, px.B);
    }

    [Fact]
    public void IrColor_OneIsWhite()
    {
        var color = HeatmapImageGenerator.IrColor(1f);
        var px = (SixLabors.ImageSharp.PixelFormats.Rgba32)color;
        Assert.Equal(255, px.R);
        Assert.Equal(255, px.G);
        Assert.Equal(255, px.B);
    }

    [Fact]
    public void IrColor_MidpointIsGreen()
    {
        // The palette has a green stop at t=0.50
        var color = HeatmapImageGenerator.IrColor(0.5f);
        var px = (SixLabors.ImageSharp.PixelFormats.Rgba32)color;
        // Green dominant at t=0.5
        Assert.True(px.G >= px.R, $"Expected G({px.G}) >= R({px.R})");
        Assert.True(px.G >= px.B, $"Expected G({px.G}) >= B({px.B})");
    }

    [Fact]
    public void IrColor_ClampsNegative()
    {
        var colorNeg  = HeatmapImageGenerator.IrColor(-1f);
        var colorZero = HeatmapImageGenerator.IrColor(0f);
        Assert.Equal(colorZero, colorNeg);
    }

    [Fact]
    public void IrColor_ClampsAboveOne()
    {
        var colorHigh = HeatmapImageGenerator.IrColor(2f);
        var colorOne  = HeatmapImageGenerator.IrColor(1f);
        Assert.Equal(colorOne, colorHigh);
    }

    // ── Generate (smoke test) ─────────────────────────────────────────────────

    [Fact]
    public void Generate_EmptyFiles_DoesNotCreateFile()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"heatmap_test_{Guid.NewGuid()}.png");
        try
        {
            HeatmapImageGenerator.Generate([], Array.Empty<double>(), tmpPath);
            Assert.False(File.Exists(tmpPath));
        }
        finally
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
    }

    [Fact]
    public void Generate_WithFiles_CreatesPngFile()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"heatmap_test_{Guid.NewGuid()}.png");
        try
        {
            var files = new[]
            {
                MakeFile("src/foo.cs", cc: 10, sloc: 200, coupling: 5),
                MakeFile("src/bar.cs", cc:  2, sloc:  40, coupling: 1),
                MakeFile("src/baz.cs", cc:  7, sloc: 120, coupling: 3),
            };
            double[] badness = EntropyCalculator.ComputeBadness(files);
            HeatmapImageGenerator.Generate(files, badness, tmpPath);
            Assert.True(File.Exists(tmpPath), "PNG file should be created");
            Assert.True(new FileInfo(tmpPath).Length > 0, "PNG file should not be empty");
        }
        finally
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
    }
}
