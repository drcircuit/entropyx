using CodeEvo.Core.Models;
using LibGit2Sharp;

namespace CodeEvo.Core;

public class ScanPipeline
{
    private readonly GitTraversal _git = new();
    private readonly ILizardAnalyzer? _lizard;

    public ScanPipeline(ILizardAnalyzer? lizard = null)
    {
        _lizard = lizard;
    }

    public (IReadOnlyList<FileMetrics> Files, RepoMetrics Repo) ScanCommit(CommitInfo commit, string repoPath)
    {
        var exIgnorePatterns = ScanFilter.LoadExIgnorePatterns(repoPath);
        var changedFiles = _git.GetChangedFiles(repoPath, commit.Hash)
            .Where(f => !ScanFilter.IsExIgnored(f, repoPath, exIgnorePatterns))
            .ToList();
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

    public IReadOnlyList<FileMetrics> ScanDirectory(string dirPath, string[]? includePatterns = null)
    {
        var exIgnorePatterns = ScanFilter.LoadExIgnorePatterns(dirPath);
        var lizardResults = _lizard?.AnalyzeDirectory(dirPath)
            ?? new Dictionary<string, LizardFileResult>();

        return Directory
            .EnumerateFiles(dirPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            })
            .AsParallel()
            .Where(f => !ScanFilter.IsPathIgnored(f, dirPath)
                     && !ScanFilter.IsExIgnored(f, dirPath, exIgnorePatterns)
                     && ScanFilter.MatchesFilter(f, includePatterns))
            .Select(filePath =>
            {
                var language = LanguageDetector.Detect(filePath);
                string[] lines;
                try { lines = File.ReadAllLines(filePath); }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
                var sloc = SlocCounter.CountSloc(lines, language);
                var relativePath = Path.GetRelativePath(dirPath, filePath);
                lizardResults.TryGetValue(relativePath, out var lizard);
                var avgCc = lizard?.AvgCyclomaticComplexity ?? 0.0;
                var mi = ComputeMaintainabilityIndex(sloc, avgCc);
                return new FileMetrics(
                    CommitHash: string.Empty,
                    Path: relativePath,
                    Language: language,
                    Sloc: sloc,
                    CyclomaticComplexity: avgCc,
                    MaintainabilityIndex: mi,
                    SmellsHigh: lizard?.SmellsHigh ?? 0,
                    SmellsMedium: lizard?.SmellsMedium ?? 0,
                    SmellsLow: lizard?.SmellsLow ?? 0,
                    CouplingProxy: 0,
                    MaintainabilityProxy: 0);
            })
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
    }

    private static double ComputeMaintainabilityIndex(int sloc, double avgCyclomaticComplexity)
    {
        // Simplified Maintainability Index (0–100) without Halstead Volume:
        // MI = max(0, (171 − 0.23 × CC − 16.2 × ln(LOC)) × 100 / 171)
        var mi = (171.0 - 0.23 * avgCyclomaticComplexity - 16.2 * Math.Log(Math.Max(1, sloc))) * 100.0 / 171.0;
        return Math.Max(0.0, mi);
    }
}
