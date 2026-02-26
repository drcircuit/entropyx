using System.Collections.Concurrent;
using System.CommandLine;
using System.Runtime.InteropServices;
using CodeEvo.Adapters;
using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;
using Spectre.Console;

var rootCommand = new RootCommand("EntropyX - git history analyzer");

// ── scan command group ────────────────────────────────────────────────────────
var scanCommand = new Command("scan", "Scan code for metrics");

// scan lang [path] [--include <patterns>]
var scanLangPathArg = new Argument<string>("path", () => ".", "Directory to scan for language detection");
var scanLangIncludeOption = new Option<string?>("--include", () => null, "Comma-separated file patterns to include (e.g. *.cs,*.ts)");
var scanLangCommand = new Command("lang", "Detect language for each source file in a directory");
scanLangCommand.AddArgument(scanLangPathArg);
scanLangCommand.AddOption(scanLangIncludeOption);
scanLangCommand.SetHandler((string path, string? include) =>
{
    var includePatterns = ParsePatterns(include);
    var exIgnorePatterns = ScanFilter.LoadExIgnorePatterns(path);
    var reporter = new ConsoleReporter();
    var files = Directory.EnumerateFiles(path, "*", new EnumerationOptions
            { RecurseSubdirectories = true, IgnoreInaccessible = true })
        .Where(f => !ScanFilter.IsPathIgnored(f, path) && !ScanFilter.IsExIgnored(f, path, exIgnorePatterns) && ScanFilter.MatchesFilter(f, includePatterns))
        .Select(f => (Path: Path.GetRelativePath(path, f), Language: LanguageDetector.Detect(f)))
        .Where(x => x.Language.Length > 0)
        .OrderBy(x => x.Language).ThenBy(x => x.Path);
    reporter.ReportLanguageScan(files);
}, scanLangPathArg, scanLangIncludeOption);

// scan here [path] [--include <patterns>] [--save <output.json>] [--kind all|production|utility]
var scanHerePathArg = new Argument<string>("path", () => ".", "Directory to scan (no git required)");
var scanHereIncludeOption = new Option<string?>("--include", () => null, "Comma-separated file patterns to include (e.g. *.cs,*.ts)");
var scanHereSaveOption = new Option<string?>("--save", () => null, "Save a snapshot data.json to this file path (for later comparison)");
var scanHereKindOption = new Option<string>("--kind", () => "all", "Filter metrics by code kind: all, production, utility");
var scanHereCommand = new Command("here", "Scan current directory without git");
scanHereCommand.AddArgument(scanHerePathArg);
scanHereCommand.AddOption(scanHereIncludeOption);
scanHereCommand.AddOption(scanHereSaveOption);
scanHereCommand.AddOption(scanHereKindOption);
scanHereCommand.SetHandler((string path, string? include, string? savePath, string kind) =>
{
    var includePatterns = ParsePatterns(include);
    var pipeline = new ScanPipeline(new LizardAnalyzer());
    var reporter = new ConsoleReporter();
    IReadOnlyList<FileMetrics> files = [];
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Scanning {path}...", _ => files = pipeline.ScanDirectory(path, includePatterns));
    // When no explicit filter is given, show only recognized source files
    var display = includePatterns is null
        ? files.Where(f => f.Language.Length > 0).ToList()
        : (IReadOnlyList<FileMetrics>)files;
    display = FilterByKind(display, kind);
    reporter.ReportFileMetrics(display);
    var entropy = EntropyCalculator.ComputeEntropy(display);
    reporter.ReportScanChart(display);
    reporter.ReportSmellsChart(display);
    reporter.ReportScanSummary(display.Count, display.Sum(f => f.Sloc), entropy);

    if (savePath is not null)
    {
        var json = HtmlReporter.GenerateDataJson(display);
        File.WriteAllText(savePath, json);
        AnsiConsole.MarkupLine($"[green]✓[/] Snapshot saved to [cyan]{Markup.Escape(savePath)}[/]");
    }
}, scanHerePathArg, scanHereIncludeOption, scanHereSaveOption, scanHereKindOption);

