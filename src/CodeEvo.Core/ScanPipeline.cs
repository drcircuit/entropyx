using CodeEvo.Core.Models;
using LibGit2Sharp;

namespace CodeEvo.Core;

public class ScanPipeline
{
    private readonly GitTraversal _git = new();

    public (IReadOnlyList<FileMetrics> Files, RepoMetrics Repo) ScanCommit(CommitInfo commit, string repoPath)
    {
        var changedFiles = _git.GetChangedFiles(repoPath, commit.Hash).ToList();
        var fileMetrics = new List<FileMetrics>();

        using var repo = new Repository(repoPath);
        var gitCommit = repo.Lookup<Commit>(commit.Hash);

        foreach (var filePath in changedFiles)
        {
            var language = LanguageDetector.Detect(filePath);
            int sloc = 0;

            var entry = gitCommit?.Tree[filePath];
            if (entry?.TargetType == TreeEntryTargetType.Blob)
            {
                var blob = (Blob)entry.Target;
                using var reader = new StreamReader(blob.GetContentStream());
                var content = reader.ReadToEnd();
                var lines = content.Split('\n');
                sloc = SlocCounter.CountSloc(lines, language);
            }

            fileMetrics.Add(new FileMetrics(
                CommitHash: commit.Hash,
                Path: filePath,
                Language: language,
                Sloc: sloc,
                CyclomaticComplexity: 0,
                MaintainabilityIndex: 0,
                SmellsHigh: 0,
                SmellsMedium: 0,
                SmellsLow: 0,
                CouplingProxy: 0,
                MaintainabilityProxy: 0));
        }

        var entropy = EntropyCalculator.ComputeEntropy(fileMetrics);
        var totalSloc = fileMetrics.Sum(f => f.Sloc);
        var repoMetrics = new RepoMetrics(commit.Hash, fileMetrics.Count, totalSloc, entropy);

        return (fileMetrics, repoMetrics);
    }
}
