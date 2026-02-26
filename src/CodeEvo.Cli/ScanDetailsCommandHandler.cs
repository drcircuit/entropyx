using CodeEvo.Adapters;
using CodeEvo.Core;
using CodeEvo.Core.Models;
using CodeEvo.Reporting;
using CodeEvo.Storage;
using Spectre.Console;

namespace CodeEvo.Cli;

internal static class ScanDetailsCommandHandler
{
    internal static void Handle(string repoPath, string dbPath, string? htmlPath)
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
    }
}
