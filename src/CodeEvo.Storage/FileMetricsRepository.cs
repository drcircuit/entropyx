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
                 SmellsHigh, SmellsMedium, SmellsLow, CouplingProxy, MaintainabilityProxy, Kind)
            VALUES
                (@commitHash, @path, @lang, @sloc, @cc, @mi, @sh, @sm, @sl, @cp, @mp, @kind)
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
        cmd.Parameters.AddWithValue("@kind", metrics.Kind.ToString());
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
                   SmellsHigh, SmellsMedium, SmellsLow, CouplingProxy, MaintainabilityProxy, Kind
            FROM FileMetrics WHERE CommitHash = @commitHash
            """;
        cmd.Parameters.AddWithValue("@commitHash", commitHash);
        var results = new List<FileMetrics>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var kindOrdinal = reader.GetOrdinal("Kind");
            var kindStr = reader.IsDBNull(kindOrdinal) ? "Production" : reader.GetString(kindOrdinal);
            var kind = Enum.TryParse<CodeEvo.Core.Models.CodeKind>(kindStr, out var k)
                ? k : CodeEvo.Core.Models.CodeKind.Production;
            results.Add(new FileMetrics(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetDouble(4), reader.GetDouble(5),
                reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                reader.GetDouble(9), reader.GetDouble(10), kind));
        }
        return results;
    }
}
