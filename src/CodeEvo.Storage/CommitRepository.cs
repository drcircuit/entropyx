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
}