// scan head [repoPath] [--db]
var scanHeadRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanHeadDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanHeadCommand = new Command("head", "Scan the current HEAD commit only");
scanHeadCommand.AddArgument(scanHeadRepoArg);
scanHeadCommand.AddOption(scanHeadDbOption);
scanHeadCommand.SetHandler((string repoPath, string dbPath) =>
{
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal, db) = BuildScanDeps(dbPath);
    var headCommit = traversal.GetAllCommits(repoPath).FirstOrDefault();
    if (headCommit is null) { AnsiConsole.MarkupLine("[red]No commits found.[/]"); return; }
    RunGitScan([headCommit], pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter, repoPath, db);
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
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal, db) = BuildScanDeps(dbPath);
    var commits = traversal.GetAllCommits(repoPath).Reverse().SkipWhile(c => c.Hash != since);
    RunGitScan(commits, pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter, repoPath, db);
}, scanFromCommitArg, scanFromRepoArg, scanFromDbOption);

// scan full [repoPath] [--db]
var scanFullRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanFullDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanFullCommand = new Command("full", "Scan entire git history");
scanFullCommand.AddArgument(scanFullRepoArg);
scanFullCommand.AddOption(scanFullDbOption);
scanFullCommand.SetHandler((string repoPath, string dbPath) =>
{
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal, db) = BuildScanDeps(dbPath);
    RunGitScan(traversal.GetAllCommits(repoPath).Reverse(), pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter, repoPath, db);
}, scanFullRepoArg, scanFullDbOption);

// scan chk [repoPath] [--db]
var scanChkRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanChkDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanChkCommand = new Command("chk", "Scan git checkpoint commits (tagged and merge commits)");
scanChkCommand.AddArgument(scanChkRepoArg);
scanChkCommand.AddOption(scanChkDbOption);
scanChkCommand.SetHandler((string repoPath, string dbPath) =>
{
    var (reporter, commitRepo, fileRepo, repoMetricsRepo, pipeline, traversal, db) = BuildScanDeps(dbPath);
    RunGitScan(traversal.GetCheckpointCommits(repoPath), pipeline, commitRepo, fileRepo, repoMetricsRepo, reporter, repoPath, db);
}, scanChkRepoArg, scanChkDbOption);

scanCommand.AddCommand(scanLangCommand);
scanCommand.AddCommand(scanHereCommand);
scanCommand.AddCommand(scanHeadCommand);
scanCommand.AddCommand(scanFromCommand);
scanCommand.AddCommand(scanFullCommand);
scanCommand.AddCommand(scanChkCommand);

// scan details [repoPath] [--db] [--html]
var scanDetailsRepoArg  = new Argument<string>("repoPath", () => ".", "Path to the git repository");
var scanDetailsDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var scanDetailsHtmlOption = new Option<string?>("--html", () => null, "Write a rich HTML drilldown report to this file");
var scanDetailsCommand = new Command("details", "Scan HEAD commit and show detailed per-language SLOC, per-file metrics, notable events, and a health assessment");
scanDetailsCommand.AddArgument(scanDetailsRepoArg);
scanDetailsCommand.AddOption(scanDetailsDbOption);
scanDetailsCommand.AddOption(scanDetailsHtmlOption);
scanDetailsCommand.SetHandler((string repoPath, string dbPath, string? htmlPath) =>
{
    var pipeline  = new ScanPipeline(new LizardAnalyzer());
    var reporter  = new ConsoleReporter();
    var traversal = new GitTraversal();

    var headCommit = traversal.GetAllCommits(repoPath).FirstOrDefault();
    if (headCommit is null) { AnsiConsole.MarkupLine("[red]No commits found.[/]"); return; }

    IReadOnlyList<FileMetrics> files = [];
    RepoMetrics repoMetrics = new(string.Empty, 0, 0, 0.0);
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Scanning {headCommit.Hash[..Math.Min(8, headCommit.Hash.Length)]}...", _ =>
        {
            (files, repoMetrics) = pipeline.ScanCommit(headCommit, repoPath);
        });

    // Load historical context from the database when available
    List<(CommitInfo, RepoMetrics)> history = [];
    RepoMetrics? prevMetrics = null;
    IReadOnlyList<HtmlReporter.CommitDelta> troubled = [];
    IReadOnlyList<HtmlReporter.CommitDelta> heroic   = [];

    if (File.Exists(dbPath))
    {
        var db = new DatabaseContext();
        db.Initialize(dbPath);
        var allMetrics = new RepoMetricsRepository(db).GetAll();
        var allCommits = new CommitRepository(db).GetAll();

        var metricsByHash = allMetrics.ToDictionary(m => m.CommitHash);
        history = allCommits
            .Where(c => metricsByHash.ContainsKey(c.Hash))
            .OrderBy(c => c.Timestamp)
            .Select(c => (c, metricsByHash[c.Hash]))
            .ToList();

        if (history.Count > 0)
        {
            var deltas = HtmlReporter.ComputeDeltas(history);
            (troubled, heroic) = HtmlReporter.ClassifyCommits(deltas);

            int headIdx = history.FindIndex(x => x.Item1.Hash == headCommit.Hash);
            if (headIdx > 0)
                prevMetrics = history[headIdx - 1].Item2;
        }
    }

    // ── CLI output ────────────────────────────────────────────────────────────
    reporter.ReportSlocByLanguage(files);
    reporter.ReportFileMetrics(files.Where(f => f.Language.Length > 0).ToList());
    reporter.ReportNotableEvents(troubled, heroic);
    reporter.ReportAssessment(repoMetrics, prevMetrics, history.Select(h => h.Item2).ToList());

    // ── HTML output ───────────────────────────────────────────────────────────
    if (htmlPath is not null)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Generating HTML drilldown report...", _ =>
            {
                var htmlReporter = new HtmlReporter();
                var html = htmlReporter.GenerateDrilldown(headCommit, repoMetrics, files, history, prevMetrics);
                File.WriteAllText(htmlPath, html);
            });
        AnsiConsole.MarkupLine($"[green]✓[/] HTML drilldown report written to [cyan]{Markup.Escape(htmlPath)}[/]");
    }
}, scanDetailsRepoArg, scanDetailsDbOption, scanDetailsHtmlOption);
scanCommand.AddCommand(scanDetailsCommand);

