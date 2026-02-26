using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace CodeEvo.Reporting;

// ── Data model ────────────────────────────────────────────────────────────────

public record DataJsonSummary(double Entropy, int Files, int Sloc);

public record DataJsonHistoryEntry(string Hash, string Date, double Entropy, int Files, int Sloc);

public record DataJsonFileEntry(
    string Path,
    string Language,
    int Sloc,
    double CyclomaticComplexity,
    double MaintainabilityIndex,
    int SmellsHigh,
    int SmellsMedium,
    int SmellsLow,
    double CouplingProxy,
    double Badness,
    string Kind = "Production");

/// <summary>Represents a parsed data.json snapshot produced by <c>report --html</c>.</summary>
public class DataJsonReport
{
    public string Generated { get; init; } = "";
    public int CommitCount { get; init; }
    public DataJsonSummary? Summary { get; init; }
    public IReadOnlyList<DataJsonHistoryEntry> History { get; init; } = [];
    public IReadOnlyList<DataJsonFileEntry> LatestFiles { get; init; } = [];

    /// <summary>Parses a data.json string into a <see cref="DataJsonReport"/>.</summary>
    public static DataJsonReport Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        DataJsonSummary? summary = null;
        if (root.TryGetProperty("summary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.Object)
        {
            summary = new DataJsonSummary(
                summaryEl.TryGetProperty("entropy", out var e) ? e.GetDouble() : 0,
                summaryEl.TryGetProperty("files",   out var f) ? f.GetInt32()  : 0,
                summaryEl.TryGetProperty("sloc",    out var s) ? s.GetInt32()  : 0);
        }

        var history = new List<DataJsonHistoryEntry>();
        if (root.TryGetProperty("history", out var histEl) && histEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in histEl.EnumerateArray())
            {
                history.Add(new DataJsonHistoryEntry(
                    h.TryGetProperty("hash",    out var hh) ? hh.GetString() ?? "" : "",
                    h.TryGetProperty("date",    out var hd) ? hd.GetString() ?? "" : "",
                    h.TryGetProperty("entropy", out var he) ? he.GetDouble() : 0,
                    h.TryGetProperty("files",   out var hf) ? hf.GetInt32()  : 0,
                    h.TryGetProperty("sloc",    out var hs) ? hs.GetInt32()  : 0));
            }
        }

        var latestFiles = new List<DataJsonFileEntry>();
        if (root.TryGetProperty("latestFiles", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var lf in filesEl.EnumerateArray())
            {
                latestFiles.Add(new DataJsonFileEntry(
                    lf.TryGetProperty("path",                  out var p)  ? p.GetString()  ?? "" : "",
                    lf.TryGetProperty("language",              out var la) ? la.GetString() ?? "" : "",
                    lf.TryGetProperty("sloc",                  out var sl) ? sl.GetInt32()       : 0,
                    lf.TryGetProperty("cyclomaticComplexity",  out var cc) ? cc.GetDouble()      : 0,
                    lf.TryGetProperty("maintainabilityIndex",  out var mi) ? mi.GetDouble()      : 0,
                    lf.TryGetProperty("smellsHigh",            out var sh) ? sh.GetInt32()       : 0,
                    lf.TryGetProperty("smellsMedium",          out var sm) ? sm.GetInt32()       : 0,
                    lf.TryGetProperty("smellsLow",             out var slo)? slo.GetInt32()      : 0,
                    lf.TryGetProperty("couplingProxy",         out var cp) ? cp.GetDouble()      : 0,
                    lf.TryGetProperty("badness",               out var b)  ? b.GetDouble()       : 0,
                    lf.TryGetProperty("kind",                  out var ki) ? ki.GetString() ?? "Production" : "Production"));
            }
        }

        return new DataJsonReport
        {
            Generated   = root.TryGetProperty("generated",   out var g)  ? g.GetString()  ?? "" : "",
            CommitCount = root.TryGetProperty("commitCount", out var cc2) ? cc2.GetInt32()       : 0,
            Summary     = summary,
            History     = history,
            LatestFiles = latestFiles,
        };
    }
}

// ── Assessment ────────────────────────────────────────────────────────────────

public enum Verdict { Stable, Warming, Cooling, HeatSpike, HeatWave, ColdFront }

public record ComparisonAssessment(
    Verdict Verdict,
    string VerdictLabel,
    string Summary,
    IReadOnlyList<string> Observations);

// ── Reporter ──────────────────────────────────────────────────────────────────

/// <summary>Compares two data.json snapshots and generates rich HTML or console assessments.</summary>
public class ComparisonReporter
{
    private const int TopFilesCount = 10;
    private const string ReportDateFormat = "yyyy-MM-dd HH:mm 'UTC'";
    /// <summary>Minimum absolute entropy change used across spike/wave/front detectors.</summary>
    private const double EntropyElevationThreshold = 0.05;

    private static string ShortDate(string generated) =>
        generated[..Math.Min(10, generated.Length)];

    /// <summary>
    /// The six weather-forecast conditions used to describe EntropyX drift trends,
    /// in display order. Each entry is (emoji, name, description).
    /// </summary>
    public static readonly IReadOnlyList<(string Emoji, string Name, string Description)> WeatherLegend =
    [
        ("⚖️",  "Stable",      "EntropyX is flat within normal variability — no significant structural drift."),
        ("🌡️", "Warming",     "EntropyX is trending up steadily — structural drift accumulating commit by commit."),
        ("🧊",  "Cooling",     "EntropyX is trending downward — stabilization or refactor benefit in progress."),
        ("🔥",  "Heat Spike",  "EntropyX jumps sharply in a single step — a regression event was detected."),
        ("♨️",  "Heat Wave",   "EntropyX stays elevated across a sustained window — drift accumulating unchecked."),
        ("❄️",  "Cold Front",  "EntropyX drops consistently over multiple commits — focused cleanup paying off."),
    ];

