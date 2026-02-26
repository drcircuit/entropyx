using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeEvo.Core;
using CodeEvo.Core.Models;

namespace CodeEvo.Reporting;

public class HtmlReporter
{
    private const int TopFilesCount = 10;
    private const int MaxChartPoints = 500;

    public string Generate(
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> history,
        IReadOnlyList<FileMetrics> latestFiles)
    {
        var ordered = history.OrderBy(h => h.Commit.Timestamp).ToList();
        var deltas = ComputeDeltas(ordered);
        var (troubled, heroic) = ClassifyCommits(deltas);

        var largeFiles = latestFiles.OrderByDescending(f => f.Sloc).Take(TopFilesCount).ToList();
        var complexFiles = latestFiles.OrderByDescending(f => f.CyclomaticComplexity).Take(TopFilesCount).ToList();
        var smellyFiles = latestFiles
            .OrderByDescending(f => f.SmellsHigh * 3 + f.SmellsMedium * 2 + f.SmellsLow)
            .Take(TopFilesCount).ToList();

        double[] badness = latestFiles.Count > 0 ? EntropyCalculator.ComputeBadness(latestFiles) : [];

        return BuildHtml(ordered, deltas, troubled, heroic, largeFiles, complexFiles, smellyFiles, latestFiles, badness);
    }

    public record CommitDelta(CommitInfo Commit, RepoMetrics Metrics, double Delta, double RelativeDelta, int SlocDelta = 0, int FilesDelta = 0);

    /// <summary>
    /// Generates a rich HTML drilldown report for a single commit showing per-language SLOC,
    /// per-file metrics, the commit's effect on the repo, notable events, and a health assessment.
    /// </summary>
    /// <param name="commit">The commit being inspected.</param>
    /// <param name="metrics">Repo-level metrics for this commit.</param>
    /// <param name="files">File-level metrics for this commit.</param>
    /// <param name="history">Full stored history (oldest first) used for context; may be empty.</param>
    /// <param name="previousMetrics">Metrics for the immediately preceding commit, if known.</param>
    public string GenerateDrilldown(
        CommitInfo commit,
        RepoMetrics metrics,
        IReadOnlyList<FileMetrics> files,
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> history,
        RepoMetrics? previousMetrics)
    {
        var ordered = history.OrderBy(h => h.Commit.Timestamp).ToList();
        var deltas = ComputeDeltas(ordered);
        var (troubled, heroic) = ClassifyCommits(deltas);
        double[] badness = files.Count > 0 ? EntropyCalculator.ComputeBadness(files) : [];

        var sb = new StringBuilder();
        var reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        AppendHtmlHeader(sb, reportDate);
        AppendDrilldownHeader(sb, commit, metrics, reportDate);
        AppendDrilldownSummaryStats(sb, metrics, previousMetrics);
        AppendDrilldownAssessment(sb, metrics, previousMetrics);
        AppendDrilldownLanguageChart(sb, files);
        AppendHeatmapSection(sb, files, badness);
        AppendDrilldownFileTable(sb, files, badness);
        AppendTroubledSection(sb, troubled);
        AppendHeroicSection(sb, heroic);
        AppendHtmlFooter(sb);

        return sb.ToString();
    }

    private static void AppendDrilldownHeader(StringBuilder sb, CommitInfo commit, RepoMetrics metrics, string reportDate)
    {
        var hash = commit.Hash[..Math.Min(8, commit.Hash.Length)];
        var date = commit.Timestamp != DateTimeOffset.MinValue
            ? commit.Timestamp.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
            : "unknown";
        string badge = EntropyBadgeSvg(metrics.EntropyScore);
        sb.AppendLine($$"""
              <header>
                <div>
                  <h1>⚡ EntropyX Drilldown</h1>
                  <div class="subtitle">Commit <code style="color:var(--accent)">{{EscapeHtml(hash)}}</code> &nbsp;·&nbsp; {{EscapeHtml(date)}} &nbsp;·&nbsp; Generated {{reportDate}}</div>
                </div>
                <div>{{badge}}</div>
              </header>
              <div class="container">
            """);
    }

    private static void AppendDrilldownSummaryStats(StringBuilder sb, RepoMetrics metrics, RepoMetrics? previous)
    {
        string entropy  = metrics.EntropyScore.ToString("F4", CultureInfo.InvariantCulture);
        string files    = metrics.TotalFiles.ToString(CultureInfo.InvariantCulture);
        string sloc     = metrics.TotalSloc.ToString("N0", CultureInfo.InvariantCulture);

        string deltaEntropy = previous is null ? "" : FormatDeltaHtml(metrics.EntropyScore - previous.EntropyScore);
        string deltaSloc    = previous is null ? "" : FormatDeltaHtml(metrics.TotalSloc - previous.TotalSloc, isInteger: true);
        string deltaFiles   = previous is null ? "" : FormatDeltaHtml(metrics.TotalFiles - previous.TotalFiles, isInteger: true);

        sb.AppendLine($$"""
                <section>
                  <div class="grid-3">
                    <div class="card stat">
                      <div class="value">{{entropy}}</div>
                      <div class="label">Entropy Score {{deltaEntropy}}</div>
                    </div>
                    <div class="card stat">
                      <div class="value">{{files}}</div>
                      <div class="label">Total Files {{deltaFiles}}</div>
                    </div>
                    <div class="card stat">
                      <div class="value">{{sloc}}</div>
                      <div class="label">Total SLOC {{deltaSloc}}</div>
                    </div>
                  </div>
                </section>
            """);
    }

    private static string FormatDeltaHtml(double delta, bool isInteger = false)
    {
        if (Math.Abs(delta) < 1e-9) return "";
        string sign = delta > 0 ? "+" : "";
        string val = isInteger
            ? $"{sign}{(int)delta:N0}"
            : $"{sign}{delta.ToString("F4", CultureInfo.InvariantCulture)}";
        string cls = delta > 0 ? "delta-pos" : "delta-neg";
        return $"<span class=\"{cls}\" style=\"font-size:0.65rem\">{EscapeHtml(val)}</span>";
    }