// ── check command group ───────────────────────────────────────────────────────
var checkCommand = new Command("check", "Check system requirements");
var checkToolsPathArg = new Argument<string>("path", () => ".", "Directory to scan for language detection");
var checkToolsCommand = new Command("tools", "Verify external tool availability and show install instructions");
checkToolsCommand.AddArgument(checkToolsPathArg);
checkToolsCommand.SetHandler((string path) => CheckTools(path), checkToolsPathArg);
checkCommand.AddCommand(checkToolsCommand);

// ── report subcommand ─────────────────────────────────────────────────────────
var reportRepoArg = new Argument<string>("repoPath", "Path to the git repository");
var reportDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var reportCommitOption = new Option<string?>("--commit", () => null, "Show metrics for a specific commit hash");
var reportHtmlOption = new Option<string?>("--html", () => null, "Write a rich HTML report to this file");
var reportKindOption = new Option<string>("--kind", () => "all", "Filter metrics by code kind: all, production, utility");

var reportCommand = new Command("report", "Show metrics report");
reportCommand.AddArgument(reportRepoArg);
reportCommand.AddOption(reportDbOption);
reportCommand.AddOption(reportCommitOption);
reportCommand.AddOption(reportHtmlOption);
reportCommand.AddOption(reportKindOption);

