using CodeEvo.Core.Models;
using CodeEvo.Storage;
using Xunit;

namespace CodeEvo.Tests;

public class DatabaseContextClearTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseContext _db;

    public DatabaseContextClearTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
        _db = new DatabaseContext();
        _db.Initialize(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void Clear_RemovesAllCommits()
    {
        var commitRepo = new CommitRepository(_db);
        commitRepo.Insert(new CommitInfo("abc123", DateTimeOffset.UtcNow, []));
        Assert.True(commitRepo.Exists("abc123"));

        _db.Clear();

        Assert.False(commitRepo.Exists("abc123"));
        Assert.Empty(commitRepo.GetAll());
    }

    [Fact]
    public void Clear_RemovesAllRepoMetrics()
    {
        var repoMetricsRepo = new RepoMetricsRepository(_db);
        repoMetricsRepo.Insert(new RepoMetrics("abc123", 10, 100, 1.5));
        Assert.True(repoMetricsRepo.Exists("abc123"));

        _db.Clear();

        Assert.False(repoMetricsRepo.Exists("abc123"));
        Assert.Empty(repoMetricsRepo.GetAll());
    }

    [Fact]
    public void Clear_RemovesAllFileMetrics()
    {
        var fileMetricsRepo = new FileMetricsRepository(_db);
        fileMetricsRepo.Insert(new FileMetrics("abc123", "Foo.cs", "CSharp", 10, 1.0, 80.0, 0, 0, 0, 0.0, 0.0));
        Assert.True(fileMetricsRepo.Exists("abc123", "Foo.cs"));

        _db.Clear();

        Assert.False(fileMetricsRepo.Exists("abc123", "Foo.cs"));
        Assert.Empty(fileMetricsRepo.GetByCommit("abc123"));
    }

    [Fact]
    public void Clear_OnEmptyDatabase_DoesNotThrow()
    {
        var exception = Record.Exception(() => _db.Clear());
        Assert.Null(exception);
    }

    // ── RegisterRepo / GetAllRepos ────────────────────────────────────────────

    [Fact]
    public void RegisterRepo_CanBeRetrievedByGetAllRepos()
    {
        _db.RegisterRepo("owner/myrepo", "https://github.com/owner/myrepo.git");

        var repos = _db.GetAllRepos();

        Assert.Single(repos);
        Assert.Equal("owner/myrepo", repos[0].Name);
        Assert.Equal("https://github.com/owner/myrepo.git", repos[0].RemoteUrl);
    }

    [Fact]
    public void RegisterRepo_IsIdempotent_DoesNotDuplicate()
    {
        _db.RegisterRepo("owner/myrepo", "https://github.com/owner/myrepo.git");
        _db.RegisterRepo("owner/myrepo", "https://github.com/owner/myrepo.git");

        var repos = _db.GetAllRepos();
        Assert.Single(repos);
    }

    [Fact]
    public void GetTotalCommitCount_ReturnsCorrectCount()
    {
        _db.RegisterRepo("owner/myrepo", "https://github.com/owner/myrepo.git");
        var commitRepo = new CommitRepository(_db);
        commitRepo.Insert(new CommitInfo("aaa111", DateTimeOffset.UtcNow, []));
        commitRepo.Insert(new CommitInfo("bbb222", DateTimeOffset.UtcNow, []));

        Assert.Equal(2, _db.GetTotalCommitCount());
    }

    [Fact]
    public void GetAllRepos_EmptyDatabase_ReturnsEmptyList()
    {
        var repos = _db.GetAllRepos();
        Assert.Empty(repos);
    }

    [Fact]
    public void RepoMetricsRepository_GetAll_ReturnsCommitsInChronologicalOrder()
    {
        var commitRepo = new CommitRepository(_db);
        var repoMetricsRepo = new RepoMetricsRepository(_db);

        var oldest = new CommitInfo("aaa001", DateTimeOffset.UtcNow.AddDays(-10), []);
        var middle = new CommitInfo("bbb002", DateTimeOffset.UtcNow.AddDays(-5), []);
        var newest = new CommitInfo("ccc003", DateTimeOffset.UtcNow.AddDays(-1), []);

        // Insert commits in non-chronological order so alphabetical != chronological
        commitRepo.Insert(newest);
        commitRepo.Insert(oldest);
        commitRepo.Insert(middle);

        repoMetricsRepo.Insert(new RepoMetrics("ccc003", 3, 300, 0.3));
        repoMetricsRepo.Insert(new RepoMetrics("aaa001", 1, 100, 0.1));
        repoMetricsRepo.Insert(new RepoMetrics("bbb002", 2, 200, 0.2));

        var result = repoMetricsRepo.GetAll();

        Assert.Equal(3, result.Count);
        Assert.Equal("aaa001", result[0].CommitHash);
        Assert.Equal("bbb002", result[1].CommitHash);
        Assert.Equal("ccc003", result[2].CommitHash);
    }
}
