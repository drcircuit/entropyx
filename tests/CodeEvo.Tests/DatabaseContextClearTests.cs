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
}