    // ── Public API ─────────────────────────────────────────────────────────────

    public string GenerateHtml(DataJsonReport baseline, DataJsonReport current)
    {
        var assessment = BuildAssessment(baseline, current);
        var sb = new StringBuilder();
        AppendHtmlHeader(sb, baseline, current);
        AppendEntropyHero(sb, baseline, current);
        AppendAssessmentSection(sb, assessment);
        AppendWeatherLegend(sb);
        AppendMetricsComparison(sb, baseline, current);
        AppendTrendChart(sb, baseline, current);
        AppendFilesComparison(sb, baseline, current);
        AppendHtmlFooter(sb);
        return sb.ToString();
    }

    public void ReportToConsole(DataJsonReport baseline, DataJsonReport current)
    {
        var assessment = BuildAssessment(baseline, current);

        AnsiConsole.MarkupLine("\n[bold cyan]⚡ EntropyX Evolutionary Assessment[/]\n");

        // Entropy prime indicator
        double bEntropy = baseline.Summary?.Entropy ?? 0;
        double cEntropy = current.Summary?.Entropy ?? 0;
        double entropyDelta = cEntropy - bEntropy;

        var verdictColor = assessment.Verdict switch
        {
            Verdict.Stable     => "grey",
            Verdict.Warming    => "yellow",
            Verdict.Cooling    => "cyan",
            Verdict.HeatSpike  => "bold red",
            Verdict.HeatWave   => "red",
            Verdict.ColdFront  => "bold cyan",
            _ => "white"
        };

        AnsiConsole.MarkupLine($"[bold]Forecast:[/] [{verdictColor}]{Markup.Escape(assessment.VerdictLabel)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(assessment.Summary)}[/]\n");

        // Metrics table
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Metric");
        table.AddColumn(new TableColumn("Baseline").RightAligned());
        table.AddColumn(new TableColumn("Current").RightAligned());
        table.AddColumn(new TableColumn("Delta").RightAligned());

        AddConsoleRow(table, "EntropyX Score ★", bEntropy.ToString("F4", CultureInfo.InvariantCulture),
            cEntropy.ToString("F4", CultureInfo.InvariantCulture), entropyDelta, "F4", higherIsBad: true);
        AddConsoleRow(table, "Files",
            (baseline.Summary?.Files ?? 0).ToString(),
            (current.Summary?.Files  ?? 0).ToString(),
            (current.Summary?.Files  ?? 0) - (baseline.Summary?.Files ?? 0), "0", higherIsBad: false);
        AddConsoleRow(table, "SLOC",
            (baseline.Summary?.Sloc ?? 0).ToString("N0"),
            (current.Summary?.Sloc  ?? 0).ToString("N0"),
            (double)((current.Summary?.Sloc ?? 0) - (baseline.Summary?.Sloc ?? 0)), "N0", higherIsBad: false);
        AddConsoleRow(table, "Commits",
            baseline.CommitCount.ToString(),
            current.CommitCount.ToString(),
            current.CommitCount - baseline.CommitCount, "0", higherIsBad: false);

        AnsiConsole.Write(table);

        // Observations
        if (assessment.Observations.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Observations:[/]");
            foreach (var obs in assessment.Observations)
                AnsiConsole.MarkupLine($"  • {Markup.Escape(obs)}");
        }

        // Weather legend
        AnsiConsole.WriteLine();
        var legendTable = new Table().Border(TableBorder.Rounded).Title("[grey]Drift Forecast — Legend[/]");
        legendTable.AddColumn("[grey]Condition[/]");
        legendTable.AddColumn("[grey]Meaning[/]");
        foreach (var (emoji, name, desc) in WeatherLegend)
            legendTable.AddRow($"{Markup.Escape(emoji)} [bold]{Markup.Escape(name)}[/]", $"[grey]{Markup.Escape(desc)}[/]");
        AnsiConsole.Write(legendTable);

        AnsiConsole.WriteLine();
    }

    // ── Assessment logic ───────────────────────────────────────────────────────

