namespace CodeEvo.Core.Models;

public record RepoMetrics(string CommitHash, int TotalFiles, int TotalSloc, double EntropyScore);
