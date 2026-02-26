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
            """;
        cmd.ExecuteNonQuery();
    }

    public SqliteConnection OpenConnection() => new(_connectionString);
}
