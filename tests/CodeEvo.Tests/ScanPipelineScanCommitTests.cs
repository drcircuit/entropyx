using CodeEvo.Core;
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
}
