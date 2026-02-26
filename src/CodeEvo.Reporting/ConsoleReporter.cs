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

        var top = hasCc
            ? files.Where(f => f.CyclomaticComplexity > 0)
                   .OrderByDescending(f => f.CyclomaticComplexity)
                   .Take(topN)
                   .ToList()
            : files.Where(f => f.Sloc > 0)
                   .OrderByDescending(f => f.Sloc)
                   .Take(topN)
                   .ToList();

        if (top.Count == 0)
            return;

        string label = hasCc ? "[bold]Top files by Cyclomatic Complexity[/]" : "[bold]Top files by SLOC[/]";

        var chart = new BarChart()
            .Width(80)
            .Label(label)
            .CenterLabel();

        for (int i = 0; i < top.Count; i++)
        {
            var f = top[i];
            var barLabel = Path.GetFileName(f.Path);
            double value = hasCc ? Math.Round(f.CyclomaticComplexity, 1) : f.Sloc;
            var color = hasCc ? CcColor(f.CyclomaticComplexity) : SlocColor(f.Sloc);
            chart.AddItem(barLabel, value, color);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(chart);
    }

    public void ReportSmellsChart(IReadOnlyList<FileMetrics> files, int topN = 10)
    {
        var top = files
            .Where(f => f.SmellsHigh > 0 || f.SmellsMedium > 0 || f.SmellsLow > 0)
            .OrderByDescending(WeightedSmells)
            .Take(topN)
            .ToList();

        if (top.Count == 0)
            return;

        var chart = new BarChart()
            .Width(80)
            .Label("[bold]Top files by Code Smells (weighted H×3 + M×2 + L×1)[/]")
            .CenterLabel();

        for (int i = 0; i < top.Count; i++)
        {
            int score = WeightedSmells(top[i]);
            chart.AddItem(Path.GetFileName(top[i].Path), score, SmellColor(score));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(chart);
    }

    private static int WeightedSmells(FileMetrics f) =>
        f.SmellsHigh * 3 + f.SmellsMedium * 2 + f.SmellsLow;

    public void ReportRepoMetrics(RepoMetrics repoMetrics)
    {
        AnsiConsole.MarkupLine($"[bold cyan]Commit:[/] [yellow]{repoMetrics.CommitHash[..Math.Min(8, repoMetrics.CommitHash.Length)]}[/]  " +
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
}
