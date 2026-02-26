using CodeEvo.Core;
using LibGit2Sharp;
using Xunit;

namespace CodeEvo.Tests;

public class GitTraversalRepoInfoTests : IDisposable
{
    private readonly string _repoDir;
    private readonly Repository _repo;
    private readonly Signature _sig;

    public GitTraversalRepoInfoTests()
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

    // ── IsValidRepo ───────────────────────────────────────────────────────────

    [Fact]
    public void IsValidRepo_ValidGitRepo_ReturnsTrue()
    {
        Assert.True(GitTraversal.IsValidRepo(_repoDir));
    }

    [Fact]
    public void IsValidRepo_NonGitDirectory_ReturnsFalse()
    {
        var plain = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(plain);
        try
        {
            Assert.False(GitTraversal.IsValidRepo(plain));
        }
        finally
        {
            Directory.Delete(plain, recursive: true);
        }
    }

    [Fact]
    public void IsValidRepo_NonExistentPath_ReturnsFalse()
    {
        Assert.False(GitTraversal.IsValidRepo(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz")));
    }

    // ── GetRepoInfo ───────────────────────────────────────────────────────────

    [Fact]
    public void GetRepoInfo_NoRemote_UsesDirName()
    {
        var (name, remoteUrl) = GitTraversal.GetRepoInfo(_repoDir);

        Assert.Equal(Path.GetFileName(_repoDir), name);
        Assert.Equal(string.Empty, remoteUrl);
    }

    [Theory]
    [InlineData("https://github.com/owner/myrepo.git", "owner/myrepo")]
    [InlineData("https://github.com/owner/myrepo", "owner/myrepo")]
    [InlineData("git@github.com:owner/myrepo.git", "owner/myrepo")]
    public void GetRepoInfo_WithRemote_DerivesNameFromUrl(string url, string expectedName)
    {
        _repo.Network.Remotes.Add("origin", url);

        var (name, remoteUrl) = GitTraversal.GetRepoInfo(_repoDir);

        Assert.Equal(expectedName, name);
        Assert.Equal(url, remoteUrl);
    }
}
