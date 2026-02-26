using CodeEvo.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeEvo.Storage;

public class RepoMetricsRepository(DatabaseContext context)
{
    public void Insert(RepoMetrics metrics)
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RepoMetrics (CommitHash, TotalFiles, TotalSloc, EntropyScore)
            VALUES (@commitHash, @totalFiles, @totalSloc, @entropy)
            """;
        cmd.Parameters.AddWithValue("@commitHash", metrics.CommitHash);
        cmd.Parameters.AddWithValue("@totalFiles", metrics.TotalFiles);
        cmd.Parameters.AddWithValue("@totalSloc", metrics.TotalSloc);
        cmd.Parameters.AddWithValue("@entropy", metrics.EntropyScore);
        cmd.ExecuteNonQuery();
    }

    public bool Exists(string commitHash)
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM RepoMetrics WHERE CommitHash = @commitHash";
        cmd.Parameters.AddWithValue("@commitHash", commitHash);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result) > 0;
    }

    public IReadOnlyList<RepoMetrics> GetAll()
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CommitHash, TotalFiles, TotalSloc, EntropyScore FROM RepoMetrics ORDER BY CommitHash";
        var results = new List<RepoMetrics>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new RepoMetrics(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetDouble(3)));
        return results;
    }
}
