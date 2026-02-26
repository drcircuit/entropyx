using Microsoft.Data.Sqlite;

namespace CodeEvo.Storage;

public class DatabaseContext
{
    private string _connectionString = string.Empty;

    public void Initialize(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Commits (
                Hash TEXT PRIMARY KEY,
                Timestamp TEXT NOT NULL,
                Parents TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS FileMetrics (
                CommitHash TEXT NOT NULL,
                Path TEXT NOT NULL,
                Language TEXT NOT NULL,
                Sloc INTEGER NOT NULL,
                CyclomaticComplexity REAL NOT NULL,
                MaintainabilityIndex REAL NOT NULL,
                SmellsHigh INTEGER NOT NULL,
                SmellsMedium INTEGER NOT NULL,
                SmellsLow INTEGER NOT NULL,
                CouplingProxy REAL NOT NULL,
                MaintainabilityProxy REAL NOT NULL,
                PRIMARY KEY (CommitHash, Path)
            );
            CREATE TABLE IF NOT EXISTS RepoMetrics (
                CommitHash TEXT PRIMARY KEY,
                TotalFiles INTEGER NOT NULL,
                TotalSloc INTEGER NOT NULL,
                EntropyScore REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Repos (
                Name TEXT PRIMARY KEY,
                RemoteUrl TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void RegisterRepo(string name, string remoteUrl)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Repos (Name, RemoteUrl) VALUES (@name, @remoteUrl)";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@remoteUrl", remoteUrl);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<(string Name, string RemoteUrl)> GetAllRepos()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name, RemoteUrl FROM Repos ORDER BY Name";
        var results = new List<(string, string)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }

    public int GetTotalCommitCount()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Commits";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public SqliteConnection OpenConnection() => new(_connectionString);

    public void Clear()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM FileMetrics;
            DELETE FROM RepoMetrics;
            DELETE FROM Commits;
            """;
        cmd.ExecuteNonQuery();
    }
}
