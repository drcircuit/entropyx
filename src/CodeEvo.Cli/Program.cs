using System.CommandLine;
using System.Runtime.InteropServices;
using CodeEvo.Adapters;
using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;

var rootCommand = new RootCommand("EntropyX - git history analyzer");

// ── scan subcommand ──────────────────────────────────────────────────────────
var scanRepoArg = new Argument<string>("repoPath", "Path to the git repository");
var scanDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanSinceOption = new Option<string?>("--since", () => null, "Start scanning from this commit hash");

var scanCommand = new Command("scan", "Scan git history and store metrics");
scanCommand.AddArgument(scanRepoArg);
scanCommand.AddOption(scanDbOption);
scanCommand.AddOption(scanSinceOption);

scanCommand.SetHandler(async (string repoPath, string dbPath, string? since) =>
{
    var reporter = new ConsoleReporter();
    var db = new DatabaseContext();
    db.Initialize(dbPath);
    var commitRepo = new CommitRepository(db);
    var fileRepo = new FileMetricsRepository(db);
    var repoMetricsRepo = new RepoMetricsRepository(db);
    var pipeline = new ScanPipeline();
    var traversal = new GitTraversal();

    bool foundSince = since is null;
    foreach (var commit in traversal.GetAllCommits(repoPath).Reverse())
    {
        if (!foundSince)
        {
            if (commit.Hash == since) foundSince = true;
            else continue;
        }

        if (commitRepo.Exists(commit.Hash))
        {
            Console.WriteLine($"Skipping already-scanned commit {commit.Hash[..8]}");
            continue;
        }

        Console.Write($"Scanning {commit.Hash[..8]}... ");
        var (files, repoMetrics) = pipeline.ScanCommit(commit, repoPath);

        commitRepo.Insert(commit);
        foreach (var fm in files)
            if (!fileRepo.Exists(fm.CommitHash, fm.Path))
                fileRepo.Insert(fm);
        repoMetricsRepo.Insert(repoMetrics);

        reporter.ReportCommit(commit, repoMetrics);
    }

    await Task.CompletedTask;
}, scanRepoArg, scanDbOption, scanSinceOption);

// ── report subcommand ────────────────────────────────────────────────────────
var reportRepoArg = new Argument<string>("repoPath", "Path to the git repository");
var reportDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var reportCommitOption = new Option<string?>("--commit", () => null, "Show metrics for a specific commit hash");

var reportCommand = new Command("report", "Show metrics report");
reportCommand.AddArgument(reportRepoArg);
reportCommand.AddOption(reportDbOption);
reportCommand.AddOption(reportCommitOption);

reportCommand.SetHandler(async (string repoPath, string dbPath, string? commitHash) =>
{
    var reporter = new ConsoleReporter();
    var db = new DatabaseContext();
    db.Initialize(dbPath);
    var repoMetricsRepo = new RepoMetricsRepository(db);

    var allMetrics = repoMetricsRepo.GetAll();
    if (allMetrics.Count == 0)
    {
        Console.WriteLine("No metrics found. Run 'scan' first.");
        return;
    }

    foreach (var rm in allMetrics)
    {
        if (commitHash is not null && !rm.CommitHash.StartsWith(commitHash, StringComparison.OrdinalIgnoreCase))
            continue;
        Console.WriteLine($"Commit: {rm.CommitHash[..Math.Min(8, rm.CommitHash.Length)]}  Files: {rm.TotalFiles}  SLOC: {rm.TotalSloc}  Entropy: {rm.EntropyScore:F4}");
    }

    await Task.CompletedTask;
}, reportRepoArg, reportDbOption, reportCommitOption);

// ── tools subcommand ─────────────────────────────────────────────────────────
var toolsCommand = new Command("tools", "Check availability of external tools");
toolsCommand.SetHandler(() =>
{
    var procurement = new ToolProcurement();
    var reporter = new ConsoleReporter();
    string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux";

    string[] tools = ["git", "cloc"];
    foreach (var tool in tools)
    {
        if (procurement.CheckTool(tool))
            Console.WriteLine($"✓ {tool} is available");
        else
        {
            var instructions = procurement.GetInstallInstructions(tool, platform);
            reporter.ReportToolMissing(tool, instructions);
        }
    }
});

rootCommand.AddCommand(scanCommand);
rootCommand.AddCommand(reportCommand);
rootCommand.AddCommand(toolsCommand);

return await rootCommand.InvokeAsync(args);
