using CodeEvo.Core;
using CodeEvo.Core.Models;
using Spectre.Console;

namespace CodeEvo.Reporting;

public class ConsoleReporter
{
    // Shared gradient: green (simple/short) → maroon (complex/long)
    private static readonly Color[] SizeGradient =
        [Color.Green, Color.GreenYellow, Color.Yellow, Color.Orange1, Color.OrangeRed1,
         Color.Red, Color.Red1, Color.Red3, Color.DarkRed, Color.Maroon];

    private static readonly Color[] TrendColors =
        [Color.Magenta1, Color.MediumOrchid, Color.MediumOrchid1, Color.Purple,
         Color.BlueViolet, Color.DarkMagenta, Color.DeepPink1, Color.DeepPink3, Color.HotPink, Color.Plum1];

    // Absolute SLOC thresholds: <50, <100, <150, <200, <300, <400, <500, <750, <1000, ≥1000
    private static Color SlocColor(int sloc) => sloc switch
    {
        < 50   => SizeGradient[0],
        < 100  => SizeGradient[1],
        < 150  => SizeGradient[2],
        < 200  => SizeGradient[3],
        < 300  => SizeGradient[4],
        < 400  => SizeGradient[5],
        < 500  => SizeGradient[6],
        < 750  => SizeGradient[7],
        < 1000 => SizeGradient[8],
        _      => SizeGradient[9],
    };

    // Absolute CC thresholds (industry standard: 1-5 simple, 6-10 moderate, 11-20 complex, 21-50 very complex, >50 untestable)
    private static Color CcColor(double cc) => cc switch
    {
        < 5  => SizeGradient[0],
        < 10 => SizeGradient[1],
        < 15 => SizeGradient[2],
        < 20 => SizeGradient[3],
        < 25 => SizeGradient[4],
        < 30 => SizeGradient[5],
        < 40 => SizeGradient[6],
        < 50 => SizeGradient[7],
        < 75 => SizeGradient[8],
        _    => SizeGradient[9],
    };

    // Absolute weighted-smells thresholds
    private static Color SmellColor(int score) => score switch
    {
        < 2  => SizeGradient[0],
        < 5  => SizeGradient[1],
        < 8  => SizeGradient[2],
        < 12 => SizeGradient[3],
        < 16 => SizeGradient[4],
        < 20 => SizeGradient[5],
        < 25 => SizeGradient[6],
        < 30 => SizeGradient[7],
        < 40 => SizeGradient[8],
        _    => SizeGradient[9],
    };

    public void ReportCommit(CommitInfo commit, RepoMetrics repoMetrics)
    {
        AnsiConsole.MarkupLine($"[bold cyan]Commit:[/] [yellow]{commit.Hash[..Math.Min(8, commit.Hash.Length)]}[/]  " +
                               $"[grey]{commit.Timestamp:yyyy-MM-dd HH:mm:ss zzz}[/]");
        AnsiConsole.MarkupLine($"  Files: [green]{repoMetrics.TotalFiles}[/]  " +
                               $"SLOC: [green]{repoMetrics.TotalSloc}[/]  " +
                               $"Entropy: [magenta]{repoMetrics.EntropyScore:F4}[/]");
    }

    public void ReportFileMetrics(IReadOnlyList<FileMetrics> files)
    {
        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No file metrics to display.[/]");
            return;
        }

        var table = new Table();
        table.AddColumn("Path");
        table.AddColumn("Language");
        table.AddColumn(new TableColumn("SLOC").RightAligned());
        table.AddColumn(new TableColumn("CC").RightAligned());
        table.AddColumn(new TableColumn("MI").RightAligned());
        table.AddColumn(new TableColumn("SmellsH/M/L").RightAligned());

        foreach (var f in files)
        {
            table.AddRow(
                Markup.Escape(f.Path),
                f.Language.Length > 0 ? f.Language : "[grey]unknown[/]",
                f.Sloc.ToString(),
                f.CyclomaticComplexity.ToString("F1"),
                f.MaintainabilityIndex.ToString("F1"),
                $"{f.SmellsHigh}/{f.SmellsMedium}/{f.SmellsLow}");
        }

