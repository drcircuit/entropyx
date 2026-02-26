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
    double Badness);

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
                    lf.TryGetProperty("badness",               out var b)  ? b.GetDouble()       : 0));
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

public enum Verdict { Improving, Stable, Regressing, Critical }

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

    private static string ShortDate(string generated) =>
        generated[..Math.Min(10, generated.Length)];

    // ── Public API ─────────────────────────────────────────────────────────────

    public string GenerateHtml(DataJsonReport baseline, DataJsonReport current)
    {
        var assessment = BuildAssessment(baseline, current);
        var sb = new StringBuilder();
        AppendHtmlHeader(sb, baseline, current);
        AppendEntropyHero(sb, baseline, current);
        AppendAssessmentSection(sb, assessment);
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
            Verdict.Improving  => "green",
            Verdict.Stable     => "yellow",
            Verdict.Regressing => "red",
            Verdict.Critical   => "bold red",
            _ => "white"
        };

        AnsiConsole.MarkupLine($"[bold]Verdict:[/] [{verdictColor}]{Markup.Escape(assessment.VerdictLabel)}[/]");
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

        var observations = new List<string>();

        // Entropy trend within current snapshot
        double currentTrend = ComputeEntropyTrend(current.History);
        double baselineTrend = ComputeEntropyTrend(baseline.History);

        // Primary entropy observation
        if (Math.Abs(entropyDelta) < 0.001)
            observations.Add("EntropyX score is virtually unchanged between snapshots.");
        else if (entropyDelta < 0)
            observations.Add($"EntropyX score decreased by {Math.Abs(entropyDelta):F4} ({Math.Abs(relativeDelta):P1}), indicating improved code quality distribution.");
        else
            observations.Add($"EntropyX score increased by {entropyDelta:F4} ({relativeDelta:P1}), indicating growing code complexity.");

        // Internal trend of the current snapshot
        if (current.History.Count >= 3)
        {
            if (currentTrend > 0.001)
                observations.Add($"Current snapshot shows an upward entropy trend (slope ≈ {currentTrend:F4}/commit), suggesting ongoing complexity growth.");
            else if (currentTrend < -0.001)
                observations.Add($"Current snapshot shows a downward entropy trend (slope ≈ {currentTrend:F4}/commit), suggesting active refactoring.");
            else
                observations.Add("Entropy trend within the current snapshot is flat — complexity is stable.");
        }

        // SLOC growth relative to entropy
        int slocDelta = cSloc - bSloc;
        if (slocDelta > 0 && entropyDelta <= 0)
            observations.Add($"Codebase grew by {slocDelta:N0} SLOC while entropy improved — a positive signal of controlled growth.");
        else if (slocDelta > 0 && entropyDelta > 0)
            observations.Add($"Codebase grew by {slocDelta:N0} SLOC with a corresponding entropy increase — complexity is spreading.");
        else if (slocDelta < 0 && entropyDelta < 0)
            observations.Add($"Codebase shrank by {Math.Abs(slocDelta):N0} SLOC and entropy improved — likely effective dead code removal.");

        // File count change
        int filesDelta = cFiles - bFiles;
        if (filesDelta > 0)
            observations.Add($"{filesDelta} new file(s) added since baseline.");
        else if (filesDelta < 0)
            observations.Add($"{Math.Abs(filesDelta)} file(s) removed since baseline.");

        // Top worsened/improved files
        var worsenedFiles = GetWorsenedFiles(baseline, current);
        var improvedFiles = GetImprovedFiles(baseline, current);
        if (worsenedFiles.Count > 0)
            observations.Add($"{worsenedFiles.Count} file(s) show higher badness in the current snapshot (e.g. {worsenedFiles[0].Path}).");
        if (improvedFiles.Count > 0)
            observations.Add($"{improvedFiles.Count} file(s) show lower badness in the current snapshot (e.g. {improvedFiles[0].Path}).");

        // Determine verdict
        Verdict verdict;
        string verdictLabel;
        string summary;

        if (cEntropy >= 2.0 && entropyDelta > 0)
        {
            verdict = Verdict.Critical;
            verdictLabel = "🔴 Critical";
            summary = "EntropyX is high and still rising. Code disorder is at a critical level — immediate refactoring is strongly recommended.";
        }
        else if (entropyDelta > 0.1 || relativeDelta > 0.1)
        {
            verdict = Verdict.Regressing;
            verdictLabel = "🟠 Regressing";
            summary = "EntropyX score has grown noticeably. The codebase is accumulating disorder faster than it is being addressed.";
        }
        else if (entropyDelta < -0.05 || (entropyDelta < 0 && slocDelta >= 0))
        {
            verdict = Verdict.Improving;
            verdictLabel = "🟢 Improving";
            summary = "EntropyX score has decreased, indicating that code quality distribution is improving. Keep up the good work.";
        }
        else
        {
            verdict = Verdict.Stable;
            verdictLabel = "🟡 Stable";
            summary = "EntropyX score is largely stable between snapshots. The codebase is holding its current quality level.";
        }

        return new ComparisonAssessment(verdict, verdictLabel, summary, observations);
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
                .verdict-box { border-radius: 12px; padding: 1.5rem 2rem; border: 2px solid; margin-bottom: 1.5rem; }
                .verdict-improving  { border-color: var(--green);  background: rgba(34,197,94,.08); }
                .verdict-stable     { border-color: var(--yellow); background: rgba(245,158,11,.08); }
                .verdict-regressing { border-color: var(--red);    background: rgba(239,68,68,.08); }
                .verdict-critical   { border-color: #ff0033;       background: rgba(255,0,51,.12); }
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
            Verdict.Improving  => "verdict-improving",
            Verdict.Stable     => "verdict-stable",
            Verdict.Regressing => "verdict-regressing",
            Verdict.Critical   => "verdict-critical",
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
            <script>
            (function() {
              var bLabels = [{{bLabels}}];
              var bData   = [{{bData}}];
              var cLabels = [{{cLabels}}];
              var cData   = [{{cData}}];
              // Merge all labels by normalizing both series to index-based x
              var ctx = document.getElementById('trendChart').getContext('2d');
              new Chart(ctx, {
                type: 'line',
                data: {
                  labels: bLabels.length >= cLabels.length ? bLabels : cLabels,
                  datasets: [
                    {
                      label: 'Baseline',
                      data: bData.map((v,i) => ({x: i, y: parseFloat(v)})),
                      borderColor: '#60a5fa',
                      backgroundColor: 'rgba(96,165,250,.1)',
                      borderWidth: 2,
                      pointRadius: 0,
                      tension: 0.3,
                      parsing: false
                    },
                    {
                      label: 'Current',
                      data: cData.map((v,i) => ({x: i, y: parseFloat(v)})),
                      borderColor: '#f97316',
                      backgroundColor: 'rgba(249,115,22,.1)',
                      borderWidth: 2,
                      pointRadius: 0,
                      tension: 0.3,
                      parsing: false
                    }
                  ]
                },
                options: {
                  responsive: true, maintainAspectRatio: false,
                  plugins: { legend: { labels: { color: '#888' } } },
                  scales: {
                    x: { type: 'linear', ticks: { color: '#888' }, grid: { color: '#2d3044' } },
                    y: { ticks: { color: '#888' }, grid: { color: '#2d3044' } }
                  }
                }
              });
            })();
            </script>
            """);
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

        // Worsened files
        string worsenedMeta = worsened.Count == 0 ? "none" : $"{worsened.Count} file(s)";
        sb.AppendLine($"    <details open><summary>Files with Higher Badness (Worsened) <span class=\"summary-meta\">{EscapeHtml(worsenedMeta)}</span></summary>");
        sb.AppendLine("      <div class=\"details-body\">");
        if (worsened.Count == 0)
            sb.AppendLine("        <div style=\"color:var(--muted);font-style:italic\">No files worsened. ✓</div>");
        else
            AppendFilesDiffTable(sb, worsened, baseMap, isWorsened: true);
        sb.AppendLine("      </div></details>");

        // Improved files
        string improvedMeta = improved.Count == 0 ? "none" : $"{improved.Count} file(s)";
        sb.AppendLine($"    <details><summary>Files with Lower Badness (Improved) <span class=\"summary-meta\">{EscapeHtml(improvedMeta)}</span></summary>");
        sb.AppendLine("      <div class=\"details-body\">");
        if (improved.Count == 0)
            sb.AppendLine("        <div style=\"color:var(--muted);font-style:italic\">No files improved.</div>");
        else
            AppendFilesDiffTable(sb, improved, baseMap, isWorsened: false);
        sb.AppendLine("      </div></details>");

        // New files
        string newMeta = newFiles.Count == 0 ? "none" : $"{newFiles.Count} file(s)";
        sb.AppendLine($"    <details><summary>New Files <span class=\"summary-meta\">{EscapeHtml(newMeta)}</span></summary>");
        sb.AppendLine("      <div class=\"details-body\">");
        if (newFiles.Count == 0)
            sb.AppendLine("        <div style=\"color:var(--muted);font-style:italic\">No new files.</div>");
        else
            AppendFilesTable(sb, newFiles);
        sb.AppendLine("      </div></details>");

        // Removed files
        string removedMeta = removedFiles.Count == 0 ? "none" : $"{removedFiles.Count} file(s)";
        sb.AppendLine($"    <details><summary>Removed Files <span class=\"summary-meta\">{EscapeHtml(removedMeta)}</span></summary>");
        sb.AppendLine("      <div class=\"details-body\">");
        if (removedFiles.Count == 0)
            sb.AppendLine("        <div style=\"color:var(--muted);font-style:italic\">No removed files.</div>");
        else
            AppendFilesTable(sb, removedFiles);
        sb.AppendLine("      </div></details>");

        sb.AppendLine("</section>");
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
