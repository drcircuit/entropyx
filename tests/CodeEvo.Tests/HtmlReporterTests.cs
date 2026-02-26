using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using Xunit;

namespace CodeEvo.Tests;

public class HtmlReporterTests
{
    private static CommitInfo MakeCommit(string hash, int daysAgo = 0) =>
        new(hash, DateTimeOffset.UtcNow.AddDays(-daysAgo), []);

    private static RepoMetrics MakeRepoMetrics(string hash, double entropy, int files = 5, int sloc = 100) =>
        new(hash, files, sloc, entropy);

    private static FileMetrics MakeFileMetrics(string path, int sloc = 50, double cc = 3.0, double mi = 80.0,
        int smellsHigh = 0, int smellsMed = 0, int smellsLow = 0) =>
        new("hash", path, "CSharp", sloc, cc, mi, smellsHigh, smellsMed, smellsLow, 0, 0);

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
}
