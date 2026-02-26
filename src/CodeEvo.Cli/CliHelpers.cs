using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using CodeEvo.Adapters;
using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;
using Spectre.Console;

namespace CodeEvo.Cli;

internal static class CliHelpers
{
    internal static string[]? ParsePatterns(string? input) =>
        input?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Filters a list of file metrics by the given kind string (all/production/utility).</summary>
    internal static IReadOnlyList<FileMetrics> FilterByKind(IReadOnlyList<FileMetrics> files, string kind) =>
        kind.ToLowerInvariant() switch
        {
            "production" => files.Where(f => f.Kind == CodeKind.Production).ToList(),
            "utility"    => files.Where(f => f.Kind == CodeKind.Utility).ToList(),
            _            => files  // "all" or unrecognised → no filtering
        };

    internal static (ConsoleReporter, CommitRepository, FileMetricsRepository, RepoMetricsRepository, ScanPipeline, GitTraversal, DatabaseContext)
        BuildScanDeps(string dbPath)
    {
        var db = new DatabaseContext();
        db.Initialize(dbPath);
        return (new ConsoleReporter(), new CommitRepository(db), new FileMetricsRepository(db),
                new RepoMetricsRepository(db), new ScanPipeline(new LizardAnalyzer()), new GitTraversal(), db);
    }

    internal static void RunGitScan(
        IEnumerable<CommitInfo> commits,
        ScanPipeline pipeline,
        CommitRepository commitRepo,
        FileMetricsRepository fileRepo,
        RepoMetricsRepository repoMetricsRepo,
        ConsoleReporter reporter,
        string repoPath,
        DatabaseContext db)
    {
        // Register repo metadata so 'db list' can show it
        if (GitTraversal.IsValidRepo(repoPath))
        {
            var (repoName, remoteUrl) = GitTraversal.GetRepoInfo(repoPath);
            db.RegisterRepo(repoName, remoteUrl);
        }
        // Phase 1: discover which commits still need scanning
        List<CommitInfo> toScan = [];
        int skipped = 0;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Discovering commits...", _ =>
            {
                foreach (var commit in commits)
                {
                    if (commitRepo.Exists(commit.Hash)) { skipped++; continue; }
                    toScan.Add(commit);
                }
            });

        if (skipped > 0)
            AnsiConsole.MarkupLine($"[grey]Skipped {skipped} already-scanned commit(s).[/]");

        if (toScan.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No new commits to scan.[/]");
            return;
        }

        // Phase 2: scan commits in parallel with a live progress bar
        var results = new ConcurrentBag<(CommitInfo Commit, IReadOnlyList<FileMetrics> Files, RepoMetrics RepoMetrics)>();
        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask($"Scanning {toScan.Count} commit(s)", maxValue: toScan.Count);
                Parallel.ForEach(toScan, commit =>
                {
                    var (files, repoMetrics) = pipeline.ScanCommit(commit, repoPath);
                    results.Add((commit, files, repoMetrics));
                    task.Increment(1);
                });
            });

        // Phase 3: store and report results in chronological order
        foreach (var (commit, files, repoMetrics) in results.OrderBy(r => r.Commit.Timestamp))
        {
            commitRepo.Insert(commit);
            foreach (var fm in files)
                if (!fileRepo.Exists(fm.CommitHash, fm.Path))
                    fileRepo.Insert(fm);
            repoMetricsRepo.Insert(repoMetrics);
            reporter.ReportCommit(commit, repoMetrics);
        }
    }

    internal static void CheckTools(string path)
    {
        var procurement = new ToolProcurement();
        var reporter = new ConsoleReporter();
        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux";

        List<string> detectedLanguages;
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Warning: path '{path}' does not exist.");
            detectedLanguages = [];
        }
        else
        {
            var exIgnorePatterns = ScanFilter.LoadExIgnorePatterns(path);
            detectedLanguages = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                })
                .Where(f => !ScanFilter.IsPathIgnored(f, path) && !ScanFilter.IsExIgnored(f, path, exIgnorePatterns))
                .Select(f => LanguageDetector.Detect(f))
                .Where(lang => lang.Length > 0)
                .Distinct()
                .OrderBy(lang => lang)
                .ToList();
        }

        reporter.ReportDetectedLanguages(detectedLanguages);

        var requiredTools = detectedLanguages
            .SelectMany(lang => procurement.GetRequiredToolsForLanguage(lang))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        if (requiredTools.Count == 0)
            requiredTools = ["cloc", "git"];

        foreach (var tool in requiredTools)
        {
            if (procurement.CheckTool(tool))
                reporter.ReportToolAvailable(tool);
            else
            {
                var instructions = procurement.GetInstallInstructions(tool, platform);
                reporter.ReportToolMissing(tool, instructions);
            }
        }
    }
}
