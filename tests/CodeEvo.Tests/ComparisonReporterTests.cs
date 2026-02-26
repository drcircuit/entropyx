using CodeEvo.Reporting;
using Xunit;

namespace CodeEvo.Tests;

public class ComparisonReporterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DataJsonReport MakeReport(
        double entropy, int files = 5, int sloc = 100, int commits = 3,
        IReadOnlyList<DataJsonHistoryEntry>? history = null,
        IReadOnlyList<DataJsonFileEntry>? latestFiles = null,
        string generated = "2024-01-01T00:00:00+00:00") =>
        new()
        {
            Generated   = generated,
            CommitCount = commits,
            Summary     = new DataJsonSummary(entropy, files, sloc),
            History     = history ?? [
                new("a", "2024-01-01", entropy * 0.8, files, sloc),
                new("b", "2024-01-02", entropy * 0.9, files, sloc),
                new("c", "2024-01-03", entropy,        files, sloc),
            ],
            LatestFiles = latestFiles ?? [],
        };

    private static DataJsonFileEntry MakeFile(string path, double badness = 0.5, int sloc = 100,
        double cc = 5.0, double mi = 75.0, int smHigh = 0, int smMed = 0, int smLow = 0) =>
        new(path, "CSharp", sloc, cc, mi, smHigh, smMed, smLow, badness);

    // ── DataJsonReport.Parse ──────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidJson_ReturnsCorrectReport()
    {
        var json = """
            {
              "generated": "2024-06-01T12:00:00+00:00",
              "commitCount": 5,
              "summary": { "entropy": 1.2345, "files": 10, "sloc": 500 },
              "history": [
                { "hash": "abc", "date": "2024-01-01", "entropy": 1.0, "files": 8, "sloc": 400 },
                { "hash": "def", "date": "2024-06-01", "entropy": 1.2345, "files": 10, "sloc": 500 }
              ],
              "latestFiles": [
                {
                  "path": "src/Foo.cs", "language": "CSharp", "sloc": 200,
                  "cyclomaticComplexity": 8.5, "maintainabilityIndex": 72.3,
                  "smellsHigh": 1, "smellsMedium": 2, "smellsLow": 3, "badness": 0.42
                }
              ]
            }
            """;

        var report = DataJsonReport.Parse(json);

        Assert.Equal("2024-06-01T12:00:00+00:00", report.Generated);
        Assert.Equal(5, report.CommitCount);
        Assert.NotNull(report.Summary);
        Assert.Equal(1.2345, report.Summary.Entropy);
        Assert.Equal(10, report.Summary.Files);
        Assert.Equal(500, report.Summary.Sloc);
        Assert.Equal(2, report.History.Count);
        Assert.Equal("abc", report.History[0].Hash);
        Assert.Equal(1.0, report.History[0].Entropy);
        Assert.Single(report.LatestFiles);
        Assert.Equal("src/Foo.cs", report.LatestFiles[0].Path);
        Assert.Equal(8.5, report.LatestFiles[0].CyclomaticComplexity);
        Assert.Equal(0.42, report.LatestFiles[0].Badness);
    }

    [Fact]
    public void Parse_EmptyHistoryAndFiles_ReturnsValidReport()
    {
        var json = """
            {
              "generated": "2024-01-01T00:00:00+00:00",
              "commitCount": 0,
              "history": [],
              "latestFiles": []
            }
            """;

        var report = DataJsonReport.Parse(json);

        Assert.Equal(0, report.CommitCount);
        Assert.Null(report.Summary);
        Assert.Empty(report.History);
        Assert.Empty(report.LatestFiles);
    }

    [Fact]
    public void Parse_MissingSummary_SummaryIsNull()
    {
        var json = """{ "commitCount": 2, "history": [], "latestFiles": [] }""";
        var report = DataJsonReport.Parse(json);
        Assert.Null(report.Summary);
    }

    // ── BuildAssessment – weather forecast verdicts ───────────────────────────

    [Fact]
    public void BuildAssessment_FlatHistory_VerdictStable()
    {
        // Perfectly flat history → no trend, tiny delta
        var flatHistory = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 1.0, 5, 100),
            new("b", "2024-01-02", 1.05, 5, 100),
            new("c", "2024-01-03", 0.95, 5, 100),
            new("d", "2024-01-04", 1.0, 5, 100),
        };
        var baseline = MakeReport(entropy: 1.0);
        var current  = MakeReport(entropy: 1.0, history: flatHistory);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Equal(Verdict.Stable, result.Verdict);
        Assert.Contains("Stable", result.VerdictLabel);
    }

    [Fact]
    public void BuildAssessment_SteadilyRisingEntropy_VerdictWarming()
    {
        // Current history shows steady upward trend; overall delta > 0.01
        var warmingHistory = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.5, 5, 100),
            new("b", "2024-01-02", 0.8, 5, 100),
            new("c", "2024-01-03", 1.1, 5, 100),
            new("d", "2024-01-04", 1.4, 5, 100),
        };
        var baseline = MakeReport(entropy: 0.5);
        var current  = MakeReport(entropy: 1.4, history: warmingHistory);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Equal(Verdict.Warming, result.Verdict);
        Assert.Contains("Warming", result.VerdictLabel);
    }

    [Fact]
    public void BuildAssessment_SteadilyFallingEntropy_VerdictCooling()
    {
        // Declining trend with one small blip — not a clean ColdFront (all-declining) pattern
        var coolingHistory = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 2.0, 5, 100),
            new("b", "2024-01-02", 1.7, 5, 100),
            new("c", "2024-01-03", 1.5, 5, 100),
            new("d", "2024-01-04", 1.6, 5, 100),  // small blip up: ColdFront requires ≥70% declining
            new("e", "2024-01-05", 1.1, 5, 100),
        };
        var baseline = MakeReport(entropy: 2.0);
        var current  = MakeReport(entropy: 1.1, history: coolingHistory);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Equal(Verdict.Cooling, result.Verdict);
        Assert.Contains("Cooling", result.VerdictLabel);
    }

    [Fact]
    public void BuildAssessment_SharpEntropySpike_VerdictHeatSpike()
    {
        // Five flat commits then one huge jump → DetectHeatSpike = true
        var spikeHistory = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.5, 5, 100),
            new("b", "2024-01-02", 0.5, 5, 100),
            new("c", "2024-01-03", 0.5, 5, 100),
            new("d", "2024-01-04", 0.5, 5, 100),
            new("e", "2024-01-05", 2.5, 5, 100),
        };
        var baseline = MakeReport(entropy: 0.5);
        var current  = MakeReport(entropy: 2.5, history: spikeHistory);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Equal(Verdict.HeatSpike, result.Verdict);
        Assert.Contains("Heat Spike", result.VerdictLabel);
    }

    [Fact]
    public void BuildAssessment_SustainedElevation_VerdictHeatWave()
    {
        // Two low points, then elevation jumps and STAYS flat (stable elevated window)
        var heatWaveHistory = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.4, 5, 100),
            new("b", "2024-01-02", 0.4, 5, 100),
            new("c", "2024-01-03", 1.5, 5, 100),
            new("d", "2024-01-04", 1.6, 5, 100),
            new("e", "2024-01-05", 1.5, 5, 100),
        };
        var baseline = MakeReport(entropy: 0.4);
        var current  = MakeReport(entropy: 1.5, history: heatWaveHistory);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Equal(Verdict.HeatWave, result.Verdict);
        Assert.Contains("Heat Wave", result.VerdictLabel);
    }

    [Fact]
    public void BuildAssessment_SustainedDropAfterHigh_VerdictColdFront()
    {
        // Entropy was high then dropped consistently over several commits
        var coldFrontHistory = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 2.0, 5, 100),
            new("b", "2024-01-02", 1.7, 5, 100),
            new("c", "2024-01-03", 1.4, 5, 100),
            new("d", "2024-01-04", 1.1, 5, 100),
            new("e", "2024-01-05", 0.8, 5, 100),
        };
        var baseline = MakeReport(entropy: 2.0);
        var current  = MakeReport(entropy: 0.8, history: coldFrontHistory);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Equal(Verdict.ColdFront, result.Verdict);
        Assert.Contains("Cold Front", result.VerdictLabel);
    }

    [Fact]
    public void BuildAssessment_ContainsEntropyObservation()
    {
        var baseline = MakeReport(entropy: 1.0);
        var current  = MakeReport(entropy: 1.5);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.NotEmpty(result.Observations);
        Assert.Contains(result.Observations, o => o.Contains("EntropyX"));
    }

    // ── DetectHeatSpike ───────────────────────────────────────────────────────

    [Fact]
    public void DetectHeatSpike_FlatThenBigJump_ReturnsTrue()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.5, 5, 100),
            new("b", "2024-01-02", 0.5, 5, 100),
            new("c", "2024-01-03", 0.5, 5, 100),
            new("d", "2024-01-04", 0.5, 5, 100),
            new("e", "2024-01-05", 2.5, 5, 100),
        };
        Assert.True(ComparisonReporter.DetectHeatSpike(history));
    }

    [Fact]
    public void DetectHeatSpike_SteadyRise_ReturnsFalse()
    {
        // Uniform rise — no single outlier step
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.5, 5, 100),
            new("b", "2024-01-02", 0.8, 5, 100),
            new("c", "2024-01-03", 1.1, 5, 100),
            new("d", "2024-01-04", 1.4, 5, 100),
        };
        Assert.False(ComparisonReporter.DetectHeatSpike(history));
    }

    [Fact]
    public void DetectHeatSpike_TooShortHistory_ReturnsFalse()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 1.0, 5, 100),
            new("b", "2024-01-02", 3.0, 5, 100),
        };
        Assert.False(ComparisonReporter.DetectHeatSpike(history));
    }

    // ── DetectHeatWave ────────────────────────────────────────────────────────

    [Fact]
    public void DetectHeatWave_LastThreeElevatedAndStable_ReturnsTrue()
    {
        // 2 low entries then 3 entries at a stable elevated level (needs 5 entries for minWindow+2=5)
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.4, 5, 100),
            new("b", "2024-01-02", 0.4, 5, 100),
            new("c", "2024-01-03", 1.5, 5, 100),
            new("d", "2024-01-04", 1.6, 5, 100),
            new("e", "2024-01-05", 1.5, 5, 100),
        };
        Assert.True(ComparisonReporter.DetectHeatWave(history));
    }

    [Fact]
    public void DetectHeatWave_StillClimbingInWindow_ReturnsFalse()
    {
        // All points rise uniformly — not a stable elevated plateau (window still climbing)
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.4, 5, 100),
            new("b", "2024-01-02", 0.7, 5, 100),
            new("c", "2024-01-03", 1.0, 5, 100),
            new("d", "2024-01-04", 1.3, 5, 100),
            new("e", "2024-01-05", 1.6, 5, 100),
        };
        Assert.False(ComparisonReporter.DetectHeatWave(history));
    }

    [Fact]
    public void DetectHeatWave_LastEntryDropsBelow_ReturnsFalse()
    {
        // Same 5-entry setup but last entry drops back near reference level
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.4, 5, 100),
            new("b", "2024-01-02", 0.4, 5, 100),
            new("c", "2024-01-03", 1.5, 5, 100),
            new("d", "2024-01-04", 1.6, 5, 100),
            new("e", "2024-01-05", 0.45, 5, 100),  // drops back to near-reference
        };
        Assert.False(ComparisonReporter.DetectHeatWave(history));
    }

    [Fact]
    public void DetectHeatWave_TooShortHistory_ReturnsFalse()
    {
        // minWindow=3 requires Count >= minWindow+2 = 5; only 4 entries here
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.5, 5, 100),
            new("b", "2024-01-02", 1.5, 5, 100),
            new("c", "2024-01-03", 1.5, 5, 100),
            new("d", "2024-01-04", 1.5, 5, 100),
        };
        Assert.False(ComparisonReporter.DetectHeatWave(history));
    }

    // ── DetectColdFront ───────────────────────────────────────────────────────

    [Fact]
    public void DetectColdFront_ConsistentDescent_ReturnsTrue()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 2.0, 5, 100),
            new("b", "2024-01-02", 1.7, 5, 100),
            new("c", "2024-01-03", 1.4, 5, 100),
            new("d", "2024-01-04", 1.1, 5, 100),
        };
        Assert.True(ComparisonReporter.DetectColdFront(history));
    }

    [Fact]
    public void DetectColdFront_RisingHistory_ReturnsFalse()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 0.5, 5, 100),
            new("b", "2024-01-02", 0.8, 5, 100),
            new("c", "2024-01-03", 1.1, 5, 100),
            new("d", "2024-01-04", 1.4, 5, 100),
        };
        Assert.False(ComparisonReporter.DetectColdFront(history));
    }

    [Fact]
    public void DetectColdFront_TooShortHistory_ReturnsFalse()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 2.0, 5, 100),
            new("b", "2024-01-02", 1.5, 5, 100),
            new("c", "2024-01-03", 1.0, 5, 100),
        };
        // minWindow=3, need Count >= 4
        Assert.False(ComparisonReporter.DetectColdFront(history));
    }

    [Fact]
    public void BuildAssessment_SlocGrowthWithEntropyImprovement_ReflectedInObservations()
    {
        var baseline = MakeReport(entropy: 2.0, sloc: 500);
        var current  = MakeReport(entropy: 1.5, sloc: 700);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Contains(result.Observations, o => o.Contains("200") && o.Contains("SLOC"));
    }

    [Fact]
    public void BuildAssessment_NewFilesDetected_ReflectedInObservations()
    {
        var baseFiles = new List<DataJsonFileEntry> { MakeFile("src/A.cs") };
        var curFiles  = new List<DataJsonFileEntry> { MakeFile("src/A.cs"), MakeFile("src/B.cs") };
        var baseline  = MakeReport(entropy: 1.0, files: 1, latestFiles: baseFiles);
        var current   = MakeReport(entropy: 1.0, files: 2, latestFiles: curFiles);

        var result = ComparisonReporter.BuildAssessment(baseline, current);

        Assert.Contains(result.Observations, o => o.Contains("new file") || o.Contains("file(s) added"));
    }

    // ── File diff helpers ─────────────────────────────────────────────────────

    [Fact]
    public void GetNewFiles_FileNotInBaseline_ReturnsIt()
    {
        var baseline = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs")]);
        var current  = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs"), MakeFile("src/B.cs")]);

        var newFiles = ComparisonReporter.GetNewFiles(baseline, current);

        Assert.Single(newFiles);
        Assert.Equal("src/B.cs", newFiles[0].Path);
    }

    [Fact]
    public void GetRemovedFiles_FileNotInCurrent_ReturnsIt()
    {
        var baseline = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs"), MakeFile("src/B.cs")]);
        var current  = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs")]);

        var removed = ComparisonReporter.GetRemovedFiles(baseline, current);

        Assert.Single(removed);
        Assert.Equal("src/B.cs", removed[0].Path);
    }

    [Fact]
    public void GetWorsenedFiles_BadnessIncreased_ReturnsFile()
    {
        var baseline = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs", badness: 0.3)]);
        var current  = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs", badness: 0.8)]);

        var worsened = ComparisonReporter.GetWorsenedFiles(baseline, current);

        Assert.Single(worsened);
        Assert.Equal("src/A.cs", worsened[0].Path);
    }

    [Fact]
    public void GetImprovedFiles_BadnessDecreased_ReturnsFile()
    {
        var baseline = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs", badness: 0.8)]);
        var current  = MakeReport(1.0, latestFiles: [MakeFile("src/A.cs", badness: 0.3)]);

        var improved = ComparisonReporter.GetImprovedFiles(baseline, current);

        Assert.Single(improved);
        Assert.Equal("src/A.cs", improved[0].Path);
    }

    [Fact]
    public void GetWorsenedFiles_NoChange_ReturnsEmpty()
    {
        var f = MakeFile("src/A.cs", badness: 0.5);
        var baseline = MakeReport(1.0, latestFiles: [f]);
        var current  = MakeReport(1.0, latestFiles: [f]);

        var worsened = ComparisonReporter.GetWorsenedFiles(baseline, current);

        Assert.Empty(worsened);
    }

    // ── ComputeEntropyTrend ───────────────────────────────────────────────────

    [Fact]
    public void ComputeEntropyTrend_UpwardHistory_ReturnsPositiveSlope()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 1.0, 5, 100),
            new("b", "2024-01-02", 1.5, 5, 100),
            new("c", "2024-01-03", 2.0, 5, 100),
        };

        double trend = ComparisonReporter.ComputeEntropyTrend(history);

        Assert.True(trend > 0);
        Assert.Equal(0.5, trend, precision: 10);
    }

    [Fact]
    public void ComputeEntropyTrend_DownwardHistory_ReturnsNegativeSlope()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 2.0, 5, 100),
            new("b", "2024-01-02", 1.5, 5, 100),
            new("c", "2024-01-03", 1.0, 5, 100),
        };

        double trend = ComparisonReporter.ComputeEntropyTrend(history);

        Assert.True(trend < 0);
        Assert.Equal(-0.5, trend, precision: 10);
    }

    [Fact]
    public void ComputeEntropyTrend_SingleEntry_ReturnsZero()
    {
        var history = new List<DataJsonHistoryEntry>
        {
            new("a", "2024-01-01", 1.5, 5, 100),
        };

        double trend = ComparisonReporter.ComputeEntropyTrend(history);

        Assert.Equal(0, trend);
    }

    [Fact]
    public void ComputeEntropyTrend_EmptyHistory_ReturnsZero()
    {
        double trend = ComparisonReporter.ComputeEntropyTrend([]);
        Assert.Equal(0, trend);
    }

    // ── GenerateHtml ──────────────────────────────────────────────────────────

    [Fact]
    public void GenerateHtml_EmptyInputs_ReturnsValidHtml()
    {
        var reporter = new ComparisonReporter();
        var html = reporter.GenerateHtml(MakeReport(0), MakeReport(0));

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("EntropyX", html);
    }

    [Fact]
    public void GenerateHtml_ContainsRequiredSections()
    {
        var reporter = new ComparisonReporter();
        var baseline = MakeReport(1.0, sloc: 200, files: 5, commits: 10);
        var current  = MakeReport(1.5, sloc: 300, files: 7, commits: 15);

        var html = reporter.GenerateHtml(baseline, current);

        Assert.Contains("Prime Indicator",        html);
        Assert.Contains("Evolutionary Assessment", html);
        Assert.Contains("Metrics Overview",        html);
        Assert.Contains("Entropy Trend Comparison",html);
        Assert.Contains("File-Level Changes",      html);
        Assert.Contains("Drift Forecast",          html);  // legend section
        Assert.Contains("chart.js",                html);
    }

    [Fact]
    public void GenerateHtml_ContainsWeatherLegendAllConditions()
    {
        var reporter = new ComparisonReporter();
        var html = reporter.GenerateHtml(MakeReport(1.0), MakeReport(1.5));

        // Every weather condition name should appear in the legend
        foreach (var (_, name, _) in ComparisonReporter.WeatherLegend)
            Assert.Contains(name, html);
    }

    [Fact]
    public void WeatherLegend_ContainsAllSixConditions()
    {
        Assert.Equal(6, ComparisonReporter.WeatherLegend.Count);
        var names = ComparisonReporter.WeatherLegend.Select(x => x.Name).ToList();
        Assert.Contains("Stable",      names);
        Assert.Contains("Warming",     names);
        Assert.Contains("Cooling",     names);
        Assert.Contains("Heat Spike",  names);
        Assert.Contains("Heat Wave",   names);
        Assert.Contains("Cold Front",  names);
    }

    [Fact]
    public void GenerateHtml_ShowsBothEntropyScores()
    {
        var reporter = new ComparisonReporter();
        var html = reporter.GenerateHtml(MakeReport(1.2345), MakeReport(0.9876));

        Assert.Contains("1.2345", html);
        Assert.Contains("0.9876", html);
    }

    [Fact]
    public void GenerateHtml_EscapesXssInFilePaths()
    {
        var reporter = new ComparisonReporter();
        var baseFiles = new List<DataJsonFileEntry>
        {
            MakeFile("src/<script>alert(1)</script>.cs"),
        };
        var curFiles = new List<DataJsonFileEntry>
        {
            MakeFile("src/<script>alert(1)</script>.cs", badness: 0.9),
        };
        var baseline = MakeReport(1.0, latestFiles: baseFiles);
        var current  = MakeReport(1.2, latestFiles: curFiles);

        var html = reporter.GenerateHtml(baseline, current);

        Assert.DoesNotContain("<script>alert(1)</script>.cs", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public void GenerateHtml_ShowsWorsenedAndImprovedSections()
    {
        var reporter = new ComparisonReporter();
        var baseFiles = new List<DataJsonFileEntry>
        {
            MakeFile("src/A.cs", badness: 0.3),
            MakeFile("src/B.cs", badness: 0.8),
        };
        var curFiles = new List<DataJsonFileEntry>
        {
            MakeFile("src/A.cs", badness: 0.9),  // worsened
            MakeFile("src/B.cs", badness: 0.2),  // improved
        };

        var html = reporter.GenerateHtml(
            MakeReport(1.0, latestFiles: baseFiles),
            MakeReport(1.2, latestFiles: curFiles));

        Assert.Contains("Files with Higher Badness", html);
        Assert.Contains("Files with Lower Badness",  html);
    }

    // ── Round-trip with GenerateDataJson ──────────────────────────────────────

    [Fact]
    public void Parse_RoundTrip_WithGenerateDataJson()
    {
        // Generate a data.json using the existing HtmlReporter.GenerateDataJson
        var commit1 = new CodeEvo.Core.Models.CommitInfo("abc123", DateTimeOffset.UtcNow.AddDays(-5), []);
        var commit2 = new CodeEvo.Core.Models.CommitInfo("def456", DateTimeOffset.UtcNow, []);
        var rm1 = new CodeEvo.Core.Models.RepoMetrics("abc123", 5, 200, 1.2);
        var rm2 = new CodeEvo.Core.Models.RepoMetrics("def456", 7, 350, 1.5);
        var history = new List<(CodeEvo.Core.Models.CommitInfo, CodeEvo.Core.Models.RepoMetrics)>
        {
            (commit1, rm1), (commit2, rm2)
        };
        var files = new List<CodeEvo.Core.Models.FileMetrics>
        {
            new("def456", "src/Foo.cs", "CSharp", 100, 5.0, 80.0, 0, 1, 2, 0, 0),
        };

        var jsonStr = HtmlReporter.GenerateDataJson(history, files);
        var report  = DataJsonReport.Parse(jsonStr);

        Assert.Equal(2, report.CommitCount);
        Assert.NotNull(report.Summary);
        Assert.Equal(1.5, report.Summary.Entropy, precision: 10);
        Assert.Equal(7,   report.Summary.Files);
        Assert.Equal(350, report.Summary.Sloc);
        Assert.Equal(2, report.History.Count);
        Assert.Single(report.LatestFiles);
        Assert.Equal("src/Foo.cs", report.LatestFiles[0].Path);
    }
}
