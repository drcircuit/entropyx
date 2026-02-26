using CodeEvo.Core;
using CodeEvo.Core.Models;
using LibGit2Sharp;
using Xunit;

namespace CodeEvo.Tests;

/// <summary>
/// Tests verifying that ScanPipeline.ScanCommit respects .exignore patterns.
/// </summary>
public class ScanPipelineScanCommitTests : IDisposable
{
    private readonly string _repoDir;
    private readonly Repository _repo;
    private readonly Signature _sig;

    public ScanPipelineScanCommitTests()
    {
        _repoDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_repoDir);
        Repository.Init(_repoDir);
        _repo = new Repository(_repoDir);
        _sig = new Signature("test", "test@test.com", DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_repoDir, recursive: true); }
        catch (IOException) { /* Native handles may still be open; OS will reclaim on exit */ }
    }

    private Commit CommitFiles(string message, params (string path, string content)[] files)
    {
        foreach (var (path, content) in files)
        {
            var fullPath = Path.Combine(_repoDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            Commands.Stage(_repo, path);
        }
        return _repo.Commit(message, _sig, _sig);
    }

    [Fact]
    public void ScanCommit_WithExIgnore_ExcludesMatchingFiles()
    {
        // Arrange: add a source file and a binary file to the repo
        var commit = CommitFiles("init",
            ("src/Main.cs", "class Main {}"),
            ("assets/icon.png", "PNG_BINARY_DATA"));

        // Write .exignore to exclude .png files
        File.WriteAllText(Path.Combine(_repoDir, ".exignore"), ".png\n");

        var pipeline = new ScanPipeline();
        var (files, _) = pipeline.ScanCommit(
            new CodeEvo.Core.Models.CommitInfo(commit.Sha, commit.Author.When, []),
            _repoDir);

        Assert.DoesNotContain(files, f => f.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.Path.EndsWith("Main.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanCommit_WithExIgnore_ExcludesIgnoredDirectories()
    {
        // Arrange: add files in a third_party directory
        var commit = CommitFiles("init",
            ("src/Main.cs", "class Main {}"),
            ("third_party/lib.cpp", "int foo() { return 0; }"));

        // Write .exignore to exclude third_party directory
        File.WriteAllText(Path.Combine(_repoDir, ".exignore"), "third_party\n");

        var pipeline = new ScanPipeline();
        var (files, _) = pipeline.ScanCommit(
            new CodeEvo.Core.Models.CommitInfo(commit.Sha, commit.Author.When, []),
            _repoDir);

        Assert.DoesNotContain(files, f => f.Path.Contains("third_party"));
        Assert.Contains(files, f => f.Path.EndsWith("Main.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanCommit_WithNoExIgnore_IncludesAllFiles()
    {
        var commit = CommitFiles("init",
            ("src/Main.cs", "class Main {}"),
            ("assets/icon.png", "PNG_BINARY_DATA"));

        var pipeline = new ScanPipeline();
        var (files, _) = pipeline.ScanCommit(
            new CodeEvo.Core.Models.CommitInfo(commit.Sha, commit.Author.When, []),
            _repoDir);

        Assert.Equal(2, files.Count);
    }

    /// <summary>
    /// Regression test: git paths always use '/' as the separator, but
    /// <see cref="LizardAnalyzer.ParseCsvOutput"/> uses <see cref="Path.GetRelativePath"/>
    /// which returns OS-native separators ('\\' on Windows). ScanCommit must normalise
    /// before the dictionary lookup so CC is populated on all platforms.
    /// </summary>
    [Fact]
    public void ScanCommit_WithLizardResultsForSubdirectoryFile_PopulatesCc()
    {
        // Arrange: commit a file in a subdirectory so git yields a path containing '/'.
        var commit = CommitFiles("init", ("src/Main.cs", "class Main { void A(){} }"));

        // Build the fake Lizard result using OS-native path separators to simulate
        // what LizardAnalyzer.ParseCsvOutput produces via Path.GetRelativePath.
        var nativePath = "src" + Path.DirectorySeparatorChar + "Main.cs";
        var fakeLizard = new FakeLizardAnalyzer(new Dictionary<string, LizardFileResult>(
            StringComparer.OrdinalIgnoreCase)
        {
            [nativePath] = new LizardFileResult(
                AvgCyclomaticComplexity: 7.5, SmellsHigh: 0, SmellsMedium: 0, SmellsLow: 1)
        });

        var pipeline = new ScanPipeline(fakeLizard);
        var (files, _) = pipeline.ScanCommit(
            new CommitInfo(commit.Sha, commit.Author.When, []),
            _repoDir);

        var main = Assert.Single(files, f => f.Path.EndsWith("Main.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(7.5, main.CyclomaticComplexity);
        Assert.Equal(1, main.SmellsLow);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeLizardAnalyzer(IReadOnlyDictionary<string, LizardFileResult> results) : ILizardAnalyzer
    {
        public IReadOnlyDictionary<string, LizardFileResult> AnalyzeDirectory(string dirPath) => results;
    }
}