reportCommand.SetHandler(async (string repoPath, string dbPath, string? commitHash, string? htmlPath, string kind) =>
{
    var reporter = new ConsoleReporter();
    var db = new DatabaseContext();
    db.Initialize(dbPath);
    var repoMetricsRepo = new RepoMetricsRepository(db);
    var commitRepo = new CommitRepository(db);
    var fileMetricsRepo = new FileMetricsRepository(db);

    var allMetrics = repoMetricsRepo.GetAll();
    if (allMetrics.Count == 0)
    {
        Console.WriteLine("No metrics found. Run 'scan full', 'scan head', 'scan from', or 'scan chk' first.");
        return;
    }

    var filtered = allMetrics
        .Where(rm => commitHash is null || rm.CommitHash.StartsWith(commitHash, StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var rm in filtered)
        reporter.ReportRepoMetrics(rm);

    reporter.ReportEntropyTrend(filtered);

    if (htmlPath is not null)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Generating HTML report...", _ =>
            {
                // Build ordered commit history
                var allCommits = commitRepo.GetAll();
                var commitByHash = allCommits.ToDictionary(c => c.Hash);
                var metricsByHash = allMetrics.ToDictionary(m => m.CommitHash);

                var history = allCommits
                    .Where(c => metricsByHash.ContainsKey(c.Hash))
                    .OrderBy(c => c.Timestamp)
                    .Select(c => (c, metricsByHash[c.Hash]))
                    .ToList();

                // Fall back: use metrics in stored order when commit info is unavailable
                if (history.Count == 0)
                {
                    history = allMetrics
                        .Select(m =>
                        {
                            commitByHash.TryGetValue(m.CommitHash, out var ci);
                            ci ??= new CommitInfo(m.CommitHash, DateTimeOffset.MinValue, []);
                            return (ci, m);
                        })
                        .ToList();
                }

                // Get file metrics for the latest (most recent) commit, filtered by kind
                IReadOnlyList<FileMetrics> latestFiles = [];
                if (history.Count > 0)
                    latestFiles = FilterByKind(fileMetricsRepo.GetByCommit(history[^1].Item1.Hash), kind);

                // When --kind is specified, rebuild per-commit entropy from stored file metrics
                if (!string.Equals(kind, "all", StringComparison.OrdinalIgnoreCase))
                {
                    history = history.Select(h =>
                    {
                        var commitFiles = FilterByKind(fileMetricsRepo.GetByCommit(h.Item1.Hash), kind);
                        var kindEntropy = EntropyCalculator.ComputeEntropy(commitFiles);
                        var kindMetrics = new RepoMetrics(
                            h.Item2.CommitHash,
                            commitFiles.Count,
                            commitFiles.Sum(f => f.Sloc),
                            kindEntropy);
                        return (h.Item1, kindMetrics);
                    }).ToList();
                }

                var htmlReporter = new HtmlReporter();
                var html = htmlReporter.Generate(history, latestFiles);
                File.WriteAllText(htmlPath, html);

                // Write data.json alongside the HTML for later comparison
                var jsonPath = Path.ChangeExtension(htmlPath, ".json");
                var json = HtmlReporter.GenerateDataJson(history, latestFiles);
                File.WriteAllText(jsonPath, json);
            });

        AnsiConsole.MarkupLine($"[green]✓[/] HTML report written to [cyan]{Markup.Escape(htmlPath)}[/]");
        var jsonOutputPath = Path.ChangeExtension(htmlPath, ".json");
        AnsiConsole.MarkupLine($"[green]✓[/] Data JSON written to [cyan]{Markup.Escape(jsonOutputPath)}[/]");
    }

    await Task.CompletedTask;
}, reportRepoArg, reportDbOption, reportCommitOption, reportHtmlOption, reportKindOption);

// ── tools subcommand (kept for backward compatibility) ────────────────────────
var toolsPathArg = new Argument<string>("path", () => ".", "Directory to scan for language detection");
var toolsCommand = new Command("tools", "Check availability of external tools");
toolsCommand.AddArgument(toolsPathArg);
toolsCommand.SetHandler((string path) => CheckTools(path), toolsPathArg);

// ── heatmap command ───────────────────────────────────────────────────────────
var heatmapPathArg     = new Argument<string>("path", () => ".", "Directory to scan");
var heatmapOutputOpt   = new Option<string?>("--output", () => null, "Save heatmap PNG to this file path");
var heatmapIncludeOpt  = new Option<string?>("--include", () => null, "Comma-separated file patterns to include (e.g. *.cs,*.ts)");
var heatmapCommand     = new Command("heatmap", "Show a complexity heatmap for source files in a directory");
heatmapCommand.AddArgument(heatmapPathArg);
heatmapCommand.AddOption(heatmapOutputOpt);
heatmapCommand.AddOption(heatmapIncludeOpt);
heatmapCommand.SetHandler((string path, string? output, string? include) =>
{
    var includePatterns = ParsePatterns(include);
    var pipeline  = new ScanPipeline(new LizardAnalyzer());
    var reporter  = new ConsoleReporter();
    IReadOnlyList<FileMetrics> files = [];
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Scanning {path}...", _ => files = pipeline.ScanDirectory(path, includePatterns));

    var display = (includePatterns is null
        ? files.Where(f => f.Language.Length > 0).ToList()
        : (IReadOnlyList<FileMetrics>)files);

    if (display.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]No source files found.[/]");
        return;
    }

    double[] badness = EntropyCalculator.ComputeBadness(display);
    reporter.ReportHeatmap(display, badness);
    var heatmapEntropy = EntropyCalculator.ComputeEntropy(display);
    reporter.ReportScanSummary(display.Count, display.Sum(f => f.Sloc), heatmapEntropy);

    if (output is not null)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Generating heatmap image → {output}...", _ =>
                HeatmapImageGenerator.Generate(display, badness, output));
        AnsiConsole.MarkupLine($"[green]Heatmap image saved:[/] {Markup.Escape(output)}");
    }
}, heatmapPathArg, heatmapOutputOpt, heatmapIncludeOpt);