        AnsiConsole.Write(table);
    }

    public void ReportToolAvailable(string toolName)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(toolName)} is available");
    }

    public void ReportToolMissing(string toolName, string installInstructions)
    {
        AnsiConsole.MarkupLine($"[bold yellow]WARNING:[/] Tool [red]{Markup.Escape(toolName)}[/] is not available.");
        AnsiConsole.MarkupLine($"  Install with: [cyan]{Markup.Escape(installInstructions)}[/]");
    }

    public void ReportDetectedLanguages(IEnumerable<string> languages)
    {
        var langList = languages.ToList();
        if (langList.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No recognized source languages detected.[/]");
            return;
        }
        AnsiConsole.MarkupLine($"[bold]Detected languages:[/] [green]{string.Join(", ", langList.Select(Markup.Escape))}[/]");
    }

    public void ReportLanguageScan(IEnumerable<(string Path, string Language)> results)
    {
        var table = new Table();
        table.AddColumn("File");
        table.AddColumn("Language");
        foreach (var (path, language) in results)
            table.AddRow(Markup.Escape(path), language);
        AnsiConsole.Write(table);
    }

    public void ReportScanSummary(int fileCount, int totalSloc, double entropy)
    {
        AnsiConsole.MarkupLine($"\n[bold]Total files:[/] [green]{fileCount}[/]  [bold]Total SLOC:[/] [green]{totalSloc}[/]  [bold]Entropy:[/] [magenta]{entropy:F4}[/]");
    }

    public void ReportScanChart(IReadOnlyList<FileMetrics> files, int topN = 10)
    {
        bool hasCc = files.Any(f => f.CyclomaticComplexity > 0);
        var top = (hasCc
            ? files.Where(f => f.CyclomaticComplexity > 0).OrderByDescending(f => f.CyclomaticComplexity)
            : files.Where(f => f.Sloc > 0).OrderByDescending(f => f.Sloc))
            .Take(topN).ToList();
        string label = hasCc ? "[bold]Top files by Cyclomatic Complexity[/]" : "[bold]Top files by SLOC[/]";
        RenderBarChart(top, label, f =>
            (Path.GetFileName(f.Path),
             hasCc ? Math.Round(f.CyclomaticComplexity, 1) : (double)f.Sloc,
             hasCc ? CcColor(f.CyclomaticComplexity) : SlocColor(f.Sloc)));
    }

    public void ReportSmellsChart(IReadOnlyList<FileMetrics> files, int topN = 10)
    {
        var top = files.Where(f => f.SmellsHigh > 0 || f.SmellsMedium > 0 || f.SmellsLow > 0)
            .OrderByDescending(WeightedSmells).Take(topN).ToList();
        RenderBarChart(top, "[bold]Top files by Code Smells (weighted H×3 + M×2 + L×1)[/]",
            f => { var s = WeightedSmells(f); return (Path.GetFileName(f.Path), (double)s, SmellColor(s)); });
    }

    private static void RenderBarChart<T>(List<T> items, string label,
        Func<T, (string name, double value, Color color)> selector)
    {
        if (items.Count == 0) return;
        var chart = new BarChart().Width(80).Label(label).CenterLabel();
        foreach (var item in items)
        {
            var (name, value, color) = selector(item);
            chart.AddItem(name, value, color);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(chart);
    }

    private static int WeightedSmells(FileMetrics f) =>
        f.SmellsHigh * 3 + f.SmellsMedium * 2 + f.SmellsLow;

    /// <summary>Renders a SLOC-per-language summary table sorted by total SLOC descending.</summary>
    public void ReportSlocByLanguage(IReadOnlyList<FileMetrics> files)
    {
        var byLang = files
            .Where(f => f.Language.Length > 0)
            .GroupBy(f => f.Language)
            .Select(g => (Language: g.Key, FileCount: g.Count(), TotalSloc: g.Sum(f => f.Sloc)))
            .OrderByDescending(x => x.TotalSloc)
            .ToList();

        if (byLang.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No source language files detected.[/]");
            return;
        }

        int grandTotal = byLang.Sum(x => x.TotalSloc);
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Language");
        table.AddColumn(new TableColumn("Files").RightAligned());
        table.AddColumn(new TableColumn("SLOC").RightAligned());
        table.AddColumn(new TableColumn("%").RightAligned());

        foreach (var (lang, count, sloc) in byLang)
        {
            double pct = grandTotal > 0 ? sloc * 100.0 / grandTotal : 0;
            table.AddRow(
                Markup.Escape(lang),
                count.ToString(),
                sloc.ToString("N0"),
                $"{pct:F1}%");
        }

        AnsiConsole.MarkupLine("\n[bold]SLOC by Language[/]");
        AnsiConsole.Write(table);
    }

    /// <summary>Renders a summary of notable commits (troubled / heroic) from stored history.</summary>
    public void ReportNotableEvents(
        IReadOnlyList<HtmlReporter.CommitDelta> troubled,
        IReadOnlyList<HtmlReporter.CommitDelta> heroic)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Notable Events[/]");

        if (troubled.Count == 0 && heroic.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No notable events detected in stored history.[/]");
            return;
        }

        PrintEventGroup(troubled, "[bold red]😈 Troubled Commits (entropy spikes):[/]", "red");
        PrintEventGroup(heroic,   "[bold green]🦸 Heroic Commits (entropy drops):[/]",  "green");
    }

    private static void PrintEventGroup(IReadOnlyList<HtmlReporter.CommitDelta> deltas, string header, string deltaColor)
    {
        if (deltas.Count == 0) return;
        AnsiConsole.MarkupLine(header);
        foreach (var d in deltas.Take(5))
        {
            var hash = d.Commit.Hash[..Math.Min(8, d.Commit.Hash.Length)];
            AnsiConsole.MarkupLine(
                $"  [yellow]{Markup.Escape(hash)}[/]  " +
                $"[grey]{d.Commit.Timestamp:yyyy-MM-dd}[/]  " +
                $"Entropy: [magenta]{d.Metrics.EntropyScore:F4}[/]  " +
                $"Δ [{deltaColor}]{d.Delta:F4}[/]");
        }
    }

    /// <summary>
    /// Renders a health assessment for the scanned commit, including a grade, entropy score,
    /// textual description, and optional trend vs the previous commit.
    /// When <paramref name="allHistory"/> contains 3 or more snapshots the assessment is also
    /// expressed relative to the repository's own recorded history.
    /// </summary>
    public void ReportAssessment(RepoMetrics current, RepoMetrics? previous, IReadOnlyList<RepoMetrics>? allHistory = null)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Assessment[/]");

        var (grade, gradeColor, description) = current.EntropyScore switch
        {
            < 0.3 => ("Excellent", Color.Green,    "Entropy drift is very low – the codebase temperature is cool."),
            < 0.7 => ("Good",      Color.GreenYellow, "Entropy drift is low – only minor areas are contributing to structural drift."),
            < 1.2 => ("Fair",      Color.Yellow,   "Entropy drift is moderate – structural complexity is accumulating over time."),
            < 2.0 => ("Poor",      Color.OrangeRed1, "Entropy drift is high – structural complexity has spread significantly across the codebase."),
            _     => ("Critical",  Color.Red,      "Entropy drift is very high – complexity is broadly distributed. Use trend analysis to identify when this began.")
        };

        AnsiConsole.MarkupLine($"  Drift Level:    [{gradeColor}]{grade}[/]");
        AnsiConsole.MarkupLine($"  Entropy Score:  [magenta]{current.EntropyScore:F4}[/]");
        AnsiConsole.MarkupLine($"  Files / SLOC:   [green]{current.TotalFiles}[/] / [green]{current.TotalSloc:N0}[/]");
        AnsiConsole.MarkupLine($"  {description}");

        // Show relative context when enough history is available
        var scores = allHistory?.Select(m => m.EntropyScore).ToList();
        if (scores is { Count: >= 3 })
        {
            double min  = scores.Min();
            double max  = scores.Max();
            double mean = scores.Average();
            double e    = current.EntropyScore;
            int countAtOrBelow = scores.Count(s => s <= e);
            double pct  = (double)countAtOrBelow / scores.Count * 100.0;

            string relativePos = pct switch
            {
                <= 25  => "near its historical low",
                <= 50  => "below its historical average",
                <= 75  => "above its historical average",
                <= 90  => "near its historical high",
                _      => "at or near its all-time high"
            };

            AnsiConsole.MarkupLine($"\n  [bold]Relative to this repo's own history ({scores.Count} snapshots):[/]");
            AnsiConsole.MarkupLine($"  This score is [cyan]{Markup.Escape(relativePos)}[/] — at the {pct:F0}{OrdinalSuffix(pct)} percentile of recorded history.");
            AnsiConsole.MarkupLine($"  [grey]Historical range: {min:F4} – {max:F4}  ·  avg: {mean:F4}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"\n  [grey]ℹ️  EntropyX measures structural drift over time — not a pass/fail grade.[/]");
        }

        if (previous is not null)
        {
            double delta = current.EntropyScore - previous.EntropyScore;
            var (trend, trendColor) = delta switch
            {
                > 0.02  => ("⬆ Worsening", "red"),
                < -0.02 => ("⬇ Improving", "green"),
                _       => ("→ Stable",    "grey")
            };
            string deltaStr = delta > 1e-9 ? $"+{delta:F4}" : delta < -1e-9 ? $"{delta:F4}" : "0.0000";
            AnsiConsole.MarkupLine($"  Trend:          [{trendColor}]{trend}[/]  " +
                                   $"(Δ Entropy: [grey]{Markup.Escape(deltaStr)}[/])");
        }
    }

    public void ReportRepoMetrics(RepoMetrics repoMetrics, string? repositoryName = null)
    {
        var repoPrefix = string.IsNullOrWhiteSpace(repositoryName) ? "" : $"[bold cyan]Repo:[/] [blue]{Markup.Escape(repositoryName)}[/]  ";
        AnsiConsole.MarkupLine(repoPrefix +
                               $"[bold cyan]Commit:[/] [yellow]{repoMetrics.CommitHash[..Math.Min(8, repoMetrics.CommitHash.Length)]}[/]  " +
                               $"Files: [green]{repoMetrics.TotalFiles}[/]  " +
                               $"SLOC: [green]{repoMetrics.TotalSloc}[/]  " +
                               $"Entropy: [magenta]{repoMetrics.EntropyScore:F4}[/]");
    }

    public void ReportEntropyTrend(IReadOnlyList<RepoMetrics> metrics)
    {
        if (metrics.Count == 0)
            return;

        var chart = new BarChart()
            .Width(80)
            .Label("[bold]Entropy score per commit[/]")
            .CenterLabel();

        for (int i = 0; i < metrics.Count; i++)
        {
            var m = metrics[i];
            var label = m.CommitHash[..Math.Min(7, m.CommitHash.Length)];
            chart.AddItem(label, Math.Round(m.EntropyScore, 4), TrendColors[i % TrendColors.Length]);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(chart);
    }

    /// <summary>
    /// Renders a per-file temperature graph in the console.
    /// Each file gets a coloured heat bar that runs from green (cool) through
    /// yellow (warm) to red (hot) based on its normalised badness score.
    /// Files are sorted hottest-first.
    /// </summary>
    /// <param name="files">File metrics in any order.</param>
    /// <param name="badness">Parallel badness array from EntropyCalculator.ComputeBadness.</param>
    public void ReportHeatmap(IReadOnlyList<FileMetrics> files, double[] badness)
    {
        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No files to display.[/]");
            return;
        }

        double max = badness.Length > 0 ? badness.Max() : 1.0;
        if (max == 0) max = 1.0;

        var sorted = files.Zip(badness)
            .OrderByDescending(x => x.Second)
            .ToList();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Heat");
        table.AddColumn("File");
        table.AddColumn(new TableColumn("SLOC").RightAligned());
        table.AddColumn(new TableColumn("CC").RightAligned());
        table.AddColumn(new TableColumn("Coupling").RightAligned());
        table.AddColumn(new TableColumn("Badness").RightAligned());

        foreach (var (file, b) in sorted)
        {
            double normalized = b / max;
            string colorHex   = TrafficLightHex(normalized);
            string bar        = HeatBar(normalized);

            table.AddRow(
                new Markup($"[{colorHex}]{bar}[/]"),
                new Markup(Markup.Escape(file.Path)),
                new Markup(file.Sloc.ToString()),
                new Markup(file.CyclomaticComplexity.ToString("F1")),
                new Markup(file.CouplingProxy.ToString("F1")),
                new Markup($"[{colorHex}]{b:F3}[/]"));
        }

        AnsiConsole.Write(table);
    }

    // ── heatmap helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a hex colour string interpolating green → yellow → red
    /// for <paramref name="t"/> ∈ [0, 1] (traffic-light gradient).
    /// </summary>
    public static string TrafficLightHex(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte r, g;
        if (t <= 0.5)
        {
            // Green (#00C800) → Yellow (#C8C800)
            r = (byte)(t * 2.0 * 200);
            g = 200;
        }
        else
        {
            // Yellow (#C8C800) → Red (#C80000)
            r = 200;
            g = (byte)((1.0 - t) * 2.0 * 200);
        }
        return $"#{r:X2}{g:X2}00";
    }

    private static string HeatBar(double normalized)
    {
        int filled = (int)Math.Round(normalized * 10);
        return new string('█', filled) + new string('░', 10 - filled);
    }

    /// <summary>
    /// Renders a ranked list of the top files recommended for refactoring based on
    /// the supplied per-file scores.  The <paramref name="focus"/> string is shown
    /// in the heading so the user can see which metric(s) drove the ranking.
    /// </summary>
    public void ReportRefactorList(IReadOnlyList<FileMetrics> files, double[] scores, string focus, int topN = 10)
    {
        var top = files.Zip(scores)
            .OrderByDescending(x => x.Second)
            .Take(topN)
            .ToList();

        if (top.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No files to recommend for refactoring.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"\n[bold]Top {top.Count} Refactor Candidates[/] [grey](focus: [cyan]{Markup.Escape(focus)}[/])[/]");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("#").RightAligned());
        table.AddColumn("File");
        table.AddColumn(new TableColumn("SLOC").RightAligned());
        table.AddColumn(new TableColumn("CC").RightAligned());
        table.AddColumn(new TableColumn("MI").RightAligned());
        table.AddColumn(new TableColumn("Smells H/M/L").RightAligned());
        table.AddColumn(new TableColumn("Coupling").RightAligned());
        table.AddColumn(new TableColumn("Score").RightAligned());

        double maxScore = top.Count > 0 ? top.Max(x => x.Second) : 1.0;
        if (maxScore == 0) maxScore = 1.0;

        for (int i = 0; i < top.Count; i++)
        {
            var (file, score) = top[i];
            double normalized = score / maxScore;
            string colorHex = TrafficLightHex(normalized);
            table.AddRow(
                (i + 1).ToString(),
                Markup.Escape(file.Path),
                file.Sloc.ToString(),
                file.CyclomaticComplexity.ToString("F1"),
                file.MaintainabilityIndex.ToString("F1"),
                $"{file.SmellsHigh}/{file.SmellsMedium}/{file.SmellsLow}",
                file.CouplingProxy.ToString("F1"),
                $"[{colorHex}]{score:F3}[/]");
        }

        AnsiConsole.Write(table);
    }

    /// <summary>Returns the correct English ordinal suffix (st/nd/rd/th) for a percentile value.</summary>
    private static string OrdinalSuffix(double value)
    {
        int n = (int)Math.Round(value);
        if (n % 100 is 11 or 12 or 13)
            return "th";
        return (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }
}