    public static ComparisonAssessment BuildAssessment(DataJsonReport baseline, DataJsonReport current)
    {
        double bEntropy = baseline.Summary?.Entropy ?? 0;
        double cEntropy = current.Summary?.Entropy ?? 0;
        double entropyDelta = cEntropy - bEntropy;
        double relativeDelta = bEntropy == 0 ? 0 : entropyDelta / bEntropy;

        int bFiles = baseline.Summary?.Files ?? 0;
        int cFiles = current.Summary?.Files  ?? 0;
        int bSloc  = baseline.Summary?.Sloc  ?? 0;
        int cSloc  = current.Summary?.Sloc   ?? 0;
        int slocDelta  = cSloc  - bSloc;
        int filesDelta = cFiles - bFiles;

        var observations = new List<string>();

        // Entropy trend within current snapshot
        double currentTrend = ComputeEntropyTrend(current.History);

        // Primary entropy observation (drift-framed language)
        if (Math.Abs(entropyDelta) < 0.001)
            observations.Add("EntropyX is virtually unchanged between snapshots — no significant drift detected.");
        else if (entropyDelta < 0)
            observations.Add($"EntropyX dropped by {Math.Abs(entropyDelta):F4} ({Math.Abs(relativeDelta):P1}) since baseline — the codebase has cooled.");
        else
            observations.Add($"EntropyX rose by {entropyDelta:F4} ({relativeDelta:P1}) since baseline — structural drift is accumulating.");

        // Internal trend observation
        if (current.History.Count >= 3)
        {
            if (currentTrend > 0.001)
                observations.Add($"The codebase is warming within this snapshot — entropy rising at ~{currentTrend:F4} per commit.");
            else if (currentTrend < -0.001)
                observations.Add($"The codebase is cooling within this snapshot — entropy falling at ~{Math.Abs(currentTrend):F4} per commit.");
            else
                observations.Add("Entropy is flat within this snapshot — the codebase temperature is stable.");
        }

        // SLOC growth relative to entropy direction
        if (slocDelta > 0 && entropyDelta <= 0)
            observations.Add($"Codebase grew by {slocDelta:N0} SLOC while entropy cooled — a positive sign of controlled growth.");
        else if (slocDelta > 0 && entropyDelta > 0)
            observations.Add($"Codebase grew by {slocDelta:N0} SLOC with rising entropy — complexity is spreading.");
        else if (slocDelta < 0 && entropyDelta < 0)
            observations.Add($"Codebase shrank by {Math.Abs(slocDelta):N0} SLOC and entropy improved — likely effective dead code removal.");

        // File count change
        if (filesDelta > 0)
            observations.Add($"{filesDelta} new file(s) added since baseline.");
        else if (filesDelta < 0)
            observations.Add($"{Math.Abs(filesDelta)} file(s) removed since baseline.");

        // Per-file temperature observations
        var worsenedFiles = GetWorsenedFiles(baseline, current);
        var improvedFiles = GetImprovedFiles(baseline, current);
        if (worsenedFiles.Count > 0)
            observations.Add($"{worsenedFiles.Count} file(s) are running hotter (higher badness) than at baseline (e.g. {worsenedFiles[0].Path}).");
        if (improvedFiles.Count > 0)
            observations.Add($"{improvedFiles.Count} file(s) have cooled down (lower badness) since baseline (e.g. {improvedFiles[0].Path}).");

        // Determine weather forecast — HeatWave takes priority over HeatSpike because
        // sustained elevation is more severe than a single spike that may have subsided.
        bool heatWave  = cEntropy > bEntropy && DetectHeatWave(current.History);
        bool heatSpike = !heatWave && entropyDelta > 0 && DetectHeatSpike(current.History);
        bool coldFront = entropyDelta < -EntropyElevationThreshold && DetectColdFront(current.History);

        Verdict verdict;
        string verdictLabel;
        string summary;

        if (heatWave)
        {
            verdict = Verdict.HeatWave;
            verdictLabel = "♨️ Heat Wave";
            summary = "EntropyX has remained elevated across multiple commits — sustained structural drift is accumulating without correction. Consider a focused refactoring session.";
        }
        else if (heatSpike)
        {
            verdict = Verdict.HeatSpike;
            verdictLabel = "🔥 Heat Spike";
            summary = "EntropyX spiked sharply — a single regression event has significantly disrupted the codebase's structural balance. Investigate recent commits.";
        }
        else if (coldFront)
        {
            verdict = Verdict.ColdFront;
            verdictLabel = "❄️ Cold Front";
            summary = "A sustained entropy reduction is underway — focused refactoring or cleanup efforts are paying off with a meaningful structural improvement.";
        }
        else if (currentTrend > 0.001 && entropyDelta > 0.01)
        {
            verdict = Verdict.Warming;
            verdictLabel = "🌡️ Warming";
            summary = "EntropyX is drifting upward steadily. No immediate crisis, but structural complexity is accumulating — keep an eye on hot spots.";
        }
        else if (currentTrend < -0.001 && entropyDelta <= 0)
        {
            verdict = Verdict.Cooling;
            verdictLabel = "🧊 Cooling";
            summary = "EntropyX is trending downward — the codebase is stabilizing. Refactoring and cleanup efforts are having a measurable effect.";
        }
        else
        {
            verdict = Verdict.Stable;
            verdictLabel = "⚖️ Stable";
            summary = "EntropyX is flat within normal variability — no significant structural drift detected. The codebase temperature is holding steady.";
        }

        return new ComparisonAssessment(verdict, verdictLabel, summary, observations);
    }

    // ── Weather condition detectors ────────────────────────────────────────────

    /// <summary>
    /// Returns true if a single commit in the history caused a sharp entropy spike —
    /// i.e. the largest positive step is a statistical outlier (> mean + 1.5σ) AND at least
    /// <see cref="EntropyElevationThreshold"/> in absolute terms. The absolute guard prevents
    /// false positives in histories where all steps are tiny floating-point noise.
    /// </summary>
    public static bool DetectHeatSpike(IReadOnlyList<DataJsonHistoryEntry> history)
    {
        if (history.Count < 3) return false;
        var steps = new List<double>(history.Count - 1);
        for (int i = 1; i < history.Count; i++)
            steps.Add(history[i].Entropy - history[i - 1].Entropy);
        double mean = steps.Average();
        double variance = steps.Average(d => (d - mean) * (d - mean));
        double stdDev = Math.Sqrt(variance);
        double maxStep = steps.Max();
        return maxStep > mean + 1.5 * stdDev && maxStep > EntropyElevationThreshold;
    }

    /// <summary>
    /// Returns true if entropy has been elevated AND stable (not still climbing) for the last
    /// <paramref name="minWindow"/> commits. This distinguishes a "heat wave" (sustained plateau
    /// at a high level) from "warming" (entropy still rising step by step).
    /// Requires at least <paramref name="minWindow"/> + 2 history entries.
    /// </summary>
    public static bool DetectHeatWave(IReadOnlyList<DataJsonHistoryEntry> history, int minWindow = 3)
    {
        if (history.Count < minWindow + 2) return false;
        // Reference = point just before the elevated window
        int refIdx = history.Count - minWindow - 1;
        double refEntropy = history[refIdx].Entropy;

        // All window points must be strictly elevated above the reference (not just at the threshold)
        for (int i = refIdx + 1; i < history.Count; i++)
            if (history[i].Entropy <= refEntropy + EntropyElevationThreshold)
                return false;

        // The elevation must be relatively stable (not a steep ongoing climb).
        // A heat wave = jumped up AND the rise *within* the window is smaller than the jump itself.
        double firstOfWindow = history[refIdx + 1].Entropy;
        double lastOfWindow  = history[history.Count - 1].Entropy;
        double elevation     = firstOfWindow - refEntropy;
        double windowRise    = lastOfWindow  - firstOfWindow;

        return elevation > EntropyElevationThreshold && Math.Abs(windowRise) < elevation;
    }

