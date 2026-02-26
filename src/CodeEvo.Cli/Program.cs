using System.CommandLine;
using CodeEvo.Adapters;
using CodeEvo.Cli;
using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;
using Spectre.Console;
using static CodeEvo.Cli.CliHelpers;

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
scanDetailsCommand.SetHandler(ScanDetailsCommandHandler.Handle,
    scanDetailsRepoArg, scanDetailsDbOption, scanDetailsHtmlOption);
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
var reportExportFiguresOption = new Option<string?>("--export-figures", () => null, "Export SVG figures to this directory (entropy-over-time.svg, sloc-over-time.svg, sloc-per-file-over-time.svg, cc-over-time.svg, smell-over-time.svg)");

var reportCommand = new Command("report", "Show metrics report");
reportCommand.AddArgument(reportRepoArg);
reportCommand.AddOption(reportDbOption);
reportCommand.AddOption(reportCommitOption);
reportCommand.AddOption(reportHtmlOption);
reportCommand.AddOption(reportKindOption);
reportCommand.AddOption(reportExportFiguresOption);

reportCommand.SetHandler(ReportCommandHandler.Handle,
    reportRepoArg, reportDbOption, reportCommitOption, reportHtmlOption, reportKindOption, reportExportFiguresOption);

// ── tools subcommand (kept for backward compatibility) ────────────────────────
var toolsPathArg = new Argument<string>("path", () => ".", "Directory to scan for language detection");
var toolsCommand = new Command("tools", "Check availability of external tools");
toolsCommand.AddArgument(toolsPathArg);
toolsCommand.SetHandler((string path) => CheckTools(path), toolsPathArg);

// ── heatmap command ───────────────────────────────────────────────────────────
var heatmapPathArg     = new Argument<string>("path", () => ".", "Directory to scan");
var heatmapOutputOpt   = new Option<string?>("--html", () => null, "Save heatmap PNG to this file path");
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