    private static void AppendDrilldownAssessment(StringBuilder sb, RepoMetrics metrics, RepoMetrics? previous)
    {
        double e = metrics.EntropyScore;
        var (grade, color, description) = e switch
        {
            < 0.3 => ("Excellent", "#22c55e", "Entropy is very low – the codebase is in excellent shape."),
            < 0.7 => ("Good",      "#86efac", "Entropy is low – only minor areas could be improved."),
            < 1.2 => ("Fair",      "#f59e0b", "Entropy is moderate – technical debt is accumulating."),
            < 2.0 => ("Poor",      "#f97316", "Entropy is high – significant refactoring is recommended."),
            _     => ("Critical",  "#ef4444", "Entropy is very high – immediate attention required.")
        };

        string trend = "";
        if (previous is not null)
        {
            double delta = metrics.EntropyScore - previous.EntropyScore;
            trend = delta switch
            {
                > 0.02  => $"<span class=\"delta-pos\">⬆ Worsening (Δ {delta:+F4})</span>",
                < -0.02 => $"<span class=\"delta-neg\">⬇ Improving (Δ {delta:F4})</span>",
                _       => "<span class=\"delta-zero\">→ Stable</span>"
            };
        }

        sb.AppendLine($$"""
                <section>
                  <div class="card">
                    <h2>📊 Assessment</h2>
                    <p style="font-size:1.1rem;margin-bottom:0.5rem">
                      Health Grade: <strong style="color:{{color}}">{{EscapeHtml(grade)}}</strong>
                      {{(trend.Length > 0 ? $"&nbsp;·&nbsp; {trend}" : "")}}
                    </p>
                    <p style="color:var(--muted)">{{EscapeHtml(description)}}</p>
                  </div>
                </section>
            """);
    }

    private static void AppendDrilldownLanguageChart(StringBuilder sb, IReadOnlyList<FileMetrics> files)
    {
        var byLang = files
            .Where(f => f.Language.Length > 0)
            .GroupBy(f => f.Language)
            .Select(g => (Language: g.Key, FileCount: g.Count(), TotalSloc: g.Sum(f => f.Sloc)))
            .OrderByDescending(x => x.TotalSloc)
            .ToList();

        if (byLang.Count == 0) return;

        int grandTotal = byLang.Sum(x => x.TotalSloc);
        var colors = new[] { "#7c6af7", "#22c55e", "#f59e0b", "#ef4444", "#3b82f6", "#ec4899", "#14b8a6", "#a855f7", "#f97316", "#64748b" };
        var chartLabels = string.Join(",", byLang.Select(x => JsonString(x.Language)));
        var chartData   = string.Join(",", byLang.Select(x => x.TotalSloc.ToString(CultureInfo.InvariantCulture)));
        var chartColors = string.Join(",", byLang.Select((_, i) => JsonString(colors[i % colors.Length])));

        sb.AppendLine($$"""
                <section>
                  <div class="grid-2">
                    <div class="chart-card">
                      <h2>🌐 SLOC by Language</h2>
                      <div class="chart-wrap"><canvas id="langChart"></canvas></div>
                    </div>
                    <div class="card">
                      <h2>📋 Language Breakdown</h2>
                      <table>
                        <thead><tr><th>Language</th><th style="text-align:right">Files</th><th style="text-align:right">SLOC</th><th style="text-align:right">Share</th></tr></thead>
                        <tbody>
            """);

        foreach (var (lang, count, sloc) in byLang)
        {
            double pct = grandTotal > 0 ? sloc * 100.0 / grandTotal : 0;
            sb.AppendLine($$"""
                          <tr>
                            <td>{{EscapeHtml(lang)}}</td>
                            <td style="text-align:right">{{count}}</td>
                            <td style="text-align:right">{{sloc.ToString("N0", CultureInfo.InvariantCulture)}}</td>
                            <td style="text-align:right">{{pct.ToString("F1", CultureInfo.InvariantCulture)}}%</td>
                          </tr>
                """);
        }

        sb.AppendLine($$"""
                        </tbody>
                      </table>
                    </div>
                  </div>
                </section>
                <script>
                (function() {
                  var ctx = document.getElementById('langChart').getContext('2d');
                  new Chart(ctx, {
                    type: 'doughnut',
                    data: {
                      labels: [{{chartLabels}}],
                      datasets: [{ data: [{{chartData}}], backgroundColor: [{{chartColors}}], borderWidth: 2, borderColor: '#0f1117' }]
                    },
                    options: {
                      responsive: true, maintainAspectRatio: false,
                      plugins: {
                        legend: { position: 'right', labels: { color: '#e0e0e0', font: { size: 12 } } },
                        tooltip: { callbacks: { label: function(c) { return c.label + ': ' + c.raw.toLocaleString() + ' SLOC'; } } }
                      }
                    }
                  });
                })();
                </script>
            """);
    }

