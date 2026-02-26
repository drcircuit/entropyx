using System.CommandLine;
using System.Runtime.InteropServices;
using CodeEvo.Adapters;
using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;

var rootCommand = new RootCommand("EntropyX - git history analyzer");

// ── scan command group ────────────────────────────────────────────────────────
var scanCommand = new Command("scan", "Scan code for metrics");

// scan lang [path]
var scanLangPathArg = new Argument<string>("path", () => ".", "Directory to scan for language detection");
var scanLangCommand = new Command("lang", "Detect language for each source file in a directory");
scanLangCommand.AddArgument(scanLangPathArg);
scanLangCommand.SetHandler((string path) =>
{
    var reporter = new ConsoleReporter();
    var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
        .Select(f => (Path: Path.GetRelativePath(path, f), Language: LanguageDetector.Detect(f)))
        .Where(x => x.Language.Length > 0)
        .OrderBy(x => x.Language).ThenBy(x => x.Path);
    reporter.ReportLanguageScan(files);
}, scanLangPathArg);

// scan here [path]
var scanHerePathArg = new Argument<string>("path", () => ".", "Directory to scan (no git required)");
var scanHereCommand = new Command("here", "Scan current directory without git");
scanHereCommand.AddArgument(scanHerePathArg);
scanHereCommand.SetHandler((string path) =>
{
    var pipeline = new ScanPipeline();
    var reporter = new ConsoleReporter();
    var files = pipeline.ScanDirectory(path);
    reporter.ReportFileMetrics(files);
    reporter.ReportScanSummary(files.Count, files.Sum(f => f.Sloc));
}, scanHerePathArg);

// scan head [repoPath] [--db]
var scanHeadRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanHeadDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanHeadCommand = new Command("head", "Scan the current HEAD commit only");
scanHeadCommand.AddArgument(scanHeadRepoArg);
scanHeadCommand.AddOption(scanHeadDbOption);
scanHeadCommand.SetHandler((string repoPath, string dbPath) =>
{
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal) = BuildScanDeps(dbPath);
    var headCommit = traversal.GetAllCommits(repoPath).FirstOrDefault();
    if (headCommit is null) { Console.WriteLine("No commits found."); return; }
    ScanAndStore(headCommit, repoPath, pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter);
}, scanHeadRepoArg, scanHeadDbOption);

// scan from <commit> [repoPath] [--db]
var scanFromCommitArg = new Argument<string>("commit", "Start scanning from this commit hash (inclusive)");
var scanFromRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanFromDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanFromCommand = new Command("from", "Scan commits starting from a given commit hash");
scanFromCommand.AddArgument(scanFromCommitArg);
scanFromCommand.AddArgument(scanFromRepoArg);
scanFromCommand.AddOption(scanFromDbOption);
scanFromCommand.SetHandler((string since, string repoPath, string dbPath) =>
{
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal) = BuildScanDeps(dbPath);
    RunGitScan(traversal, pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter, repoPath, since);
}, scanFromCommitArg, scanFromRepoArg, scanFromDbOption);

// scan full [repoPath] [--db]
var scanFullRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanFullDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanFullCommand = new Command("full", "Scan entire git history");
scanFullCommand.AddArgument(scanFullRepoArg);
scanFullCommand.AddOption(scanFullDbOption);
scanFullCommand.SetHandler((string repoPath, string dbPath) =>
{
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal) = BuildScanDeps(dbPath);
    RunGitScan(traversal, pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter, repoPath, since: null);
}, scanFullRepoArg, scanFullDbOption);

// scan chk [repoPath] [--db]
var scanChkRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanChkDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanChkCommand = new Command("chk", "Scan git checkpoint commits (tagged and merge commits)");
scanChkCommand.AddArgument(scanChkRepoArg);
scanChkCommand.AddOption(scanChkDbOption);
scanChkCommand.SetHandler((string repoPath, string dbPath) =>
{
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal) = BuildScanDeps(dbPath);
    foreach (var commit in traversal.GetCheckpointCommits(repoPath))
    {
        if (commitRepo.Exists(commit.Hash))
        {
            Console.WriteLine($"Skipping already-scanned commit {commit.Hash[..8]}");
            continue;
        }
        ScanAndStore(commit, repoPath, pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter);
    }
}, scanChkRepoArg, scanChkDbOption);

scanCommand.AddCommand(scanLangCommand);
scanCommand.AddCommand(scanHereCommand);
scanCommand.AddCommand(scanHeadCommand);
scanCommand.AddCommand(scanFromCommand);
scanCommand.AddCommand(scanFullCommand);
scanCommand.AddCommand(scanChkCommand);

// ── check command group ───────────────────────────────────────────────────────
var checkCommand = new Command("check", "Check system requirements");
var checkToolsCommand = new Command("tools", "Verify external tool availability and show install instructions");
checkToolsCommand.SetHandler(CheckTools);
checkCommand.AddCommand(checkToolsCommand);

// ── report subcommand ─────────────────────────────────────────────────────────
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
        Console.WriteLine("No metrics found. Run 'scan full', 'scan head', 'scan from', or 'scan chk' first.");
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

// ── tools subcommand (kept for backward compatibility) ────────────────────────
var toolsCommand = new Command("tools", "Check availability of external tools");
toolsCommand.SetHandler(CheckTools);

rootCommand.AddCommand(scanCommand);
rootCommand.AddCommand(checkCommand);
rootCommand.AddCommand(reportCommand);
rootCommand.AddCommand(toolsCommand);

return await rootCommand.InvokeAsync(args);

// ── helpers ───────────────────────────────────────────────────────────────────
static (ConsoleReporter, CommitRepository, FileMetricsRepository, RepoMetricsRepository, ScanPipeline, GitTraversal)
    BuildScanDeps(string dbPath)
{
    var db = new DatabaseContext();
    db.Initialize(dbPath);
    return (new ConsoleReporter(), new CommitRepository(db), new FileMetricsRepository(db),
            new RepoMetricsRepository(db), new ScanPipeline(), new GitTraversal());
}

static void ScanAndStore(CommitInfo commit, string repoPath, ScanPipeline pipeline,
    CommitRepository commitRepo, FileMetricsRepository fileRepo, RepoMetricsRepository repoMetricsRepo,
    ConsoleReporter reporter)
{
    Console.Write($"Scanning {commit.Hash[..8]}... ");
    var (files, repoMetrics) = pipeline.ScanCommit(commit, repoPath);
    commitRepo.Insert(commit);
    foreach (var fm in files)
        if (!fileRepo.Exists(fm.CommitHash, fm.Path))
            fileRepo.Insert(fm);
    repoMetricsRepo.Insert(repoMetrics);
    reporter.ReportCommit(commit, repoMetrics);
}

static void RunGitScan(GitTraversal traversal, ScanPipeline pipeline, CommitRepository commitRepo,
    FileMetricsRepository fileRepo, RepoMetricsRepository repoMetricsRepo, ConsoleReporter reporter,
    string repoPath, string? since)
{
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

        ScanAndStore(commit, repoPath, pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter);
    }
}

static void CheckTools()
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
}
