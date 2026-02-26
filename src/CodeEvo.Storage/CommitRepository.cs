using CodeEvo.Core.Models;
using Microsoft.Data.Sqlite;

namespace CodeEvo.Storage;

public class CommitRepository(DatabaseContext context)
{
    public void Insert(CommitInfo commit)
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Commits (Hash, Timestamp, Parents) VALUES (@hash, @ts, @parents)";
        cmd.Parameters.AddWithValue("@hash", commit.Hash);
        cmd.Parameters.AddWithValue("@ts", commit.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@parents", string.Join(",", commit.Parents));
        cmd.ExecuteNonQuery();
    }

    public bool Exists(string hash)
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Commits WHERE Hash = @hash";
        cmd.Parameters.AddWithValue("@hash", hash);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result) > 0;
    }

    public IReadOnlyList<CommitInfo> GetAll()
    {
        using var connection = context.OpenConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Hash, Timestamp, Parents FROM Commits ORDER BY Timestamp";
        var results = new List<CommitInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var hash = reader.GetString(0);
            var ts = DateTimeOffset.Parse(reader.GetString(1));
            var parents = reader.GetString(2)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            results.Add(new CommitInfo(hash, ts, parents));
        }
        return results;
    }
}