// ── refactor command ──────────────────────────────────────────────────────────
var refactorPathArg    = new Argument<string>("path", () => ".", "Directory to scan");
var refactorFocusOpt   = new Option<string>("--focus", () => "overall",
    "Metric(s) to rank by: overall, sloc, cc, mi, smells, coupling, or comma-separated combinations");
var refactorTopOpt     = new Option<int>("--top", () => 10, "Number of files to list (default 10)");
var refactorHtmlOpt    = new Option<string?>("--html", () => null, "Write a rich HTML refactor report to this file");
var refactorIncludeOpt = new Option<string?>("--include", () => null, "Comma-separated file patterns to include (e.g. *.cs,*.ts)");

var refactorCommand = new Command("refactor", "Show top files recommended for refactoring based on metrics");
refactorCommand.AddArgument(refactorPathArg);
refactorCommand.AddOption(refactorFocusOpt);
refactorCommand.AddOption(refactorTopOpt);
refactorCommand.AddOption(refactorHtmlOpt);
refactorCommand.AddOption(refactorIncludeOpt);
refactorCommand.SetHandler((string path, string focus, int top, string? htmlPath, string? include) =>
{
    var includePatterns = ParsePatterns(include);
    var pipeline  = new ScanPipeline(new LizardAnalyzer());
    var reporter  = new ConsoleReporter();
    IReadOnlyList<FileMetrics> files = [];
    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Scanning {path}...", _ => files = pipeline.ScanDirectory(path, includePatterns));

    var display = (includePatterns is null
        ? files.Where(f => f.Language.Length > 0).ToList()
        : (IReadOnlyList<FileMetrics>)files);

    if (display.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]No source files found.[/]");
        return;
    }

    double[] scores = EntropyCalculator.ComputeRefactorScores(display, focus);
    reporter.ReportRefactorList(display, scores, focus, top);

    if (htmlPath is not null)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Generating HTML refactor report → {htmlPath}...", _ =>
            {
                var htmlReporter = new HtmlReporter();
                var html = htmlReporter.GenerateRefactorReport(display, scores, focus, top);
                File.WriteAllText(htmlPath, html);
            });
        AnsiConsole.MarkupLine($"[green]✓[/] HTML refactor report written to [cyan]{Markup.Escape(htmlPath)}[/]");
    }
}, refactorPathArg, refactorFocusOpt, refactorTopOpt, refactorHtmlOpt, refactorIncludeOpt);

// ── compare command ───────────────────────────────────────────────────────────
var compareBaselineArg = new Argument<string>("baseline", "Path to the baseline data.json file");
var compareCurrentArg  = new Argument<string>("current",  "Path to the current data.json file");
var compareHtmlOption  = new Option<string?>("--html", () => null, "Write a rich HTML comparison report to this file");

var compareCommand = new Command("compare", "Compare two data.json snapshots for evolutionary assessment");
compareCommand.AddArgument(compareBaselineArg);
compareCommand.AddArgument(compareCurrentArg);
compareCommand.AddOption(compareHtmlOption);

compareCommand.SetHandler(async (string baselinePath, string currentPath, string? htmlPath) =>
{
    if (!File.Exists(baselinePath))
    {
        AnsiConsole.MarkupLine($"[red]Baseline file not found:[/] {Markup.Escape(baselinePath)}");
        return;
    }
    if (!File.Exists(currentPath))
    {
        AnsiConsole.MarkupLine($"[red]Current file not found:[/] {Markup.Escape(currentPath)}");
        return;
    }

    DataJsonReport baseline;
    DataJsonReport current;
    try
    {
        baseline = DataJsonReport.Parse(File.ReadAllText(baselinePath));
        current  = DataJsonReport.Parse(File.ReadAllText(currentPath));
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to parse data.json:[/] {Markup.Escape(ex.Message)}");
        return;
    }

    var comparisonReporter = new ComparisonReporter();
    comparisonReporter.ReportToConsole(baseline, current);

    if (htmlPath is not null)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Generating HTML comparison report...", _ =>
            {
                var html = comparisonReporter.GenerateHtml(baseline, current);
                File.WriteAllText(htmlPath, html);
            });
        AnsiConsole.MarkupLine($"[green]✓[/] HTML comparison report written to [cyan]{Markup.Escape(htmlPath)}[/]");
    }

    await Task.CompletedTask;
}, compareBaselineArg, compareCurrentArg, compareHtmlOption);
// ── db command group ──────────────────────────────────────────────────────────
var dbCommand = new Command("db", "Database management commands");

var dbListDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var dbListCommand = new Command("list", "List repos stored in the database");
dbListCommand.AddOption(dbListDbOption);
dbListCommand.SetHandler((string dbPath) =>
{
    if (!File.Exists(dbPath))
    {
        AnsiConsole.MarkupLine($"[red]Database not found:[/] {Markup.Escape(dbPath)}");
        return;
    }

    var db = new DatabaseContext();
    db.Initialize(dbPath);
    var repos = db.GetAllRepos();

    if (repos.Count == 0)
    {
        AnsiConsole.MarkupLine("[grey]No repos found. Run a scan command first.[/]");
        return;
    }

    var table = new Table()
        .AddColumn("Repo")
        .AddColumn("Remote URL");

    foreach (var (name, remoteUrl) in repos)
        table.AddRow(Markup.Escape(name), Markup.Escape(remoteUrl.Length > 0 ? remoteUrl : "(local)"));

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"[grey]{db.GetTotalCommitCount()} total commit(s) stored in {Markup.Escape(dbPath)}[/]");
}, dbListDbOption);

dbCommand.AddCommand(dbListCommand);

// ── clear command ─────────────────────────────────────────────────────────────
var clearRepoArg = new Argument<string>("repoPath", () => ".", "Path to the git repository to clear data for");
var clearDbOption = new Option<string>("--db", () => "entropyx.db", "Path to the SQLite database file");
var clearCommand = new Command("clear", "Clear all scanned data from the database for the given repository");
clearCommand.AddArgument(clearRepoArg);
clearCommand.AddOption(clearDbOption);
clearCommand.SetHandler((string repoPath, string dbPath) =>
{
    if (!GitTraversal.IsValidRepo(repoPath))
    {
        AnsiConsole.MarkupLine($"[red]No git repository found at:[/] {Markup.Escape(Path.GetFullPath(repoPath))}");
        return;
    }

    var (repoName, _) = GitTraversal.GetRepoInfo(repoPath);
    AnsiConsole.MarkupLine($"[yellow]Warning:[/] This will erase [bold]all[/] scanned data in [cyan]{Markup.Escape(dbPath)}[/] (repo: [cyan]{Markup.Escape(repoName)}[/]).");
    if (!AnsiConsole.Confirm("Are you sure you want to clear the database?", defaultValue: false))
    {
        AnsiConsole.MarkupLine("[grey]Aborted. No data was changed.[/]");
        return;
    }

    var db = new DatabaseContext();
    db.Initialize(dbPath);
    db.Clear();
    AnsiConsole.MarkupLine($"[green]✓[/] Database cleared for [cyan]{Markup.Escape(repoName)}[/].");
}, clearRepoArg, clearDbOption);

rootCommand.AddCommand(scanCommand);
rootCommand.AddCommand(checkCommand);
rootCommand.AddCommand(reportCommand);
rootCommand.AddCommand(toolsCommand);
rootCommand.AddCommand(heatmapCommand);
rootCommand.AddCommand(refactorCommand);
rootCommand.AddCommand(compareCommand);
rootCommand.AddCommand(dbCommand);
rootCommand.AddCommand(clearCommand);

return await rootCommand.InvokeAsync(args);

// ── helpers ───────────────────────────────────────────────────────────────────
static string[]? ParsePatterns(string? input) =>
    input?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

/// <summary>Filters a list of file metrics by the given kind string (all/production/utility).</summary>
static IReadOnlyList<FileMetrics> FilterByKind(IReadOnlyList<FileMetrics> files, string kind) =>
    kind.ToLowerInvariant() switch
    {
        "production" => files.Where(f => f.Kind == CodeKind.Production).ToList(),
        "utility"    => files.Where(f => f.Kind == CodeKind.Utility).ToList(),
        _            => files  // "all" or unrecognised → no filtering
    };

static (ConsoleReporter, CommitRepository, FileMetricsRepository, RepoMetricsRepository, ScanPipeline, GitTraversal, DatabaseContext)
    BuildScanDeps(string dbPath)
{
    var db = new DatabaseContext();
    db.Initialize(dbPath);
    return (new ConsoleReporter(), new CommitRepository(db), new FileMetricsRepository(db),
            new RepoMetricsRepository(db), new ScanPipeline(new LizardAnalyzer()), new GitTraversal(), db);
}

static void RunGitScan(
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

static void CheckTools(string path)
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