    private static void AppendDrilldownFileTable(StringBuilder sb, IReadOnlyList<FileMetrics> files, double[] badness)
    {
        if (files.Count == 0) return;

        double maxBadness = badness.Length > 0 ? Math.Max(badness.Max(), double.Epsilon) : 1.0;

        var sorted = files.Zip(badness.Length > 0 ? badness : new double[files.Count])
            .OrderByDescending(x => x.Second)
            .ToList();

        sb.AppendLine($$"""
                <section>
                  <div class="card">
                    <h2>📁 File Metrics</h2>
                    <details open>
                      <summary>All {{files.Count}} file(s) sorted by badness <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
                        <table>
                          <thead><tr><th>File</th><th>Language</th><th style="text-align:right">SLOC</th><th style="text-align:right">CC</th><th style="text-align:right">MI</th><th style="text-align:right">Smells H/M/L</th><th style="text-align:right">Badness</th></tr></thead>
                          <tbody>
            """);

        foreach (var (file, b) in sorted)
        {
            float t = (float)(b / maxBadness);
            string color = HeatColorHtml(t);
            string slocBadge = SlocBadge(file.Sloc);
            sb.AppendLine($$"""
                            <tr>
                              <td>{{EscapeHtml(file.Path)}}</td>
                              <td>{{EscapeHtml(file.Language.Length > 0 ? file.Language : "—")}}</td>
                              <td style="text-align:right">{{file.Sloc.ToString("N0", CultureInfo.InvariantCulture)}} {{slocBadge}}</td>
                              <td style="text-align:right">{{file.CyclomaticComplexity.ToString("F1", CultureInfo.InvariantCulture)}} {{CcBadge(file.CyclomaticComplexity)}}</td>
                              <td style="text-align:right">{{file.MaintainabilityIndex.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                              <td style="text-align:right">{{file.SmellsHigh}}/{{file.SmellsMedium}}/{{file.SmellsLow}} {{SmellBadge(file.SmellsHigh, file.SmellsMedium, file.SmellsLow)}}</td>
                              <td style="text-align:right"><span style="color:{{color}}">{{b.ToString("F3", CultureInfo.InvariantCulture)}}</span></td>
                            </tr>
                """);
        }

        sb.AppendLine("          </tbody></table></div></details></div></section>");
    }

