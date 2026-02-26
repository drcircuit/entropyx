using CodeEvo.Core;
using LibGit2Sharp;
using Xunit;

namespace CodeEvo.Tests;

/// <summary>
/// Tests for GitTraversal.GetAllFilesAtCommit.
/// Each test creates a temporary git repository, adds commits, and verifies that
/// GetAllFilesAtCommit returns the complete file tree snapshot at each commit,
/// not just the files that changed.
/// </summary>
public class GitTraversalTests : IDisposable
{
    private readonly string _repoDir;
    private readonly Repository _repo;
    private readonly Signature _sig;

    public GitTraversalTests()
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
        // LibGit2Sharp holds native handles that may briefly delay file deletion.
        // Disposal is best-effort; test isolation is preserved by unique random directories.
        try { Directory.Delete(_repoDir, recursive: true); }
        catch (IOException) { /* Native handles may still be open; OS will reclaim on exit */ }
    }

    private Commit Commit(string message, params (string path, string content)[] files)
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

    // ── GetAllFilesAtCommit ───────────────────────────────────────────────────

    [Fact]
    public void GetAllFilesAtCommit_UnknownHash_ReturnsEmpty()
    {
        var traversal = new GitTraversal();
        var files = traversal.GetAllFilesAtCommit(_repoDir, "deadbeef").ToList();
        Assert.Empty(files);
    }

    [Fact]
    public void GetAllFilesAtCommit_SingleCommit_ReturnsAllFiles()
    {
        var c = Commit("init",
            ("src/A.cs", "class A {}"),
            ("src/B.cs", "class B {}"));

        var traversal = new GitTraversal();
        var files = traversal.GetAllFilesAtCommit(_repoDir, c.Sha).ToList();

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.EndsWith("A.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.EndsWith("B.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAllFilesAtCommit_SecondCommitOnlyChangesOneFile_StillReturnsAllFiles()
    {
        // Commit 1: add A.cs and B.cs
        Commit("init",
            ("src/A.cs", "class A {}"),
            ("src/B.cs", "class B {}"));

        // Commit 2: only modify A.cs – B.cs is unchanged
        var c2 = Commit("update A",
            ("src/A.cs", "class A { int x; }"));

        var traversal = new GitTraversal();
        var files = traversal.GetAllFilesAtCommit(_repoDir, c2.Sha).ToList();

        // Both files must appear in the snapshot, not just the changed one
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.EndsWith("A.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.EndsWith("B.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAllFilesAtCommit_AddFileInSecondCommit_BothFilesVisible()
    {
        Commit("init", ("A.cs", "class A {}"));
        var c2 = Commit("add B", ("B.cs", "class B {}"));

        var traversal = new GitTraversal();
        var files = traversal.GetAllFilesAtCommit(_repoDir, c2.Sha).ToList();

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.EndsWith("A.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.EndsWith("B.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAllFilesAtCommit_FirstCommitSnapshot_DoesNotIncludeFilesFromLaterCommits()
    {
        var c1 = Commit("init", ("A.cs", "class A {}"));
        Commit("add B", ("B.cs", "class B {}"));

        var traversal = new GitTraversal();
        // Snapshot at c1 should only have A.cs
        var files = traversal.GetAllFilesAtCommit(_repoDir, c1.Sha).ToList();

        Assert.Single(files);
        Assert.Contains(files, f => f.EndsWith("A.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAllFilesAtCommit_IgnoresDefaultIgnoredDirectories()
    {
        var c = Commit("init",
            ("src/A.cs", "class A {}"),
            ("node_modules/pkg/index.js", "var x = 1;"),
            ("bin/output.dll", "binary"));

        var traversal = new GitTraversal();
        var files = traversal.GetAllFilesAtCommit(_repoDir, c.Sha).ToList();

        Assert.Contains(files, f => f.EndsWith("A.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.Contains("node_modules"));
        Assert.DoesNotContain(files, f => f.Contains("bin"));
    }

    [Fact]
    public void GetAllFilesAtCommit_NestedSubdirectories_AllIncluded()
    {
        var c = Commit("init",
            ("a/b/c/deep.cs", "class D {}"),
            ("root.ts", "const x = 1;"));

        var traversal = new GitTraversal();
        var files = traversal.GetAllFilesAtCommit(_repoDir, c.Sha).ToList();

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Contains("deep.cs"));
        Assert.Contains(files, f => f.Contains("root.ts"));
    }

    // ── ScanPipeline.ScanCommit – full-snapshot behaviour ─────────────────────

    [Fact]
    public void ScanCommit_SecondCommit_ReturnsAllFilesNotJustChanged()
    {
        // Commit 1: A.cs + B.cs
        Commit("init",
            ("src/A.cs", "class A { int x = 1; }"),
            ("src/B.cs", "class B { int y = 2; }"));

        // Commit 2: only A.cs changes
        var c2 = Commit("change A", ("src/A.cs", "class A { int x = 99; int z = 0; }"));

        var pipeline = new ScanPipeline();
        var commitInfo = new CodeEvo.Core.Models.CommitInfo(c2.Sha, c2.Author.When, []);
        var (files, repo) = pipeline.ScanCommit(commitInfo, _repoDir);

        // Must scan entire snapshot – both A.cs and B.cs
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Path.EndsWith("A.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.Path.EndsWith("B.cs", StringComparison.OrdinalIgnoreCase));

        // Repo metrics must reflect both files
        Assert.Equal(2, repo.TotalFiles);
        Assert.True(repo.TotalSloc > 0);
    }

    [Fact]
    public void ScanCommit_FirstCommit_ReturnsAllIntroducedFiles()
    {
        var c1 = Commit("init",
            ("X.cs", "class X { int a; int b; }"),
            ("Y.py", "x = 1\ny = 2\n"));

        var pipeline = new ScanPipeline();
        var commitInfo = new CodeEvo.Core.Models.CommitInfo(c1.Sha, c1.Author.When, []);
        var (files, _) = pipeline.ScanCommit(commitInfo, _repoDir);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Language == "CSharp");
        Assert.Contains(files, f => f.Language == "Python");
    }

    [Fact]
    public void ScanCommit_MetricsReflectFullSnapshotSloc()
    {
        // Commit 1: two files contribute SLOC
        Commit("init",
            ("A.cs", "int a = 1;\nint b = 2;\n"),
            ("B.cs", "int c = 3;\n"));

        // Commit 2: tiny change only in A.cs
        var c2 = Commit("tiny change", ("A.cs", "int a = 1;\nint b = 2;\nint d = 4;\n"));

        var pipeline = new ScanPipeline();
        var commitInfo = new CodeEvo.Core.Models.CommitInfo(c2.Sha, c2.Author.When, []);
        var (files, repo) = pipeline.ScanCommit(commitInfo, _repoDir);

        // TotalSloc should include SLOC from B.cs (unchanged) + updated A.cs
        var bSloc = files.FirstOrDefault(f => f.Path.EndsWith("B.cs", StringComparison.OrdinalIgnoreCase))?.Sloc ?? 0;
        Assert.True(bSloc > 0, "B.cs (unchanged file) must contribute SLOC to the snapshot");
        Assert.Equal(files.Sum(f => f.Sloc), repo.TotalSloc);
    }

    [Fact]
    public void ScanCommit_MaintainabilityIndexPopulated()
    {
        // Without Lizard CC is 0, but MI should still be computed from SLOC
        var c = Commit("init", ("A.cs", string.Join("\n", Enumerable.Range(0, 20).Select(i => $"int x{i} = {i};"))));

        var pipeline = new ScanPipeline();
        var commitInfo = new CodeEvo.Core.Models.CommitInfo(c.Sha, c.Author.When, []);
        var (files, _) = pipeline.ScanCommit(commitInfo, _repoDir);

        Assert.Single(files);
        // MI = max(0, (171 - 0.23*0 - 16.2*ln(SLOC)) * 100 / 171) for CC=0
        Assert.True(files[0].MaintainabilityIndex >= 0.0);
        Assert.True(files[0].MaintainabilityIndex <= 100.0);
    }
}
