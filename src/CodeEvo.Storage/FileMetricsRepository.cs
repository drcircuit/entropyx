using CodeEvo.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeEvo.Storage;

public class FileMetricsRepository(DatabaseContext context)
{
    public void Insert(FileMetrics metrics)
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FileMetrics
                (CommitHash, Path, Language, Sloc, CyclomaticComplexity, MaintainabilityIndex,
                 SmellsHigh, SmellsMedium, SmellsLow, CouplingProxy, MaintainabilityProxy)
            VALUES
                (@commitHash, @path, @lang, @sloc, @cc, @mi, @sh, @sm, @sl, @cp, @mp)
            """;
        cmd.Parameters.AddWithValue("@commitHash", metrics.CommitHash);
        cmd.Parameters.AddWithValue("@path", metrics.Path);
        cmd.Parameters.AddWithValue("@lang", metrics.Language);
        cmd.Parameters.AddWithValue("@sloc", metrics.Sloc);
        cmd.Parameters.AddWithValue("@cc", metrics.CyclomaticComplexity);
        cmd.Parameters.AddWithValue("@mi", metrics.MaintainabilityIndex);
        cmd.Parameters.AddWithValue("@sh", metrics.SmellsHigh);
        cmd.Parameters.AddWithValue("@sm", metrics.SmellsMedium);
        cmd.Parameters.AddWithValue("@sl", metrics.SmellsLow);
        cmd.Parameters.AddWithValue("@cp", metrics.CouplingProxy);
        cmd.Parameters.AddWithValue("@mp", metrics.MaintainabilityProxy);
        cmd.ExecuteNonQuery();
    }

    public bool Exists(string commitHash, string path)
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM FileMetrics WHERE CommitHash = @commitHash AND Path = @path";
        cmd.Parameters.AddWithValue("@commitHash", commitHash);
        cmd.Parameters.AddWithValue("@path", path);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result) > 0;
    }

    public IReadOnlyList<FileMetrics> GetByCommit(string commitHash)
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT CommitHash, Path, Language, Sloc, CyclomaticComplexity, MaintainabilityIndex,
                   SmellsHigh, SmellsMedium, SmellsLow, CouplingProxy, MaintainabilityProxy
            FROM FileMetrics WHERE CommitHash = @commitHash
            """;
        cmd.Parameters.AddWithValue("@commitHash", commitHash);
        var results = new List<FileMetrics>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new FileMetrics(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetDouble(4), reader.GetDouble(5),
                reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                reader.GetDouble(9), reader.GetDouble(10)));
        return results;
    }
}