    public static IReadOnlyList<CommitDelta> ComputeDeltas(
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> ordered)
    {
        var result = new List<CommitDelta>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var (commit, metrics) = ordered[i];
            double prev = i == 0 ? metrics.EntropyScore : ordered[i - 1].Metrics.EntropyScore;
            double delta = metrics.EntropyScore - prev;
            double relativeDelta = prev == 0 ? 0 : delta / prev;
            int slocDelta = i == 0 ? 0 : metrics.TotalSloc - ordered[i - 1].Metrics.TotalSloc;
            int filesDelta = i == 0 ? 0 : metrics.TotalFiles - ordered[i - 1].Metrics.TotalFiles;
            result.Add(new CommitDelta(commit, metrics, delta, relativeDelta, slocDelta, filesDelta));
        }
        return result;
    }

    public static (IReadOnlyList<CommitDelta> Troubled, IReadOnlyList<CommitDelta> Heroic)
        ClassifyCommits(IReadOnlyList<CommitDelta> deltas)
    {
        if (deltas.Count < 2)
            return ([], []);

        // Use mean ± 1.5×stddev of all deltas to identify outliers (skip the first delta which is 0)
        var allDeltas = deltas.Skip(1).Select(d => d.Delta).ToList();
        double mean = allDeltas.Average();
        double stdDev = Math.Sqrt(allDeltas.Average(d => (d - mean) * (d - mean)));

        double troubledThreshold = Math.Max(0.02, mean + 1.5 * stdDev);
        double heroicThreshold = Math.Min(-0.02, mean - 1.5 * stdDev);

        var troubled = deltas.Skip(1)
            .Where(d => d.Delta >= troubledThreshold)
            .OrderByDescending(d => d.Delta)
            .ToList();
        var heroic = deltas.Skip(1)
            .Where(d => d.Delta <= heroicThreshold)
            .OrderBy(d => d.Delta)
            .ToList();

        return (troubled, heroic);
    }

    private static string BuildHtml(
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> ordered,
        IReadOnlyList<CommitDelta> deltas,
        IReadOnlyList<CommitDelta> troubled,
        IReadOnlyList<CommitDelta> heroic,
        IReadOnlyList<FileMetrics> largeFiles,
        IReadOnlyList<FileMetrics> complexFiles,
        IReadOnlyList<FileMetrics> smellyFiles,
        IReadOnlyList<FileMetrics> latestFiles,
        double[] badness)
    {
        var sb = new StringBuilder();
        var latest = ordered.Count > 0 ? ordered[^1].Metrics : null;
        var reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        AppendHtmlHeader(sb, reportDate);
        AppendSummarySection(sb, latest, ordered.Count, reportDate);
        AppendGaugesSection(sb, latest, latestFiles);
        AppendEntropyChart(sb, ordered);
        AppendGrowthChart(sb, ordered);
        AppendHeatmapSection(sb, latestFiles, badness);
        AppendIssuesSection(sb, largeFiles, complexFiles, smellyFiles);
        AppendTroubledSection(sb, troubled);
        AppendHeroicSection(sb, heroic);
        AppendCommitTableSection(sb, deltas);
        AppendHtmlFooter(sb);

        return sb.ToString();
    }

    private static void AppendHtmlHeader(StringBuilder sb, string reportDate)
    {
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>EntropyX Code Health Report</title>
              <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.umd.min.js"></script>
              <style>
                :root {
                  --bg: #0f1117; --card: #1a1d27; --border: #2d3044;
                  --text: #e0e0e0; --muted: #888; --accent: #7c6af7;
                  --green: #22c55e; --red: #ef4444; --yellow: #f59e0b;
                  --font: 'Segoe UI', system-ui, sans-serif;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body { background: var(--bg); color: var(--text); font-family: var(--font); font-size: 14px; line-height: 1.6; }
                a { color: var(--accent); text-decoration: none; }
                h1 { font-size: 2rem; font-weight: 700; }
                h2 { font-size: 1.3rem; font-weight: 600; color: var(--accent); margin-bottom: 1rem; }
                h3 { font-size: 1rem; font-weight: 600; color: var(--muted); margin-bottom: 0.5rem; text-transform: uppercase; letter-spacing: .05em; }
                header { padding: 2rem 2.5rem; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; }
                header .subtitle { color: var(--muted); font-size: 0.85rem; margin-top: 0.25rem; }
                .container { max-width: 1280px; margin: 0 auto; padding: 2rem 2.5rem; }
                .grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem; }
                .grid-3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1.5rem; }
                @media (max-width: 900px) { .grid-2, .grid-3 { grid-template-columns: 1fr; } }
                .card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 1.5rem; }
                .stat { text-align: center; }
                .stat .value { font-size: 2.5rem; font-weight: 700; color: var(--accent); }
                .stat .label { color: var(--muted); font-size: 0.85rem; text-transform: uppercase; letter-spacing: .06em; }
                .chart-card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 1.5rem; }
                .chart-wrap { position: relative; height: 260px; }
                .gauge-wrap { position: relative; height: 160px; }
                .gauge-label { text-align: center; color: var(--muted); font-size: 0.8rem; text-transform: uppercase; letter-spacing: .06em; margin-top: 0.5rem; }
                .gauge-value { text-align: center; font-size: 1.4rem; font-weight: 700; color: var(--accent); margin-top: -0.5rem; }
                table { width: 100%; border-collapse: collapse; font-size: 13px; }
                th { text-align: left; padding: 0.5rem 0.75rem; color: var(--muted); font-weight: 600; text-transform: uppercase; letter-spacing: .05em; border-bottom: 1px solid var(--border); }
                td { padding: 0.45rem 0.75rem; border-bottom: 1px solid var(--border); word-break: break-all; }
                tr:last-child td { border-bottom: none; }
                tr:hover td { background: rgba(255,255,255,.03); }
                .badge { display: inline-block; padding: 0.15rem 0.55rem; border-radius: 999px; font-size: 11px; font-weight: 600; }
                .badge-red { background: rgba(239,68,68,.15); color: var(--red); }
                .badge-green { background: rgba(34,197,94,.15); color: var(--green); }
                .badge-yellow { background: rgba(245,158,11,.15); color: var(--yellow); }
                .badge-gray { background: rgba(136,136,136,.15); color: var(--muted); }
                .delta-pos { color: var(--red); }
                .delta-neg { color: var(--green); }
                .delta-zero { color: var(--muted); }
                section { margin-bottom: 2.5rem; }
                .empty-msg { color: var(--muted); font-style: italic; padding: 1rem 0; }
                .commit-hash { font-family: monospace; font-size: 12px; color: var(--muted); }
                .section-title { font-size: 1.3rem; font-weight: 600; color: var(--accent); margin-bottom: 1rem; padding-bottom: 0.5rem; border-bottom: 1px solid var(--border); }
                footer { padding: 1.5rem 2.5rem; border-top: 1px solid var(--border); color: var(--muted); font-size: 12px; text-align: center; }
                /* Accordion */
                details { margin-bottom: 1rem; }
                details > summary { cursor: pointer; list-style: none; display: flex; align-items: center; gap: 0.5rem; padding: 0.6rem 0.75rem; background: rgba(124,106,247,.08); border: 1px solid var(--border); border-radius: 8px; color: var(--accent); font-weight: 600; font-size: 0.9rem; user-select: none; }
                details > summary::-webkit-details-marker { display: none; }
                details > summary::before { content: '▶'; font-size: 0.7rem; transition: transform .2s; }
                details[open] > summary::before { transform: rotate(90deg); }
                details > summary .summary-meta { color: var(--muted); font-weight: 400; font-size: 0.8rem; margin-left: auto; }
                details .details-body { padding: 1rem 0; }
                /* Heatmap */
                .heatmap-row { display: flex; align-items: center; margin-bottom: 3px; font-size: 12px; }
                .heatmap-swatch { width: 56px; min-width: 56px; height: 22px; border-radius: 3px; display: flex; align-items: center; justify-content: center; font-size: 10px; font-weight: 600; color: rgba(0,0,0,.75); flex-shrink: 0; }
                .heatmap-label { flex: 1; padding: 0 0.5rem; color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: monospace; }
                .heatmap-bar-wrap { width: 120px; min-width: 120px; background: rgba(255,255,255,.05); border-radius: 3px; height: 10px; overflow: hidden; }
                .heatmap-bar { height: 100%; border-radius: 3px; }
              </style>
            </head>
            <body>
            """);
    }

    private static void AppendSummarySection(StringBuilder sb, RepoMetrics? latest, int commitCount, string reportDate)
    {
        string entropy = latest is not null ? latest.EntropyScore.ToString("F4", CultureInfo.InvariantCulture) : "—";
        string files = latest is not null ? latest.TotalFiles.ToString(CultureInfo.InvariantCulture) : "—";
        string sloc = latest is not null ? latest.TotalSloc.ToString("N0", CultureInfo.InvariantCulture) : "—";
        string badge = latest is not null ? EntropyBadgeSvg(latest.EntropyScore) : "";

        sb.AppendLine($$"""
              <header>
                <div>
                  <h1>⚡ EntropyX Report</h1>
                  <div class="subtitle">Generated {{reportDate}} &nbsp;·&nbsp; {{commitCount}} commit(s) analysed</div>
                </div>
                <div>{{badge}}</div>
              </header>
              <div class="container">
                <section>
                  <div class="grid-3">
                    <div class="card stat"><div class="value">{{entropy}}</div><div class="label">Current Entropy</div></div>
                    <div class="card stat"><div class="value">{{files}}</div><div class="label">Total Files</div></div>
                    <div class="card stat"><div class="value">{{sloc}}</div><div class="label">Total SLOC</div></div>
                  </div>
                </section>
            """);
    }

    private static void AppendEntropyChart(StringBuilder sb,
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> ordered)
    {
        var chartData = Downsample(ordered, MaxChartPoints);
        var labels = chartData
            .Select(h => JsonString(h.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToArray();
        var values = chartData
            .Select(h => h.Metrics.EntropyScore.ToString("F6", CultureInfo.InvariantCulture))
            .ToArray();

        sb.AppendLine($$"""
                <section>
                  <div class="chart-card">
                    <h2>Entropy Over Time</h2>
                    <div class="chart-wrap"><canvas id="entropyChart"></canvas></div>
                  </div>
                </section>
            """);

        sb.AppendLine($$"""
                <script>
                (function() {
                  var ctx = document.getElementById('entropyChart').getContext('2d');
                  var gradient = ctx.createLinearGradient(0, 0, 0, 260);
                  gradient.addColorStop(0, 'rgba(124,106,247,0.4)');
                  gradient.addColorStop(1, 'rgba(124,106,247,0.02)');
                  new Chart(ctx, {
                    type: 'line',
                    data: {
                      labels: [{{string.Join(",", labels)}}],
                      datasets: [{
                        label: 'Entropy Score',
                        data: [{{string.Join(",", values)}}],
                        borderColor: '#7c6af7',
                        backgroundColor: gradient,
                        borderWidth: 2,
                        pointRadius: {{(chartData.Count > 100 ? 0 : 3)}},
                        pointHoverRadius: 5,
                        fill: true,
                        tension: 0.3
                      }]
                    },
                    options: {
                      responsive: true, maintainAspectRatio: false,
                      plugins: { legend: { labels: { color: '#888' } } },
                      scales: {
                        x: { ticks: { color: '#888', maxTicksLimit: 12 }, grid: { color: '#2d3044' } },
                        y: { ticks: { color: '#888' }, grid: { color: '#2d3044' }, beginAtZero: true }
                      }
                    }
                  });
                })();
                </script>
            """);
    }

    private static void AppendGrowthChart(StringBuilder sb,
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> ordered)
    {
        var chartData = Downsample(ordered, MaxChartPoints);
        var labels = chartData
            .Select(h => JsonString(h.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToArray();
        var slocValues = chartData
            .Select(h => h.Metrics.TotalSloc.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        var fileValues = chartData
            .Select(h => h.Metrics.TotalFiles.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        sb.AppendLine($$"""
                <section>
                  <div class="grid-2">
                    <div class="chart-card">
                      <h2>SLOC Over Time</h2>
                      <div class="chart-wrap"><canvas id="slocChart"></canvas></div>
                    </div>
                    <div class="chart-card">
                      <h2>File Count Over Time</h2>
                      <div class="chart-wrap"><canvas id="filesChart"></canvas></div>
                    </div>
                  </div>
                </section>
            """);

        sb.AppendLine($$"""
                <script>
                (function() {
                  function mkChart(id, label, data, color) {
                    var ctx = document.getElementById(id).getContext('2d');
                    var g = ctx.createLinearGradient(0, 0, 0, 260);
                    g.addColorStop(0, color.replace('1)', '0.35)'));
                    g.addColorStop(1, color.replace('1)', '0.02)'));
                    new Chart(ctx, {
                      type: 'line',
                      data: {
                        labels: [{{string.Join(",", labels)}}],
                        datasets: [{ label: label, data: data, borderColor: color.replace('1)', '1)').replace('rgba', 'rgb'), backgroundColor: g, borderWidth: 2, pointRadius: {{(chartData.Count > 100 ? 0 : 3)}}, fill: true, tension: 0.3 }]
                      },
                      options: {
                        responsive: true, maintainAspectRatio: false,
                        plugins: { legend: { labels: { color: '#888' } } },
                        scales: {
                          x: { ticks: { color: '#888', maxTicksLimit: 12 }, grid: { color: '#2d3044' } },
                          y: { ticks: { color: '#888' }, grid: { color: '#2d3044' }, beginAtZero: true }
                        }
                      }
                    });
                  }
                  mkChart('slocChart', 'SLOC', [{{string.Join(",", slocValues)}}], 'rgba(34,197,94,1)');
                  mkChart('filesChart', 'Files', [{{string.Join(",", fileValues)}}], 'rgba(245,158,11,1)');
                })();
                </script>
            """);
    }

    private static void AppendIssuesSection(StringBuilder sb,
        IReadOnlyList<FileMetrics> largeFiles,
        IReadOnlyList<FileMetrics> complexFiles,
        IReadOnlyList<FileMetrics> smellyFiles)
    {
        sb.AppendLine("""
                <section>
                  <div class="section-title">⚠ Issues (Latest Commit)</div>
            """);

        // Large files
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>🗂 Large Files</h2>
                    <details open>
                      <summary>Top files by SLOC <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        AppendFileTable(sb, largeFiles, "SLOC", f => f.Sloc.ToString(CultureInfo.InvariantCulture),
            f => SlocBadge(f.Sloc));
        sb.AppendLine("      </div></details></div>");

        // High complexity
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>🔀 High Complexity Areas</h2>
                    <details open>
                      <summary>Top files by Cyclomatic Complexity <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        AppendFileTable(sb, complexFiles, "Avg CC", f => f.CyclomaticComplexity.ToString("F1", CultureInfo.InvariantCulture),
            f => CcBadge(f.CyclomaticComplexity));
        sb.AppendLine("      </div></details></div>");

        // Smelly
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>🦨 Smelly Areas</h2>
                    <details open>
                      <summary>Top files by Code Smell score <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        AppendFileTable(sb, smellyFiles, "Smells H/M/L", f => $"{f.SmellsHigh}/{f.SmellsMedium}/{f.SmellsLow}",
            f => SmellBadge(f.SmellsHigh, f.SmellsMedium, f.SmellsLow));
        sb.AppendLine("      </div></details></div>");

        sb.AppendLine("</section>");
    }

    private static void AppendFileTable(StringBuilder sb, IReadOnlyList<FileMetrics> files,
        string metricHeader, Func<FileMetrics, string> metricValue, Func<FileMetrics, string> badge)
    {
        if (files.Count == 0 || files.All(f => f.Sloc == 0 && f.CyclomaticComplexity == 0))
        {
            sb.AppendLine("""    <p class="empty-msg">No data available.</p>""");
            return;
        }
        sb.AppendLine($$"""
                    <table>
                      <thead><tr><th>File</th><th>Language</th><th>{{metricHeader}}</th><th>MI</th><th>Status</th></tr></thead>
                      <tbody>
            """);
        foreach (var f in files)
        {
            sb.AppendLine($$"""
                        <tr>
                          <td>{{EscapeHtml(f.Path)}}</td>
                          <td>{{EscapeHtml(f.Language)}}</td>
                          <td>{{metricValue(f)}}</td>
                          <td>{{f.MaintainabilityIndex.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                          <td>{{badge(f)}}</td>
                        </tr>
                """);
        }
        sb.AppendLine("      </tbody></table>");
    }

    private static void AppendTroubledSection(StringBuilder sb, IReadOnlyList<CommitDelta> troubled)
    {
        sb.AppendLine("""
                <section>
                  <div class="card">
                    <h2>😈 Troubled Commits</h2>
                    <p style="color:var(--muted);margin-bottom:1rem;font-size:13px">Commits that significantly worsened codebase entropy.</p>
            """);
        if (troubled.Count == 0)
        {
            sb.AppendLine("""    <p class="empty-msg">No troubled commits detected.</p>""");
        }
        else
        {
            AppendDeltaTable(sb, troubled, isHeroic: false);
        }
        sb.AppendLine("  </div></section>");
    }

    private static void AppendHeroicSection(StringBuilder sb, IReadOnlyList<CommitDelta> heroic)
    {
        sb.AppendLine("""
                <section>
                  <div class="card">
                    <h2>🦸 Heroic Commits</h2>
                    <p style="color:var(--muted);margin-bottom:1rem;font-size:13px">Commits that significantly improved codebase entropy.</p>
            """);
        if (heroic.Count == 0)
        {
            sb.AppendLine("""    <p class="empty-msg">No heroic commits detected.</p>""");
        }
        else
        {
            AppendDeltaTable(sb, heroic, isHeroic: true);
        }
        sb.AppendLine("  </div></section>");
    }

    private static void AppendDeltaTable(StringBuilder sb, IReadOnlyList<CommitDelta> deltas, bool isHeroic)
    {
        sb.AppendLine("""
                    <table>
                      <thead><tr><th>Commit</th><th>Date</th><th>Entropy</th><th>Δ Entropy</th><th>Δ%</th><th>Δ SLOC</th><th>Δ Files</th></tr></thead>
                      <tbody>
            """);
        foreach (var d in deltas)
        {
            var hash = d.Commit.Hash[..Math.Min(8, d.Commit.Hash.Length)];
            var date = d.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var entropy = d.Metrics.EntropyScore.ToString("F4", CultureInfo.InvariantCulture);
            var deltaStr = FormatDelta(d.Delta);
            var relStr = FormatDelta(d.RelativeDelta * 100, suffix: "%");
            var slocDeltaStr = d.SlocDelta == 0 ? "<span class=\"delta-zero\">0</span>"
                : d.SlocDelta > 0 ? $"<span class=\"delta-pos\">+{d.SlocDelta.ToString("N0", CultureInfo.InvariantCulture)}</span>"
                : $"<span class=\"delta-neg\">{d.SlocDelta.ToString("N0", CultureInfo.InvariantCulture)}</span>";
            var filesDeltaStr = d.FilesDelta == 0 ? "<span class=\"delta-zero\">0</span>"
                : d.FilesDelta > 0 ? $"<span class=\"delta-pos\">+{d.FilesDelta}</span>"
                : $"<span class=\"delta-neg\">{d.FilesDelta}</span>";
            var cls = isHeroic ? "delta-neg" : "delta-pos";
            sb.AppendLine($$"""
                        <tr>
                          <td class="commit-hash">{{EscapeHtml(hash)}}</td>
                          <td>{{date}}</td>
                          <td>{{entropy}}</td>
                          <td class="{{cls}}">{{deltaStr}}</td>
                          <td class="{{cls}}">{{relStr}}</td>
                          <td>{{slocDeltaStr}}</td>
                          <td>{{filesDeltaStr}}</td>
                        </tr>
                """);
        }
        sb.AppendLine("      </tbody></table>");
    }

    private static void AppendCommitTableSection(StringBuilder sb, IReadOnlyList<CommitDelta> deltas)
    {
        sb.AppendLine($$"""
                <section>
                  <div class="card">
                    <h2>📋 Commit History</h2>
                    <details>
                      <summary>All {{deltas.Count}} commit(s) <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
                    <table>
                      <thead><tr><th>Commit</th><th>Date</th><th>Files</th><th>SLOC</th><th>Entropy</th><th>Δ Entropy</th><th>Δ SLOC</th></tr></thead>
                      <tbody>
            """);
        foreach (var d in deltas.AsEnumerable().Reverse())
        {
            var hash = d.Commit.Hash[..Math.Min(8, d.Commit.Hash.Length)];
            var date = d.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var entropy = d.Metrics.EntropyScore.ToString("F4", CultureInfo.InvariantCulture);
            var deltaStr = FormatDelta(d.Delta);
            string cls = d.Delta > 0 ? "delta-pos" : d.Delta < 0 ? "delta-neg" : "delta-zero";
            var slocDeltaStr = d.SlocDelta == 0 ? "0"
                : d.SlocDelta > 0 ? $"+{d.SlocDelta.ToString("N0", CultureInfo.InvariantCulture)}"
                : d.SlocDelta.ToString("N0", CultureInfo.InvariantCulture);
            string slocCls = d.SlocDelta > 0 ? "delta-pos" : d.SlocDelta < 0 ? "delta-neg" : "delta-zero";
            sb.AppendLine($$"""
                        <tr>
                          <td class="commit-hash">{{EscapeHtml(hash)}}</td>
                          <td>{{date}}</td>
                          <td>{{d.Metrics.TotalFiles}}</td>
                          <td>{{d.Metrics.TotalSloc.ToString("N0", CultureInfo.InvariantCulture)}}</td>
                          <td>{{entropy}}</td>
                          <td class="{{cls}}">{{deltaStr}}</td>
                          <td class="{{slocCls}}">{{slocDeltaStr}}</td>
                        </tr>
                """);
        }
        sb.AppendLine("      </tbody></table></div></details></div></section>");
    }

    private static void AppendHtmlFooter(StringBuilder sb)
    {
        sb.AppendLine("""
              </div>
              <footer>Generated by <strong>EntropyX</strong> — <a href="https://github.com/drcircuit/entropyx">github.com/drcircuit/entropyx</a></footer>
            </body>
            </html>
            """);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void AppendGaugesSection(StringBuilder sb, RepoMetrics? latest, IReadOnlyList<FileMetrics> files)
    {
        if (latest is null) return;

        double entropy = latest.EntropyScore;
        // Normalize entropy to 0-100 on a 0-3 scale (above 3 clamped at 100)
        double entropyPct = Math.Min(100.0, entropy / 3.0 * 100.0);
        double entropyRemain = 100.0 - entropyPct;

        double avgCc = files.Count > 0 ? files.Average(f => f.CyclomaticComplexity) : 0;
        // Normalize avg CC: 0-20 scale → 0-100%
        double ccPct = Math.Min(100.0, avgCc / 20.0 * 100.0);
        double ccRemain = 100.0 - ccPct;

        double avgSmells = files.Count > 0 ? files.Average(f => f.SmellsHigh * 3.0 + f.SmellsMedium * 2.0 + f.SmellsLow) : 0;
        // Normalize avg smells: 0-15 scale → 0-100%
        double smellsPct = Math.Min(100.0, avgSmells / 15.0 * 100.0);
        double smellsRemain = 100.0 - smellsPct;

        string entropyColor = entropyPct < 33 ? "#22c55e" : entropyPct < 66 ? "#f59e0b" : "#ef4444";
        string ccColor = ccPct < 33 ? "#22c55e" : ccPct < 66 ? "#f59e0b" : "#ef4444";
        string smellsColor = smellsPct < 33 ? "#22c55e" : smellsPct < 66 ? "#f59e0b" : "#ef4444";

        sb.AppendLine($$"""
                <section>
                  <div class="grid-3">
                    <div class="chart-card">
                      <h2>Entropy Health</h2>
                      <div class="gauge-wrap"><canvas id="gaugeEntropy"></canvas></div>
                      <div class="gauge-value" style="color:{{entropyColor}}">{{entropy.ToString("F4", CultureInfo.InvariantCulture)}}</div>
                      <div class="gauge-label">EntropyX Score</div>
                    </div>
                    <div class="chart-card">
                      <h2>Complexity Health</h2>
                      <div class="gauge-wrap"><canvas id="gaugeCc"></canvas></div>
                      <div class="gauge-value" style="color:{{ccColor}}">{{avgCc.ToString("F2", CultureInfo.InvariantCulture)}}</div>
                      <div class="gauge-label">Avg Cyclomatic Complexity</div>
                    </div>
                    <div class="chart-card">
                      <h2>Smell Health</h2>
                      <div class="gauge-wrap"><canvas id="gaugeSmells"></canvas></div>
                      <div class="gauge-value" style="color:{{smellsColor}}">{{avgSmells.ToString("F1", CultureInfo.InvariantCulture)}}</div>
                      <div class="gauge-label">Avg Weighted Smell Score</div>
                    </div>
                  </div>
                </section>
                <script>
                (function() {
                  function mkGauge(id, value, remaining, color) {
                    var ctx = document.getElementById(id).getContext('2d');
                    new Chart(ctx, {
                      type: 'doughnut',
                      data: {
                        datasets: [{
                          data: [value, remaining],
                          backgroundColor: [color, 'rgba(255,255,255,0.07)'],
                          borderWidth: 0,
                          circumference: 180,
                          rotation: 270
                        }]
                      },
                      options: {
                        cutout: '72%',
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: { legend: { display: false }, tooltip: { enabled: false } }
                      }
                    });
                  }
                  mkGauge('gaugeEntropy',  {{entropyPct.ToString("F2", CultureInfo.InvariantCulture)}}, {{entropyRemain.ToString("F2", CultureInfo.InvariantCulture)}}, '{{entropyColor}}');
                  mkGauge('gaugeCc',       {{ccPct.ToString("F2", CultureInfo.InvariantCulture)}}, {{ccRemain.ToString("F2", CultureInfo.InvariantCulture)}}, '{{ccColor}}');
                  mkGauge('gaugeSmells',   {{smellsPct.ToString("F2", CultureInfo.InvariantCulture)}}, {{smellsRemain.ToString("F2", CultureInfo.InvariantCulture)}}, '{{smellsColor}}');
                })();
                </script>
            """);
    }

    private static void AppendHeatmapSection(StringBuilder sb, IReadOnlyList<FileMetrics> files, double[] badness)
    {
        if (files.Count == 0) return;

        double maxBadness = badness.Length > 0 ? badness.Max() : 1.0;
        if (maxBadness == 0) maxBadness = 1.0;

        var sorted = files.Zip(badness)
            .OrderByDescending(x => x.Second)
            .ToList();

        sb.AppendLine("""
                <section>
                  <div class="card">
                    <h2>🌡 Complexity Heatmap (Latest Commit)</h2>
                    <details open>
                      <summary>Files sorted by badness score <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);

        foreach (var (file, b) in sorted)
        {
            float t = (float)(b / maxBadness);
            string color = HeatColorHtml(t);
            double barPct = b / maxBadness * 100.0;
            string score = b.ToString("F3", CultureInfo.InvariantCulture);
            string path = EscapeHtml(file.Path);
            sb.AppendLine($$"""
                        <div class="heatmap-row">
                          <div class="heatmap-swatch" style="background:{{color}}">{{score}}</div>
                          <div class="heatmap-label" title="{{path}}">{{path}}</div>
                          <div class="heatmap-bar-wrap"><div class="heatmap-bar" style="width:{{barPct.ToString("F1", CultureInfo.InvariantCulture)}}%;background:{{color}}"></div></div>
                        </div>
                """);
        }

        sb.AppendLine("      </div></details></div></section>");
    }

    /// <summary>
    /// Serializes an ad hoc (non-git) scan as a snapshot JSON string.
    /// The result can be compared with other snapshots to track changes over time.
    /// </summary>
    public static string GenerateDataJson(IReadOnlyList<FileMetrics> files)
    {
        var snapshotHash = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var entropy = EntropyCalculator.ComputeEntropy(files);
        var repoMetrics = new RepoMetrics(snapshotHash, files.Count, files.Sum(f => f.Sloc), entropy);
        var commitInfo = new CommitInfo(snapshotHash, DateTimeOffset.UtcNow, []);
        return GenerateDataJson([(commitInfo, repoMetrics)], files);
    }

    /// <summary>
    /// Serializes the report data to a JSON string that can be saved alongside the HTML report.
    /// The JSON can be used to compare two data points and generate a diff report.
    /// </summary>
    public static string GenerateDataJson(
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> history,
        IReadOnlyList<FileMetrics> latestFiles)
    {
        var ordered = history.OrderBy(h => h.Commit.Timestamp).ToList();
        var latest = ordered.Count > 0 ? ordered[^1].Metrics : null;
        double[] badness = latestFiles.Count > 0 ? EntropyCalculator.ComputeBadness(latestFiles) : [];

        var data = new
        {
            generated = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            commitCount = ordered.Count,
            summary = latest is null ? null : (object)new
            {
                entropy = latest.EntropyScore,
                files = latest.TotalFiles,
                sloc = latest.TotalSloc
            },
            history = ordered.Select(h => new
            {
                hash = h.Commit.Hash,
                date = h.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                entropy = h.Metrics.EntropyScore,
                files = h.Metrics.TotalFiles,
                sloc = h.Metrics.TotalSloc
            }).ToList(),
            latestFiles = latestFiles.Zip(badness.Length > 0 ? badness : new double[latestFiles.Count])
                .Select(x => new
                {
                    path = x.First.Path,
                    language = x.First.Language,
                    sloc = x.First.Sloc,
                    cyclomaticComplexity = x.First.CyclomaticComplexity,
                    maintainabilityIndex = x.First.MaintainabilityIndex,
                    smellsHigh = x.First.SmellsHigh,
                    smellsMedium = x.First.SmellsMedium,
                    smellsLow = x.First.SmellsLow,
                    badness = x.Second,
                    kind = x.First.Kind.ToString()
                }).ToList()
        };

        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

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

    /// <summary>
    /// Converts a normalized heat value t ∈ [0,1] to a CSS rgb() colour using
    /// the same IR thermal palette key-stops as <see cref="HeatmapImageGenerator"/>.
    /// </summary>
    private static string HeatColorHtml(float t)
    {
        // IR palette key-stops: t → (r, g, b)
        ReadOnlySpan<(float t, byte r, byte g, byte b)> palette =
        [
            (0.00f,   0,   0,   0),
            (0.12f,  80,   0, 130),
            (0.25f,   0,   0, 200),
            (0.38f,   0, 200, 200),
            (0.50f,   0, 180,   0),
            (0.62f, 220, 220,   0),
            (0.75f, 255, 140,   0),
            (0.88f, 220,   0,   0),
            (1.00f, 255, 255, 255),
        ];
        t = Math.Clamp(t, 0f, 1f);
        for (int i = 1; i < palette.Length; i++)
        {
            var (t0, r0, g0, b0) = palette[i - 1];
            var (t1, r1, g1, b1) = palette[i];
            if (t > t1) continue;
            float a = (t - t0) / (t1 - t0);
            byte r = (byte)(r0 + (r1 - r0) * a);
            byte g = (byte)(g0 + (g1 - g0) * a);
            byte b = (byte)(b0 + (b1 - b0) * a);
            return $"rgb({r},{g},{b})";
        }
        var last = palette[^1];
        return $"rgb({last.r},{last.g},{last.b})";
    }

    // ── original helpers ───────────────────────────────────────────────────────

    private static string FormatDelta(double value, string suffix = "")
    {
        if (Math.Abs(value) < 1e-9) return $"0{suffix}";
        string sign = value > 0 ? "+" : "";
        return $"{sign}{value.ToString("F4", CultureInfo.InvariantCulture)}{suffix}";
    }

    private static string SlocBadge(int sloc) => sloc switch
    {
        > 500 => """<span class="badge badge-red">Very Large</span>""",
        > 200 => """<span class="badge badge-yellow">Large</span>""",
        > 100 => """<span class="badge badge-gray">Medium</span>""",
        _ => """<span class="badge badge-green">Small</span>"""
    };

    private static string CcBadge(double cc) => cc switch
    {
        > 20 => """<span class="badge badge-red">High Risk</span>""",
        > 10 => """<span class="badge badge-yellow">Moderate</span>""",
        > 5 => """<span class="badge badge-gray">Low</span>""",
        _ => """<span class="badge badge-green">Good</span>"""
    };

    private static string SmellBadge(int high, int med, int low)
    {
        int score = high * 3 + med * 2 + low;
        return score switch
        {
            > 15 => """<span class="badge badge-red">Critical</span>""",
            > 7 => """<span class="badge badge-yellow">Warning</span>""",
            > 2 => """<span class="badge badge-gray">Minor</span>""",
            _ => """<span class="badge badge-green">Clean</span>"""
        };
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string JsonString(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    // Downsample a list to at most maxPoints entries using uniform stride selection,
    // always keeping the first and last entry so chart extremes are preserved.
    private static IReadOnlyList<T> Downsample<T>(IReadOnlyList<T> source, int maxPoints)
    {
        if (source.Count <= maxPoints)
            return source;
        var result = new List<T>(maxPoints);
        double step = (double)(source.Count - 1) / (maxPoints - 1);
        for (int i = 0; i < maxPoints; i++)
            result.Add(source[(int)Math.Round(i * step)]);
        return result;
    }
}
