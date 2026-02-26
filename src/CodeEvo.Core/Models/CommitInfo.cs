namespace CodeEvo.Core.Models;

public record CommitInfo(string Hash, DateTimeOffset Timestamp, IReadOnlyList<string> Parents);