    /// <summary>
    /// Returns true if entropy has been declining in a sustained, consecutive manner
    /// for at least 70% of the last <paramref name="minWindow"/> steps.
    /// </summary>
    public static bool DetectColdFront(IReadOnlyList<DataJsonHistoryEntry> history, int minWindow = 3)
    {
        if (history.Count < minWindow + 1) return false;
        int declineCount = 0;
        for (int i = history.Count - minWindow; i < history.Count; i++)
            if (history[i].Entropy < history[i - 1].Entropy)
                declineCount++;
        return declineCount >= (int)Math.Ceiling(minWindow * 0.7);
    }

    // ── File diff helpers ──────────────────────────────────────────────────────

    public static IReadOnlyList<DataJsonFileEntry> GetWorsenedFiles(DataJsonReport baseline, DataJsonReport current)
    {
        var baseMap = baseline.LatestFiles.ToDictionary(f => f.Path);
        return current.LatestFiles
            .Where(f => baseMap.TryGetValue(f.Path, out var b) && f.Badness > b.Badness + 1e-9)
            .OrderByDescending(f => f.Badness - baseMap[f.Path].Badness)
            .ToList();
    }

    public static IReadOnlyList<DataJsonFileEntry> GetImprovedFiles(DataJsonReport baseline, DataJsonReport current)
    {
        var baseMap = baseline.LatestFiles.ToDictionary(f => f.Path);
        return current.LatestFiles
            .Where(f => baseMap.TryGetValue(f.Path, out var b) && f.Badness < b.Badness - 1e-9)
            .OrderBy(f => f.Badness - baseMap[f.Path].Badness)
            .ToList();
    }

    public static IReadOnlyList<DataJsonFileEntry> GetNewFiles(DataJsonReport baseline, DataJsonReport current)
    {
        var baseSet = baseline.LatestFiles.Select(f => f.Path).ToHashSet();
        return current.LatestFiles.Where(f => !baseSet.Contains(f.Path))
            .OrderByDescending(f => f.Badness).ToList();
    }

    public static IReadOnlyList<DataJsonFileEntry> GetRemovedFiles(DataJsonReport baseline, DataJsonReport current)
    {
        var curSet = current.LatestFiles.Select(f => f.Path).ToHashSet();
        return baseline.LatestFiles.Where(f => !curSet.Contains(f.Path))
            .OrderByDescending(f => f.Badness).ToList();
    }

    // ── Trend computation ──────────────────────────────────────────────────────

    /// <summary>Computes the average entropy change per commit step across the history (linear slope proxy).</summary>
    public static double ComputeEntropyTrend(IReadOnlyList<DataJsonHistoryEntry> history)
    {
        if (history.Count < 2) return 0;
        double total = 0;
        for (int i = 1; i < history.Count; i++)
            total += history[i].Entropy - history[i - 1].Entropy;
        return total / (history.Count - 1);
    }

    // ── HTML generation ────────────────────────────────────────────────────────

