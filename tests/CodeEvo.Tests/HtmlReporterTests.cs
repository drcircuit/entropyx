using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using System.Globalization;
using Xunit;

namespace CodeEvo.Tests;

public class HtmlReporterTests
{
    private static CommitInfo MakeCommit(string hash, int daysAgo = 0) =>
        new(hash, DateTimeOffset.UtcNow.AddDays(-daysAgo), []);

    private static RepoMetrics MakeRepoMetrics(string hash, double entropy, int files = 5, int sloc = 100) =>
        new(hash, files, sloc, entropy);

    private static FileMetrics MakeFileMetrics(string path, int sloc = 50, double cc = 3.0, double mi = 80.0,
        int smellsHigh = 0, int smellsMed = 0, int smellsLow = 0, double coupling = 0) =>
        new("hash", path, "CSharp", sloc, cc, mi, smellsHigh, smellsMed, smellsLow, coupling, 0);

    // ── Generate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_EmptyHistory_ReturnsValidHtml()
    {
        var reporter = new HtmlReporter();
        var html = reporter.Generate([], []);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("EntropyX", html);
    }

    [Fact]
    public void Generate_ContainsRequiredSections()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("abc1", 10), MakeRepoMetrics("abc1", 0.5)),
            (MakeCommit("abc2", 5),  MakeRepoMetrics("abc2", 0.8)),
            (MakeCommit("abc3", 0),  MakeRepoMetrics("abc3", 0.6)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/A.cs", sloc: 300, cc: 15.0, smellsHigh: 2),
            MakeFileMetrics("src/B.cs", sloc: 50, cc: 2.0),
        };

        var html = reporter.Generate(history, files);

        Assert.Contains("Entropy Over Time", html);
        Assert.Contains("SLOC Over Time", html);
        Assert.Contains("File Count Over Time", html);
        Assert.Contains("Large Files", html);
        Assert.Contains("High Complexity", html);
        Assert.Contains("Smelly Areas", html);
        Assert.Contains("Troubled Commits", html);
        Assert.Contains("Heroic Commits", html);
        Assert.Contains("Commit History", html);
    }

    [Fact]
    public void Generate_IncludesLatestEntropyInSummary()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("aaa1", 5), MakeRepoMetrics("aaa1", 1.2345)),
        };

        var html = reporter.Generate(history, []);

        Assert.Contains("1.2345", html);
    }

    [Fact]
    public void Generate_EscapesHtmlInFilePaths()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("aaa1"), MakeRepoMetrics("aaa1", 0.5)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/<script>alert('xss')</script>.cs"),
        };

        var html = reporter.Generate(history, files);

        Assert.DoesNotContain("<script>alert('xss')</script>", html.Split("<script").Last());
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Generate_ChartJsScriptTagPresent()
    {
        var reporter = new HtmlReporter();
        var html = reporter.Generate([], []);
        Assert.Contains("chart.js", html);
    }

    [Fact]
    public void Generate_WithRepositoryName_IncludesItInHeader()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("aaa1", 0), MakeRepoMetrics("aaa1", 0.7)),
        };

        var html = reporter.Generate(history, [], repositoryName: "entropyx");

        Assert.Contains("EntropyX Report — entropyx", html);
    }

    // ── ComputeDeltas ─────────────────────────────────────────────────────────

    [Fact]
    public void ComputeDeltas_EmptyList_ReturnsEmpty()
    {
        var result = HtmlReporter.ComputeDeltas([]);
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeDeltas_SingleEntry_DeltaIsZero()
    {
        var entry = (MakeCommit("abc"), MakeRepoMetrics("abc", 0.5));
        var result = HtmlReporter.ComputeDeltas([entry]);
        Assert.Single(result);
        Assert.Equal(0.0, result[0].Delta, precision: 10);
    }

    [Fact]
    public void ComputeDeltas_TwoEntries_DeltaIsCorrect()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 5), MakeRepoMetrics("a", 1.0)),
            (MakeCommit("b", 0), MakeRepoMetrics("b", 1.5)),
        };
        var result = HtmlReporter.ComputeDeltas(history);
        Assert.Equal(2, result.Count);
        Assert.Equal(0.0,  result[0].Delta, precision: 10);
        Assert.Equal(0.5,  result[1].Delta, precision: 10);
        Assert.Equal(0.5,  result[1].RelativeDelta, precision: 10);
    }

    [Fact]
    public void ComputeDeltas_DecreasingEntropy_NegativeDelta()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 5), MakeRepoMetrics("a", 2.0)),
            (MakeCommit("b", 0), MakeRepoMetrics("b", 1.0)),
        };
        var result = HtmlReporter.ComputeDeltas(history);
        Assert.Equal(-1.0, result[1].Delta, precision: 10);
    }

    // ── ClassifyCommits ───────────────────────────────────────────────────────

    [Fact]
    public void ClassifyCommits_EmptyList_ReturnsBothEmpty()
    {
        var (troubled, heroic) = HtmlReporter.ClassifyCommits([]);
        Assert.Empty(troubled);
        Assert.Empty(heroic);
    }

    [Fact]
    public void ClassifyCommits_AllSameEntropy_NeitherTroubledNorHeroic()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 3), MakeRepoMetrics("a", 1.0)),
            (MakeCommit("b", 2), MakeRepoMetrics("b", 1.0)),
            (MakeCommit("c", 1), MakeRepoMetrics("c", 1.0)),
            (MakeCommit("d", 0), MakeRepoMetrics("d", 1.0)),
        };
        var deltas = HtmlReporter.ComputeDeltas(history);
        var (troubled, heroic) = HtmlReporter.ClassifyCommits(deltas);
        Assert.Empty(troubled);
        Assert.Empty(heroic);
    }

    [Fact]
    public void ClassifyCommits_LargeEntropySpike_MarkedAsTroubled()
    {
        // Spike commit causes large positive delta — should be troubled
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 6), MakeRepoMetrics("a", 0.1)),
            (MakeCommit("b", 5), MakeRepoMetrics("b", 0.1)),
            (MakeCommit("c", 4), MakeRepoMetrics("c", 0.1)),
            (MakeCommit("d", 3), MakeRepoMetrics("d", 0.1)),
            (MakeCommit("e", 2), MakeRepoMetrics("e", 0.1)),
            (MakeCommit("f", 1), MakeRepoMetrics("f", 5.0)),  // spike
            (MakeCommit("g", 0), MakeRepoMetrics("g", 5.0)),
        };
        var deltas = HtmlReporter.ComputeDeltas(history);
        var (troubled, _) = HtmlReporter.ClassifyCommits(deltas);
        Assert.NotEmpty(troubled);
        Assert.Equal("f", troubled[0].Commit.Hash);
    }

    [Fact]
    public void ClassifyCommits_LargeEntropyDrop_MarkedAsHeroic()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 6), MakeRepoMetrics("a", 5.0)),
            (MakeCommit("b", 5), MakeRepoMetrics("b", 5.0)),
            (MakeCommit("c", 4), MakeRepoMetrics("c", 5.0)),
            (MakeCommit("d", 3), MakeRepoMetrics("d", 5.0)),
            (MakeCommit("e", 2), MakeRepoMetrics("e", 5.0)),
            (MakeCommit("f", 1), MakeRepoMetrics("f", 0.1)),  // heroic drop
            (MakeCommit("g", 0), MakeRepoMetrics("g", 0.1)),
        };
        var deltas = HtmlReporter.ComputeDeltas(history);
        var (_, heroic) = HtmlReporter.ClassifyCommits(deltas);
        Assert.NotEmpty(heroic);
        Assert.Equal("f", heroic[0].Commit.Hash);
    }

    [Fact]
    public void ClassifyCommits_SingleDelta_NeitherTroubledNorHeroic()
    {
        // Only one delta available (skipped by Skip(1)), so nothing to classify
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 1), MakeRepoMetrics("a", 0.5)),
        };
        var deltas = HtmlReporter.ComputeDeltas(history);
        var (troubled, heroic) = HtmlReporter.ClassifyCommits(deltas);
        Assert.Empty(troubled);
        Assert.Empty(heroic);
    }

    // ── ComputeDeltas – SLOC and file deltas ──────────────────────────────────

    [Fact]
    public void ComputeDeltas_IncludesSlocAndFilesDeltas()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 5), MakeRepoMetrics("a", 1.0, files: 3, sloc: 100)),
            (MakeCommit("b", 0), MakeRepoMetrics("b", 1.5, files: 5, sloc: 150)),
        };
        var result = HtmlReporter.ComputeDeltas(history);
        Assert.Equal(0, result[0].SlocDelta);
        Assert.Equal(0, result[0].FilesDelta);
        Assert.Equal(50,  result[1].SlocDelta);
        Assert.Equal(2,   result[1].FilesDelta);
    }

    [Fact]
    public void ComputeDeltas_DecreasingSlocAndFiles_NegativeDeltas()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 2), MakeRepoMetrics("a", 1.0, files: 10, sloc: 500)),
            (MakeCommit("b", 0), MakeRepoMetrics("b", 0.8, files:  7, sloc: 300)),
        };
        var result = HtmlReporter.ComputeDeltas(history);
        Assert.Equal(-200, result[1].SlocDelta);
        Assert.Equal(-3,   result[1].FilesDelta);
    }

    // ── Generate – new sections ───────────────────────────────────────────────

    [Fact]
    public void Generate_ContainsGaugeSection()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 1), MakeRepoMetrics("a", 0.8)),
        };
        var html = reporter.Generate(history, [MakeFileMetrics("a.cs", sloc: 100, cc: 5.0)]);
        Assert.Contains("gaugeEntropy",  html);
        Assert.Contains("gaugeCc",       html);
        Assert.Contains("gaugeSmells",   html);
        Assert.Contains("Entropy Health",    html);
        Assert.Contains("Complexity Health", html);
        Assert.Contains("Smell Health",      html);
    }

    [Fact]
    public void Generate_ContainsHeatmapSection()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 1), MakeRepoMetrics("a", 0.5)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/A.cs", sloc: 200, cc: 10.0),
            MakeFileMetrics("src/B.cs", sloc: 50,  cc: 2.0),
        };
        var html = reporter.Generate(history, files);
        Assert.Contains("Complexity Heatmap", html);
        Assert.Contains("heatmap-row",         html);
    }

    [Fact]
    public void Generate_ContainsAccordions()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 1), MakeRepoMetrics("a", 0.5)),
        };
        var html = reporter.Generate(history, [MakeFileMetrics("a.cs")]);
        Assert.Contains("<details", html);
        Assert.Contains("<summary", html);
    }

    [Fact]
    public void Generate_HighComplexityTable_ExcludesZeroCcFiles()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a"), MakeRepoMetrics("a", 0.5)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("low.cs",  sloc: 500, cc: 0.0),  // no CC — should NOT appear in high-complexity table
            MakeFileMetrics("high.cs", sloc: 50,  cc: 25.0), // high CC — should appear
        };

        var html = reporter.Generate(history, files);

        // The high-complexity section must list high.cs and NOT low.cs
        // Find the High Complexity Areas section and check its content
        int sectionIdx = html.IndexOf("High Complexity Areas", StringComparison.Ordinal);
        Assert.True(sectionIdx >= 0, "High Complexity Areas section not found");
        int nextSectionIdx = html.IndexOf("Smelly Areas", StringComparison.Ordinal);
        Assert.True(nextSectionIdx > sectionIdx, "Smelly Areas section not found after High Complexity Areas");
        var section = html[sectionIdx..nextSectionIdx];
        Assert.Contains("high.cs", section);
        Assert.DoesNotContain("low.cs", section);
    }

    [Fact]
    public void Generate_SmellyAreasTable_ExcludesCleanFiles()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a"), MakeRepoMetrics("a", 0.5)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("clean.cs", sloc: 500, smellsHigh: 0, smellsMed: 0, smellsLow: 0), // no smells
            MakeFileMetrics("smelly.cs", sloc: 50, smellsHigh: 3),                              // smelly
        };

        var html = reporter.Generate(history, files);

        int sectionIdx = html.IndexOf("Smelly Areas", StringComparison.Ordinal);
        Assert.True(sectionIdx >= 0, "Smelly Areas section not found");
        // Scope to the next card heading inside the issues section, not all the way to Troubled Commits
        int nextSectionIdx = html.IndexOf("High Coupling Areas", StringComparison.Ordinal);
        Assert.True(nextSectionIdx > sectionIdx, "High Coupling Areas section not found after Smelly Areas");
        var section = html[sectionIdx..nextSectionIdx];
        Assert.Contains("smelly.cs", section);
        Assert.DoesNotContain("clean.cs", section);
    }

    [Fact]
    public void Generate_TroubledAndHeroicSections_HaveAccordions()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 6), MakeRepoMetrics("a", 0.1, files: 3, sloc: 100)),
            (MakeCommit("b", 5), MakeRepoMetrics("b", 0.1, files: 3, sloc: 100)),
            (MakeCommit("c", 4), MakeRepoMetrics("c", 0.1, files: 3, sloc: 100)),
            (MakeCommit("d", 3), MakeRepoMetrics("d", 0.1, files: 3, sloc: 100)),
            (MakeCommit("e", 2), MakeRepoMetrics("e", 0.1, files: 3, sloc: 100)),
            (MakeCommit("f", 1), MakeRepoMetrics("f", 5.0, files: 10, sloc: 500)),
            (MakeCommit("g", 0), MakeRepoMetrics("g", 5.0, files: 10, sloc: 500)),
        };
        var html = reporter.Generate(history, []);

        int troubledIdx = html.IndexOf("Troubled Commits", StringComparison.Ordinal);
        int heroicIdx   = html.IndexOf("Heroic Commits",   StringComparison.Ordinal);
        Assert.True(troubledIdx >= 0, "Troubled Commits section not found");
        Assert.True(heroicIdx   >= 0, "Heroic Commits section not found");

        // Both sections must contain a <details> accordion
        var troubledSection = html[troubledIdx..heroicIdx];
        Assert.Contains("<details", troubledSection);

        var heroicSection = html[heroicIdx..];
        Assert.Contains("<details", heroicSection);
    }

    [Fact]
    public void Generate_ContainsEntropyBadge()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a"), MakeRepoMetrics("a", 1.2345)),
        };
        var html = reporter.Generate(history, []);
        Assert.Contains("EntropyX", html);
        Assert.Contains("1.2345", html);
    }

    [Fact]
    public void Generate_DeltaTableIncludesSlocAndFilesColumns()
    {
        var reporter = new HtmlReporter();
        // Create a history with a large entropy spike so troubled commits are populated
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 6), MakeRepoMetrics("a", 0.1, files: 3, sloc: 100)),
            (MakeCommit("b", 5), MakeRepoMetrics("b", 0.1, files: 3, sloc: 100)),
            (MakeCommit("c", 4), MakeRepoMetrics("c", 0.1, files: 3, sloc: 100)),
            (MakeCommit("d", 3), MakeRepoMetrics("d", 0.1, files: 3, sloc: 100)),
            (MakeCommit("e", 2), MakeRepoMetrics("e", 0.1, files: 3, sloc: 100)),
            (MakeCommit("f", 1), MakeRepoMetrics("f", 5.0, files: 10, sloc: 500)),
            (MakeCommit("g", 0), MakeRepoMetrics("g", 5.0, files: 10, sloc: 500)),
        };
        var html = reporter.Generate(history, []);
        Assert.Contains("Δ SLOC", html);
        Assert.Contains("Δ Files", html);
    }

    // ── GenerateDataJson ──────────────────────────────────────────────────────

    [Fact]
    public void GenerateDataJson_EmptyInputs_ReturnsValidJson()
    {
        var json = HtmlReporter.GenerateDataJson([], []);
        Assert.False(string.IsNullOrWhiteSpace(json));
        // Should contain top-level keys
        Assert.Contains("\"commitCount\"", json);
        Assert.Contains("\"history\"", json);
        Assert.Contains("\"latestFiles\"", json);
    }

    [Fact]
    public void GenerateDataJson_WithHistory_ContainsCommitData()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("abc123", 5), MakeRepoMetrics("abc123", 1.5, files: 3, sloc: 200)),
            (MakeCommit("def456", 0), MakeRepoMetrics("def456", 2.0, files: 5, sloc: 300)),
        };

        var json = HtmlReporter.GenerateDataJson(history, []);

        Assert.Contains("\"abc123\"", json);
        Assert.Contains("\"def456\"", json);
        Assert.Contains("\"commitCount\": 2", json);
        Assert.Contains("\"entropy\"", json);
        Assert.Contains("\"sloc\"", json);
    }

    [Fact]
    public void GenerateDataJson_WithFiles_ContainsFileData()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a"), MakeRepoMetrics("a", 0.5)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/Foo.cs", sloc: 150, cc: 8.0, smellsHigh: 1),
        };

        var json = HtmlReporter.GenerateDataJson(history, files);

        Assert.Contains("\"src/Foo.cs\"", json);
        Assert.Contains("\"badness\"", json);
        Assert.Contains("\"cyclomaticComplexity\"", json);
    }

    // ── GenerateDataJson (ad hoc snapshot overload) ───────────────────────────

    [Fact]
    public void GenerateDataJson_AdHoc_EmptyFiles_ReturnsValidJson()
    {
        var json = HtmlReporter.GenerateDataJson((IReadOnlyList<FileMetrics>)[]);
        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.Contains("\"commitCount\": 1", json);
        Assert.Contains("\"history\"", json);
        Assert.Contains("\"latestFiles\"", json);
    }

    [Fact]
    public void GenerateDataJson_AdHoc_ContainsSingleSnapshotEntry()
    {
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/A.cs", sloc: 100, cc: 4.0),
            MakeFileMetrics("src/B.cs", sloc: 200, cc: 8.0),
        };

        var json = HtmlReporter.GenerateDataJson((IReadOnlyList<FileMetrics>)files);

        Assert.Contains("\"commitCount\": 1", json);
        Assert.Contains("\"entropy\"", json);
        Assert.Contains("\"files\"", json);
        Assert.Contains("\"sloc\"", json);
    }

    [Fact]
    public void GenerateDataJson_AdHoc_ContainsFileData()
    {
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/Foo.cs", sloc: 150, cc: 8.0, smellsHigh: 1),
        };

        var json = HtmlReporter.GenerateDataJson((IReadOnlyList<FileMetrics>)files);

        Assert.Contains("\"src/Foo.cs\"", json);
        Assert.Contains("\"badness\"", json);
        Assert.Contains("\"cyclomaticComplexity\"", json);
    }

    [Fact]
    public void GenerateDataJson_AdHoc_SlocsMatchTotalFiles()
    {
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("a.cs", sloc: 100),
            MakeFileMetrics("b.cs", sloc: 200),
        };

        var json = HtmlReporter.GenerateDataJson((IReadOnlyList<FileMetrics>)files);

        Assert.Contains("\"sloc\": 300", json);
        Assert.Contains("\"files\": 2", json);
    }

    // ── GenerateDrilldown ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateDrilldown_EmptyFiles_ReturnsValidHtml()
    {
        var reporter = new HtmlReporter();
        var commit = MakeCommit("abc123");
        var metrics = MakeRepoMetrics("abc123", 0.5);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], null);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("EntropyX Drilldown", html);
        Assert.Contains("abc123", html);
    }

    [Fact]
    public void GenerateDrilldown_ContainsAssessmentSection()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("def456");
        var metrics = MakeRepoMetrics("def456", 0.8);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], null);

        Assert.Contains("Assessment", html);
        Assert.Contains("0.8000", html);
    }

    [Fact]
    public void GenerateDrilldown_WithRepositoryName_IncludesItInHeader()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("repo1");
        var metrics = MakeRepoMetrics("repo1", 0.8);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], null, "entropyx");

        Assert.Contains("EntropyX Drilldown — entropyx", html);
    }

    [Fact]
    public void GenerateDrilldown_GradeExcellent_WhenEntropyBelowThreshold()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("aaa");
        var metrics = MakeRepoMetrics("aaa", 0.1);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], null);

        Assert.Contains("Excellent", html);
    }

    [Fact]
    public void GenerateDrilldown_GradeCritical_WhenEntropyVeryHigh()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("bbb");
        var metrics = MakeRepoMetrics("bbb", 3.5);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], null);

        Assert.Contains("Critical", html);
    }

    [Fact]
    public void GenerateDrilldown_ContainsLanguageChart_WhenFilesPresent()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("ccc");
        var metrics = MakeRepoMetrics("ccc", 0.5);
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/A.cs",  sloc: 200),
            MakeFileMetrics("src/B.ts",  sloc: 100),
        };
        // Override language field to simulate different languages
        files = [
            new FileMetrics("ccc", "src/A.cs",  "CSharp",     200, 3.0, 80.0, 0, 0, 0, 0, 0),
            new FileMetrics("ccc", "src/B.ts",  "TypeScript",  100, 2.0, 85.0, 0, 0, 0, 0, 0),
        ];

        var html = reporter.GenerateDrilldown(commit, metrics, files, [], null);

        Assert.Contains("SLOC by Language", html);
        Assert.Contains("langChart", html);
        Assert.Contains("CSharp", html);
        Assert.Contains("TypeScript", html);
    }

    [Fact]
    public void GenerateDrilldown_ContainsFileMetricsTable()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("ddd");
        var metrics = MakeRepoMetrics("ddd", 0.6);
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/Program.cs", sloc: 300, cc: 12.0, smellsHigh: 1),
        };

        var html = reporter.GenerateDrilldown(commit, metrics, files, [], null);

        Assert.Contains("File Metrics", html);
        Assert.Contains("src/Program.cs", html);
    }

    [Fact]
    public void GenerateDrilldown_ShowsDeltaVsPreviousCommit()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("eee");
        var metrics = MakeRepoMetrics("eee", 1.2, files: 10, sloc: 500);
        var prev    = MakeRepoMetrics("ddd", 0.8, files:  8, sloc: 400);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], prev);

        // Delta SLOC: +100, should appear in stat cards
        Assert.Contains("+100", html);
    }

    [Fact]
    public void GenerateDrilldown_ShowsTroubledAndHeroicSections_WithHistory()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("zzz");
        var metrics = MakeRepoMetrics("zzz", 0.5);

        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 6), MakeRepoMetrics("a", 0.1, files: 3, sloc: 100)),
            (MakeCommit("b", 5), MakeRepoMetrics("b", 0.1, files: 3, sloc: 100)),
            (MakeCommit("c", 4), MakeRepoMetrics("c", 0.1, files: 3, sloc: 100)),
            (MakeCommit("d", 3), MakeRepoMetrics("d", 0.1, files: 3, sloc: 100)),
            (MakeCommit("e", 2), MakeRepoMetrics("e", 0.1, files: 3, sloc: 100)),
            (MakeCommit("f", 1), MakeRepoMetrics("f", 5.0, files: 10, sloc: 500)),  // spike
            (MakeCommit("g", 0), MakeRepoMetrics("g", 5.0, files: 10, sloc: 500)),
        };

        var html = reporter.GenerateDrilldown(commit, metrics, [], history, null);

        Assert.Contains("Troubled Commits", html);
        Assert.Contains("Heroic Commits", html);
    }

    [Fact]
    public void GenerateDrilldown_EscapesHtmlInFilePaths()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("fff");
        var metrics = MakeRepoMetrics("fff", 0.5);
        var files = new List<FileMetrics>
        {
            new("fff", "src/<xss>.cs", "CSharp", 50, 2.0, 80.0, 0, 0, 0, 0, 0),
        };

        var html = reporter.GenerateDrilldown(commit, metrics, files, [], null);

        // The unescaped element name must not appear as an HTML tag anywhere in the document
        Assert.DoesNotContain("<xss>", html);
        Assert.Contains("&lt;xss&gt;", html);
    }

    // ── Coupling in reports ───────────────────────────────────────────────────

    [Fact]
    public void Generate_ContainsHighCouplingSection()
    {
        var reporter = new HtmlReporter();
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a"), MakeRepoMetrics("a", 0.5)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/A.cs", coupling: 15),
            MakeFileMetrics("src/B.cs", coupling: 3),
        };

        var html = reporter.Generate(history, files);

        Assert.Contains("High Coupling", html);
        Assert.Contains("src/A.cs", html);
    }

    [Fact]
    public void GenerateDataJson_WithFiles_ContainsCouplingProxy()
    {
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a"), MakeRepoMetrics("a", 0.5)),
        };
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/Foo.cs", sloc: 150, cc: 8.0, coupling: 12),
        };

        var json = HtmlReporter.GenerateDataJson(history, files);

        Assert.Contains("\"couplingProxy\"", json);
        Assert.Contains("12", json);
    }

    [Fact]
    public void GenerateDrilldown_FileTable_ContainsCouplingColumn()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("abc");
        var metrics = MakeRepoMetrics("abc", 0.5);
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/Heavy.cs", sloc: 200, cc: 10.0, coupling: 18),
        };

        var html = reporter.GenerateDrilldown(commit, metrics, files, [], null);

        Assert.Contains("Coupling", html);
        Assert.Contains("18", html);
    }

    // ── Relative assessment ───────────────────────────────────────────────────

    [Fact]
    public void GenerateDrilldown_AssessmentUsesDriftLevelLabel()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("aaa");
        var metrics = MakeRepoMetrics("aaa", 0.5);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], null);

        Assert.Contains("Drift Level", html);
    }

    [Fact]
    public void GenerateDrilldown_NoHistory_ShowsDriftFramingDisclaimer()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("aaa");
        var metrics = MakeRepoMetrics("aaa", 0.5);

        var html = reporter.GenerateDrilldown(commit, metrics, [], [], null);

        Assert.Contains("drift over time", html);
    }

    [Fact]
    public void GenerateDrilldown_WithEnoughHistory_ShowsRelativeContext()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("d");
        var metrics = MakeRepoMetrics("d", 1.2);

        // History with 4 snapshots; current score (1.2) is the 3rd out of 4 values
        // sorted: [0.88, 1.00, 1.20, 1.54] → 1.2 is at 75th percentile (above historical average)
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 3), MakeRepoMetrics("a", 0.88)),
            (MakeCommit("b", 2), MakeRepoMetrics("b", 1.00)),
            (MakeCommit("c", 1), MakeRepoMetrics("c", 1.54)),
            (MakeCommit("d", 0), MakeRepoMetrics("d", 1.20)),
        };

        var html = reporter.GenerateDrilldown(commit, metrics, [], history, null);

        // Relative context section must be present
        Assert.Contains("Relative to this repo", html);
        Assert.Contains("percentile", html);
        // Historical range must be shown
        Assert.Contains("0.8800", html); // min
        Assert.Contains("1.5400", html); // max
    }

    [Fact]
    public void GenerateDrilldown_WithInsufficientHistory_OmitsRelativeContext()
    {
        var reporter = new HtmlReporter();
        var commit  = MakeCommit("b");
        var metrics = MakeRepoMetrics("b", 1.0);

        // Only 2 history snapshots — below the 3-snapshot threshold
        var history = new List<(CommitInfo, RepoMetrics)>
        {
            (MakeCommit("a", 1), MakeRepoMetrics("a", 0.9)),
            (MakeCommit("b", 0), MakeRepoMetrics("b", 1.0)),
        };

        var html = reporter.GenerateDrilldown(commit, metrics, [], history, null);

        Assert.DoesNotContain("Relative to this repo", html);
    }

    // ── GenerateRefactorReport ────────────────────────────────────────────────

    [Fact]
    public void GenerateRefactorReport_EmptyFiles_ReturnsValidHtml()
    {
        var reporter = new HtmlReporter();
        var html = reporter.GenerateRefactorReport([], [], "overall");
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("EntropyX Refactor Report", html);
    }

    [Fact]
    public void GenerateRefactorReport_ContainsRefactorCandidatesSection()
    {
        var reporter = new HtmlReporter();
        var files = new List<FileMetrics>
        {
            MakeFileMetrics("src/A.cs", sloc: 500, cc: 20.0, smellsHigh: 2),
            MakeFileMetrics("src/B.cs", sloc: 50,  cc: 2.0),
            MakeFileMetrics("src/C.cs", sloc: 200, cc: 10.0),
        };
        double[] scores = [0.9, 0.2, 0.6];

        var html = reporter.GenerateRefactorReport(files, scores, "overall");

        Assert.Contains("Refactor Candidates", html);
        Assert.Contains("src/A.cs", html);
    }

    [Fact]
    public void GenerateRefactorReport_ShowsFocusInHeader()
    {
        var reporter = new HtmlReporter();
        var files = new List<FileMetrics> { MakeFileMetrics("a.cs", sloc: 100) };
        double[] scores = [0.5];

        var html = reporter.GenerateRefactorReport(files, scores, "sloc,cc");

        Assert.Contains("sloc,cc", html);
    }

    [Fact]
    public void GenerateRefactorReport_RespectsTopN()
    {
        var reporter = new HtmlReporter();
        var files = Enumerable.Range(1, 15)
            .Select(i => MakeFileMetrics($"src/File{i}.cs", sloc: i * 10))
            .ToList();
        var scores = Enumerable.Range(1, 15).Select(i => (double)i / 15.0).ToArray();

        var html = reporter.GenerateRefactorReport(files, scores, "sloc", topN: 5);

        // Only the top 5 files (highest scores = files 11–15) should appear
        Assert.Contains("File15.cs", html);
        Assert.DoesNotContain("File1.cs", html);
    }

    [Fact]
    public void GenerateRefactorReport_ContainsChart()
    {
        var reporter = new HtmlReporter();
        var files = new List<FileMetrics> { MakeFileMetrics("a.cs", sloc: 200, cc: 8.0) };
        double[] scores = [0.7];

        var html = reporter.GenerateRefactorReport(files, scores, "cc");

        Assert.Contains("refactorChart", html);
        Assert.Contains("chart.js", html);
    }

    [Fact]
    public void GenerateRefactorReport_EscapesHtmlInFilePaths()
    {
        var reporter = new HtmlReporter();
        var files = new List<FileMetrics>
        {
            new("hash", "src/<xss>.cs", "CSharp", 100, 5.0, 70.0, 0, 0, 0, 0, 0),
        };
        double[] scores = [0.5];

        var html = reporter.GenerateRefactorReport(files, scores, "overall");

        // The unescaped element name must not appear as a raw HTML tag in the table section
        int tableStart = html.IndexOf("Refactor Candidates", StringComparison.Ordinal);
        int chartStart = html.IndexOf("refactorChart", StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "Refactor Candidates section not found");
        Assert.True(chartStart > tableStart, "refactorChart not found after table");
        var tableSection = html[tableStart..chartStart];
        Assert.DoesNotContain("<xss>", tableSection);
        Assert.Contains("&lt;xss&gt;", tableSection);
    }

    [Fact]
    public void ExportSvgFigures_UsesInvariantCoordinateFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var outputDir = Path.Combine(Path.GetTempPath(), $"entropyx-svg-{Guid.NewGuid():N}");

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");

            var history = new List<(CommitInfo, RepoMetrics)>
            {
                (new CommitInfo("a", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), []), new RepoMetrics("a", 10, 100, 0.5)),
                (new CommitInfo("b", new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero), []), new RepoMetrics("b", 12, 150, 0.8)),
            };

            HtmlReporter.ExportSvgFigures(outputDir, history);

            var svg = File.ReadAllText(Path.Combine(outputDir, "sloc-over-time.svg"));
            Assert.Contains("<polyline points=\"72.0,", svg);
            Assert.DoesNotContain("72,0,", svg);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void ExportSvgFigures_UsesSamePointBudgetAsHtmlCharts()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"entropyx-svg-{Guid.NewGuid():N}");

        try
        {
            var history = Enumerable.Range(0, 300)
                .Select(i => (
                    new CommitInfo($"c{i}", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i), []),
                    new RepoMetrics($"c{i}", 10 + i, 100 + i, 0.5 + i * 0.001)))
                .ToList();

            HtmlReporter.ExportSvgFigures(outputDir, history);

            var svg = File.ReadAllText(Path.Combine(outputDir, "sloc-over-time.svg"));
            var marker = "<polyline points=\"";
            var start = svg.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "polyline points attribute not found");
            start += marker.Length;
            var end = svg.IndexOf("\"", start, StringComparison.Ordinal);
            Assert.True(end > start, "polyline points closing quote not found");
            var points = svg[start..end]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(300, points.Length);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void ExportSvgFigures_WithRepositoryName_IncludesNameInFigureTitle()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"entropyx-svg-{Guid.NewGuid():N}");

        try
        {
            var history = new List<(CommitInfo, RepoMetrics)>
            {
                (new CommitInfo("a", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), []), new RepoMetrics("a", 10, 100, 0.5)),
                (new CommitInfo("b", new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero), []), new RepoMetrics("b", 12, 150, 0.8)),
            };

            HtmlReporter.ExportSvgFigures(outputDir, history, repositoryName: "entropyx");

            var svg = File.ReadAllText(Path.Combine(outputDir, "sloc-over-time.svg"));
            Assert.Contains("entropyx — SLOC Over Time", svg);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void ExportSvgFigures_SlocSeriesMatchesHtmlChartSeries()
    {
        var reporter = new HtmlReporter();
        var outputDir = Path.Combine(Path.GetTempPath(), $"entropyx-svg-{Guid.NewGuid():N}");

        try
        {
            var history = Enumerable.Range(0, 520)
                .Select(i =>
                {
                    var sloc = i == 260 ? 50_000 : 100 + i;
                    return (
                        new CommitInfo($"c{i}", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i), []),
                        new RepoMetrics($"c{i}", 10 + i, sloc, 0.5 + i * 0.001));
                })
                .ToList();

            var html = reporter.Generate(history, []);
            HtmlReporter.ExportSvgFigures(outputDir, history);
            var svg = File.ReadAllText(Path.Combine(outputDir, "sloc-over-time.svg"));

            const string htmlStart = "mkChart('slocChart', 'SLOC', [";
            var htmlDataStart = html.IndexOf(htmlStart, StringComparison.Ordinal);
            Assert.True(htmlDataStart >= 0, "SLOC chart dataset not found in HTML");
            htmlDataStart += htmlStart.Length;
            const string htmlEnd = "], 'rgba(34,197,94,1)');";
            var htmlDataEnd = html.IndexOf(htmlEnd, htmlDataStart, StringComparison.Ordinal);
            Assert.True(htmlDataEnd > htmlDataStart, "SLOC chart dataset end not found in HTML");
            var htmlValues = html[htmlDataStart..htmlDataEnd]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
                .ToList();

            const string svgMarker = "<polyline points=\"";
            var svgPointsStart = svg.IndexOf(svgMarker, StringComparison.Ordinal);
            Assert.True(svgPointsStart >= 0, "SVG polyline points not found");
            svgPointsStart += svgMarker.Length;
            var svgPointsEnd = svg.IndexOf("\"", svgPointsStart, StringComparison.Ordinal);
            Assert.True(svgPointsEnd > svgPointsStart, "SVG polyline points end not found");
            var svgPoints = svg[svgPointsStart..svgPointsEnd]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            Assert.Equal(htmlValues.Count, svgPoints.Count);

            var htmlPeakIndex = htmlValues.IndexOf(htmlValues.Max());
            var svgPeakIndex = svgPoints
                .Select((point, index) => (Y: double.Parse(point.Split(',')[1], CultureInfo.InvariantCulture), Index: index))
                .OrderBy(p => p.Y)
                .First()
                .Index;

            Assert.Equal(htmlPeakIndex, svgPeakIndex);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