    private static void AppendHtmlHeader(StringBuilder sb, DataJsonReport baseline, DataJsonReport current)
    {
        string reportDate = DateTimeOffset.UtcNow.ToString(ReportDateFormat, CultureInfo.InvariantCulture);
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>EntropyX Evolutionary Comparison</title>
              <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.umd.min.js"></script>
              <style>
                :root {
                  --bg: #0f1117; --card: #1a1d27; --border: #2d3044;
                  --text: #e0e0e0; --muted: #888; --accent: #7c6af7;
                  --green: #22c55e; --red: #ef4444; --yellow: #f59e0b;
                  --font: 'Segoe UI', system-ui, sans-serif;
                  --baseline: #60a5fa; --current: #f97316;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body { background: var(--bg); color: var(--text); font-family: var(--font); font-size: 14px; line-height: 1.6; }
                h1 { font-size: 2rem; font-weight: 700; }
                h2 { font-size: 1.3rem; font-weight: 600; color: var(--accent); margin-bottom: 1rem; }
                h3 { font-size: 1rem; font-weight: 600; color: var(--muted); margin-bottom: 0.5rem; text-transform: uppercase; letter-spacing: .05em; }
                header { padding: 2rem 2.5rem; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 1rem; }
                header .subtitle { color: var(--muted); font-size: 0.85rem; margin-top: 0.25rem; }
                .container { max-width: 1280px; margin: 0 auto; padding: 2rem 2.5rem; }
                .grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem; }
                .grid-3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1.5rem; }
                .grid-4 { display: grid; grid-template-columns: 1fr 1fr 1fr 1fr; gap: 1.5rem; }
                @media (max-width: 900px) { .grid-2, .grid-3, .grid-4 { grid-template-columns: 1fr; } }
                .card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 1.5rem; }
                .stat { text-align: center; }
                .stat .value { font-size: 2.2rem; font-weight: 700; }
                .stat .label { color: var(--muted); font-size: 0.85rem; text-transform: uppercase; letter-spacing: .06em; }
                .stat .sub { font-size: 0.8rem; margin-top: 0.25rem; }
                .chart-card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 1.5rem; }
                .chart-wrap { position: relative; height: 280px; }
                table { width: 100%; border-collapse: collapse; font-size: 13px; }
                th { text-align: left; padding: 0.5rem 0.75rem; color: var(--muted); font-weight: 600; text-transform: uppercase; letter-spacing: .05em; border-bottom: 1px solid var(--border); }
                td { padding: 0.45rem 0.75rem; border-bottom: 1px solid var(--border); word-break: break-all; }
                tr:last-child td { border-bottom: none; }
                tr:hover td { background: rgba(255,255,255,.03); }
                .badge { display: inline-block; padding: 0.15rem 0.55rem; border-radius: 999px; font-size: 11px; font-weight: 600; }
                .badge-red    { background: rgba(239,68,68,.15);   color: var(--red); }
                .badge-green  { background: rgba(34,197,94,.15);   color: var(--green); }
                .badge-yellow { background: rgba(245,158,11,.15);  color: var(--yellow); }
                .badge-gray   { background: rgba(136,136,136,.15); color: var(--muted); }
                .badge-blue   { background: rgba(96,165,250,.15);  color: var(--baseline); }
                .badge-orange { background: rgba(249,115,22,.15);  color: var(--current); }
                .delta-pos  { color: var(--red); }
                .delta-neg  { color: var(--green); }
                .delta-zero { color: var(--muted); }
                section { margin-bottom: 2.5rem; }
                .section-title { font-size: 1.3rem; font-weight: 600; color: var(--accent); margin-bottom: 1rem; padding-bottom: 0.5rem; border-bottom: 1px solid var(--border); }
                .verdict-box        { border-radius: 12px; padding: 1.5rem 2rem; border: 2px solid; margin-bottom: 1.5rem; }
                .verdict-stable     { border-color: #94a3b8;      background: rgba(148,163,184,.08); }
                .verdict-warming    { border-color: var(--yellow); background: rgba(245,158,11,.08); }
                .verdict-cooling    { border-color: #38bdf8;       background: rgba(56,189,248,.08); }
                .verdict-heat-spike { border-color: var(--red);    background: rgba(239,68,68,.12); }
                .verdict-heat-wave  { border-color: #f97316;       background: rgba(249,115,22,.12); }
                .verdict-cold-front { border-color: #818cf8;       background: rgba(129,140,248,.08); }
                .verdict-label { font-size: 1.4rem; font-weight: 700; margin-bottom: 0.4rem; }
                .verdict-summary { color: var(--text); font-size: 0.95rem; }
                .obs-list { list-style: none; padding: 0; margin-top: 1rem; }
                .obs-list li { padding: 0.3rem 0; color: var(--text); font-size: 0.9rem; }
                .obs-list li::before { content: '→ '; color: var(--muted); }
                .entropy-hero { display: flex; align-items: center; justify-content: center; gap: 3rem; padding: 2rem; flex-wrap: wrap; }
                .entropy-hero .side { text-align: center; }
                .entropy-hero .side .tag { font-size: 0.75rem; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); margin-bottom: 0.25rem; }
                .entropy-hero .side .score { font-size: 3rem; font-weight: 800; font-variant-numeric: tabular-nums; }
                .entropy-hero .side .score.baseline-color { color: var(--baseline); }
                .entropy-hero .side .score.current-color  { color: var(--current); }
                .entropy-hero .arrow { font-size: 2rem; color: var(--muted); }
                .entropy-hero .delta-panel { text-align: center; background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 1rem 1.5rem; }
                .entropy-hero .delta-panel .delta-value { font-size: 1.8rem; font-weight: 700; }
                .entropy-hero .delta-panel .delta-tag { font-size: 0.75rem; color: var(--muted); text-transform: uppercase; letter-spacing: .08em; }
                .legend-dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 4px; }
                .dot-baseline { background: var(--baseline); }
                .dot-current  { background: var(--current); }
                details { margin-bottom: 1rem; }
                details > summary { cursor: pointer; list-style: none; display: flex; align-items: center; gap: 0.5rem; padding: 0.6rem 0.75rem; background: rgba(124,106,247,.08); border: 1px solid var(--border); border-radius: 8px; color: var(--accent); font-weight: 600; font-size: 0.9rem; user-select: none; }
                details > summary::-webkit-details-marker { display: none; }
                details > summary::before { content: '▶'; font-size: 0.7rem; transition: transform .2s; }
                details[open] > summary::before { transform: rotate(90deg); }
                details .details-body { padding: 1rem 0; }
                footer { padding: 1.5rem 2.5rem; border-top: 1px solid var(--border); color: var(--muted); font-size: 12px; text-align: center; }
              </style>
            </head>
            <body>
            <header>
              <div>
                <h1>⚡ EntropyX Evolutionary Comparison</h1>
                <div class="subtitle">Generated {{EscapeHtml(reportDate)}}&nbsp;&nbsp;·&nbsp;&nbsp;Baseline: {{EscapeHtml(ShortDate(baseline.Generated))}} ({{baseline.CommitCount}} commits)&nbsp;&nbsp;→&nbsp;&nbsp;Current: {{EscapeHtml(ShortDate(current.Generated))}} ({{current.CommitCount}} commits)</div>
              </div>
            </header>
            <div class="container">
            """);
    }

    private static void AppendEntropyHero(StringBuilder sb, DataJsonReport baseline, DataJsonReport current)
    {
        double bE = baseline.Summary?.Entropy ?? 0;
        double cE = current.Summary?.Entropy  ?? 0;
        double delta = cE - bE;
        string deltaSign = delta >= 0 ? "+" : "";
        string deltaClass = delta > 0 ? "delta-pos" : delta < 0 ? "delta-neg" : "delta-zero";
        string bBadge = EntropyBadgeSvg(bE);
        string cBadge = EntropyBadgeSvg(cE);

        sb.AppendLine($$"""
            <section>
              <div class="section-title">EntropyX Score — Prime Indicator</div>
              <div class="card">
                <div class="entropy-hero">
                  <div class="side">
                    <div class="tag"><span class="legend-dot dot-baseline"></span>Baseline</div>
                    <div class="score baseline-color">{{bE.ToString("F4", CultureInfo.InvariantCulture)}}</div>
                    <div style="margin-top:.5rem">{{bBadge}}</div>
                    <div style="color:var(--muted);font-size:.8rem;margin-top:.5rem">{{EscapeHtml(ShortDate(baseline.Generated))}} · {{baseline.CommitCount}} commits</div>
                  </div>
                  <div class="arrow">→</div>
                  <div class="side">
                    <div class="tag"><span class="legend-dot dot-current"></span>Current</div>
                    <div class="score current-color">{{cE.ToString("F4", CultureInfo.InvariantCulture)}}</div>
                    <div style="margin-top:.5rem">{{cBadge}}</div>
                    <div style="color:var(--muted);font-size:.8rem;margin-top:.5rem">{{EscapeHtml(ShortDate(current.Generated))}} · {{current.CommitCount}} commits</div>
                  </div>
                  <div class="delta-panel">
                    <div class="delta-tag">Δ EntropyX</div>
                    <div class="delta-value {{deltaClass}}">{{deltaSign}}{{delta.ToString("F4", CultureInfo.InvariantCulture)}}</div>
                    <div style="color:var(--muted);font-size:.8rem;margin-top:.25rem">{{(bE == 0 ? "n/a" : $"{(delta / bE):+0.0%;-0.0%;0.0%}")}}</div>
                  </div>
                </div>
              </div>
            </section>
            """);
    }

    private static void AppendAssessmentSection(StringBuilder sb, ComparisonAssessment assessment)
    {
        string verdictCssClass = assessment.Verdict switch
        {
            Verdict.Stable     => "verdict-stable",
            Verdict.Warming    => "verdict-warming",
            Verdict.Cooling    => "verdict-cooling",
            Verdict.HeatSpike  => "verdict-heat-spike",
            Verdict.HeatWave   => "verdict-heat-wave",
            Verdict.ColdFront  => "verdict-cold-front",
            _ => "verdict-stable"
        };

        sb.AppendLine($"""
            <section>
              <div class="section-title">Evolutionary Assessment</div>
              <div class="verdict-box {EscapeHtml(verdictCssClass)}">
                <div class="verdict-label">{EscapeHtml(assessment.VerdictLabel)}</div>
                <div class="verdict-summary">{EscapeHtml(assessment.Summary)}</div>
                <ul class="obs-list">
            """);

        foreach (var obs in assessment.Observations)
            sb.AppendLine($"      <li>{EscapeHtml(obs)}</li>");

        sb.AppendLine("""
                </ul>
              </div>
            </section>
            """);
    }

    private static void AppendWeatherLegend(StringBuilder sb)
    {
        sb.AppendLine("""
            <section>
              <div class="section-title">Drift Forecast — Legend</div>
              <div class="card">
                <p style="color:var(--muted);font-size:.85rem;margin-bottom:1rem">
                  EntropyX is a <em>drift metric</em>, not a grade. These conditions describe the <strong>shape of entropy over time</strong> — not a pass/fail score.
                </p>
                <table>
                  <thead><tr>
                    <th style="width:8rem">Condition</th>
                    <th>Meaning</th>
                  </tr></thead>
                  <tbody>
            """);

        foreach (var (emoji, name, desc) in WeatherLegend)
        {
            sb.AppendLine($"""
                    <tr>
                      <td><span style="font-size:1.1rem">{EscapeHtml(emoji)}</span>&nbsp;<strong>{EscapeHtml(name)}</strong></td>
                      <td style="color:var(--muted)">{EscapeHtml(desc)}</td>
                    </tr>
                """);
        }

        sb.AppendLine("""
                  </tbody>
                </table>
              </div>
            </section>
            """);
    }

    private static void AppendMetricsComparison(StringBuilder sb, DataJsonReport baseline, DataJsonReport current)
    {
        double bE = baseline.Summary?.Entropy ?? 0;
        double cE = current.Summary?.Entropy  ?? 0;
        int bF = baseline.Summary?.Files ?? 0;
        int cF = current.Summary?.Files  ?? 0;
        int bS = baseline.Summary?.Sloc  ?? 0;
        int cS = current.Summary?.Sloc   ?? 0;

        sb.AppendLine("""
            <section>
              <div class="section-title">Metrics Overview</div>
              <div class="grid-4">
            """);

        AppendMetricCard(sb, "EntropyX Score ★", bE.ToString("F4", CultureInfo.InvariantCulture),
            cE.ToString("F4", CultureInfo.InvariantCulture), cE - bE, "F4", higherIsBad: true, highlight: true);
        AppendMetricCard(sb, "Total Files",
            bF.ToString(CultureInfo.InvariantCulture),
            cF.ToString(CultureInfo.InvariantCulture),
            cF - bF, "0", higherIsBad: false);
        AppendMetricCard(sb, "Total SLOC",
            bS.ToString("N0", CultureInfo.InvariantCulture),
            cS.ToString("N0", CultureInfo.InvariantCulture),
            (double)(cS - bS), "N0", higherIsBad: false);
        AppendMetricCard(sb, "Commits Analysed",
            baseline.CommitCount.ToString(CultureInfo.InvariantCulture),
            current.CommitCount.ToString(CultureInfo.InvariantCulture),
            current.CommitCount - baseline.CommitCount, "0", higherIsBad: false);

        sb.AppendLine("  </div>\n</section>");
    }

    private static void AppendMetricCard(StringBuilder sb, string label, string bVal, string cVal,
        double delta, string fmt, bool higherIsBad, bool highlight = false)
    {
        string sign = delta >= 0 ? "+" : "";
        string deltaClass = delta == 0 ? "delta-zero"
            : (higherIsBad ? (delta > 0 ? "delta-pos" : "delta-neg") : (delta < 0 ? "delta-pos" : "delta-neg"));
        string accentStyle = highlight ? "color:var(--accent);" : "";

        sb.AppendLine($$"""
                <div class="card stat">
                  <div class="label">{{EscapeHtml(label)}}</div>
                  <div class="value" style="font-size:1.4rem;{{accentStyle}}">
                    <span style="color:var(--baseline)">{{EscapeHtml(bVal)}}</span>
                    <span style="color:var(--muted);font-size:1rem"> → </span>
                    <span style="color:var(--current)">{{EscapeHtml(cVal)}}</span>
                  </div>
                  <div class="sub {{deltaClass}}">{{sign}}{{delta.ToString(fmt, CultureInfo.InvariantCulture)}}</div>
                </div>
            """);
    }

    private static void AppendTrendChart(StringBuilder sb, DataJsonReport baseline, DataJsonReport current)
    {
        var bHistory = Downsample(baseline.History, 300);
        var cHistory = Downsample(current.History, 300);

        string bLabels = string.Join(",", bHistory.Select(h => $"\"{EscapeJson(h.Date)}\""));
        string bData   = string.Join(",", bHistory.Select(h => h.Entropy.ToString("F6", CultureInfo.InvariantCulture)));
        string cLabels = string.Join(",", cHistory.Select(h => $"\"{EscapeJson(h.Date)}\""));
        string cData   = string.Join(",", cHistory.Select(h => h.Entropy.ToString("F6", CultureInfo.InvariantCulture)));

        sb.AppendLine($$"""
            <section>
              <div class="section-title">Entropy Trend Comparison</div>
              <div class="chart-card">
                <div style="display:flex;gap:1.5rem;margin-bottom:.75rem;font-size:.85rem;">
                  <span><span class="legend-dot dot-baseline" style="display:inline-block;width:12px;height:12px;border-radius:50%;background:var(--baseline);margin-right:4px;vertical-align:middle"></span>Baseline ({{baseline.CommitCount}} commits)</span>
                  <span><span class="legend-dot dot-current" style="display:inline-block;width:12px;height:12px;border-radius:50%;background:var(--current);margin-right:4px;vertical-align:middle"></span>Current ({{current.CommitCount}} commits)</span>
                </div>
                <div class="chart-wrap"><canvas id="trendChart"></canvas></div>
              </div>
            </section>
            """);

        sb.AppendLine("<script>");
        sb.AppendLine("(function() {");
        sb.AppendLine($"  var bLabels=[{bLabels}], bData=[{bData}], cLabels=[{cLabels}], cData=[{cData}];");
        sb.AppendLine("  function mkDs(lbl, data, col, bg, pr) {");
        sb.AppendLine("    return { label: lbl, data: data.map((v,i) => ({x:i, y:+v})),");
        sb.AppendLine("             borderColor: col, backgroundColor: bg, borderWidth: 2,");
        sb.AppendLine("             pointRadius: pr, tension: 0.3, parsing: false };");
        sb.AppendLine("  }");
        sb.AppendLine($"  var bPr={(bHistory.Count <= 1 ? 5 : 0)}, cPr={(cHistory.Count <= 1 ? 5 : 0)};");
        sb.AppendLine("  var ctx = document.getElementById('trendChart').getContext('2d');");
        sb.AppendLine("  new Chart(ctx, {");
        sb.AppendLine("    type: 'line',");
        sb.AppendLine("    data: {");
        sb.AppendLine("      labels: bLabels.length >= cLabels.length ? bLabels : cLabels,");
        sb.AppendLine("      datasets: [mkDs('Baseline', bData, '#60a5fa', 'rgba(96,165,250,.1)', bPr),");
        sb.AppendLine("                 mkDs('Current',  cData, '#f97316', 'rgba(249,115,22,.1)',  cPr)]");
        sb.AppendLine("    },");
        sb.AppendLine("    options: {");
        sb.AppendLine("      responsive: true, maintainAspectRatio: false,");
        sb.AppendLine("      plugins: { legend: { labels: { color: '#888' } } },");
        sb.AppendLine("      scales: {");
        sb.AppendLine("        x: { type: 'linear', ticks: { color: '#888' }, grid: { color: '#2d3044' } },");
        sb.AppendLine("        y: { ticks: { color: '#888' }, grid: { color: '#2d3044' } }");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("  });");
        sb.AppendLine("})();");
        sb.AppendLine("</script>");
    }

    private static void AppendFilesComparison(StringBuilder sb, DataJsonReport baseline, DataJsonReport current)
    {
        var newFiles     = GetNewFiles(baseline, current).Take(TopFilesCount).ToList();
        var removedFiles = GetRemovedFiles(baseline, current).Take(TopFilesCount).ToList();
        var worsened     = GetWorsenedFiles(baseline, current).Take(TopFilesCount).ToList();
        var improved     = GetImprovedFiles(baseline, current).Take(TopFilesCount).ToList();
        var baseMap = baseline.LatestFiles.ToDictionary(f => f.Path);

        sb.AppendLine("""
            <section>
              <div class="section-title">File-Level Changes</div>
            """);

        AppendDetailsGroup(sb, "Files with Higher Badness (Worsened)", worsened.Count, "No files worsened. ✓",
            () => AppendFilesDiffTable(sb, worsened, baseMap, isWorsened: true), open: true);
        AppendDetailsGroup(sb, "Files with Lower Badness (Improved)", improved.Count, "No files improved.",
            () => AppendFilesDiffTable(sb, improved, baseMap, isWorsened: false));
        AppendDetailsGroup(sb, "New Files", newFiles.Count, "No new files.",
            () => AppendFilesTable(sb, newFiles));
        AppendDetailsGroup(sb, "Removed Files", removedFiles.Count, "No removed files.",
            () => AppendFilesTable(sb, removedFiles));

        sb.AppendLine("</section>");
    }

    private static void AppendDetailsGroup(StringBuilder sb, string title, int count,
        string emptyMsg, Action appendContent, bool open = false)
    {
        string meta = count == 0 ? "none" : $"{count} file(s)";
        sb.AppendLine($"    <details{(open ? " open" : "")}><summary>{EscapeHtml(title)} <span class=\"summary-meta\">{EscapeHtml(meta)}</span></summary>");
        sb.AppendLine("      <div class=\"details-body\">");
        if (count == 0)
            sb.AppendLine($"        <div style=\"color:var(--muted);font-style:italic\">{emptyMsg}</div>");
        else
            appendContent();
        sb.AppendLine("      </div></details>");
    }

    private static void AppendFilesDiffTable(
        StringBuilder sb,
        IReadOnlyList<DataJsonFileEntry> files,
        Dictionary<string, DataJsonFileEntry> baseMap,
        bool isWorsened)
    {
        sb.AppendLine("""
              <table>
                <thead><tr>
                  <th>Path</th><th>Language</th>
                  <th style="text-align:right">Badness (Base)</th>
                  <th style="text-align:right">Badness (Now)</th>
                  <th style="text-align:right">Δ Badness</th>
                  <th style="text-align:right">SLOC</th>
                  <th style="text-align:right">CC</th>
                </tr></thead><tbody>
            """);

        foreach (var f in files)
        {
            double bBadness = baseMap.TryGetValue(f.Path, out var bf) ? bf.Badness : 0;
            double delta = f.Badness - bBadness;
            string deltaSign = delta >= 0 ? "+" : "";
            string deltaClass = isWorsened ? "delta-pos" : "delta-neg";

            sb.AppendLine($$"""
                  <tr>
                    <td style="font-family:monospace;font-size:12px">{{EscapeHtml(f.Path)}}</td>
                    <td>{{EscapeHtml(f.Language)}}</td>
                    <td style="text-align:right">{{bBadness.ToString("F4", CultureInfo.InvariantCulture)}}</td>
                    <td style="text-align:right">{{f.Badness.ToString("F4", CultureInfo.InvariantCulture)}}</td>
                    <td style="text-align:right" class="{{deltaClass}}">{{deltaSign}}{{delta.ToString("F4", CultureInfo.InvariantCulture)}}</td>
                    <td style="text-align:right">{{f.Sloc}}</td>
                    <td style="text-align:right">{{f.CyclomaticComplexity.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                  </tr>
                """);
        }

        sb.AppendLine("    </tbody></table>");
    }

    private static void AppendFilesTable(StringBuilder sb, IReadOnlyList<DataJsonFileEntry> files)
    {
        sb.AppendLine("""
              <table>
                <thead><tr>
                  <th>Path</th><th>Language</th>
                  <th style="text-align:right">SLOC</th>
                  <th style="text-align:right">CC</th>
                  <th style="text-align:right">MI</th>
                  <th style="text-align:right">Badness</th>
                </tr></thead><tbody>
            """);

        foreach (var f in files)
        {
            sb.AppendLine($$"""
                  <tr>
                    <td style="font-family:monospace;font-size:12px">{{EscapeHtml(f.Path)}}</td>
                    <td>{{EscapeHtml(f.Language)}}</td>
                    <td style="text-align:right">{{f.Sloc}}</td>
                    <td style="text-align:right">{{f.CyclomaticComplexity.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                    <td style="text-align:right">{{f.MaintainabilityIndex.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                    <td style="text-align:right">{{f.Badness.ToString("F4", CultureInfo.InvariantCulture)}}</td>
                  </tr>
                """);
        }

        sb.AppendLine("    </tbody></table>");
    }

    private static void AppendHtmlFooter(StringBuilder sb)
    {
        string year = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        sb.AppendLine($$"""
            </div>
            <footer>Generated by EntropyX &copy; {{year}} &nbsp;·&nbsp; Evolutionary comparison report</footer>
            </body>
            </html>
            """);
    }

    // ── Console helpers ────────────────────────────────────────────────────────

    private static void AddConsoleRow(Table table, string label,
        string bVal, string cVal, double delta, string fmt, bool higherIsBad)
    {
        string sign = delta >= 0 ? "+" : "";
        string color = delta == 0 ? "grey"
            : (higherIsBad ? (delta > 0 ? "red" : "green") : (delta < 0 ? "red" : "green"));
        table.AddRow(
            label,
            $"[blue]{Markup.Escape(bVal)}[/]",
            $"[orange1]{Markup.Escape(cVal)}[/]",
            $"[{color}]{sign}{Markup.Escape(delta.ToString(fmt, CultureInfo.InvariantCulture))}[/]");
    }

    // ── Utility ────────────────────────────────────────────────────────────────

    /// <summary>Returns a shields.io-style inline SVG badge for the EntropyX score.</summary>
    private static string EntropyBadgeSvg(double entropy)
    {
        string label = "EntropyX";
        string value = entropy.ToString("F4", CultureInfo.InvariantCulture);
        string color = entropy < 0.5 ? "#22c55e" : entropy < 1.5 ? "#f59e0b" : "#ef4444";
        int labelWidth = 68;
        int valueWidth = Math.Max(50, value.Length * 7 + 10);
        int totalWidth = labelWidth + valueWidth;
        int lx = labelWidth / 2;
        int vx = labelWidth + valueWidth / 2;
        return $"""<svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="20"><rect width="{labelWidth}" height="20" rx="3" fill="#555"/><rect x="{labelWidth}" width="{valueWidth}" height="20" rx="3" fill="{color}"/><text x="{lx}" y="14" text-anchor="middle" fill="#fff" font-family="DejaVu Sans,Verdana,sans-serif" font-size="11">{EscapeHtml(label)}</text><text x="{vx}" y="14" text-anchor="middle" fill="#fff" font-family="DejaVu Sans,Verdana,sans-serif" font-size="11" font-weight="bold">{EscapeHtml(value)}</text></svg>""";
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static IReadOnlyList<T> Downsample<T>(IReadOnlyList<T> source, int maxPoints)
    {
        if (source.Count <= maxPoints) return source;
        var result = new List<T>(maxPoints);
        double step = (double)(source.Count - 1) / (maxPoints - 1);
        for (int i = 0; i < maxPoints; i++)
            result.Add(source[(int)Math.Round(i * step)]);
        return result;
    }
}
