using System.Globalization;
using System.Text;
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

        return BuildHtml(ordered, deltas, troubled, heroic, largeFiles, complexFiles, smellyFiles);
    }

    public record CommitDelta(CommitInfo Commit, RepoMetrics Metrics, double Delta, double RelativeDelta);

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
            result.Add(new CommitDelta(commit, metrics, delta, relativeDelta));
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
        IReadOnlyList<FileMetrics> smellyFiles)
    {
        var sb = new StringBuilder();
        var latest = ordered.Count > 0 ? ordered[^1].Metrics : null;
        var reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        AppendHtmlHeader(sb, reportDate);
        AppendSummarySection(sb, latest, ordered.Count, reportDate);
        AppendEntropyChart(sb, ordered);
        AppendGrowthChart(sb, ordered);
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

        sb.AppendLine($$"""
              <header>
                <div>
                  <h1>⚡ EntropyX Report</h1>
                  <div class="subtitle">Generated {{reportDate}} &nbsp;·&nbsp; {{commitCount}} commit(s) analysed</div>
                </div>
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
            """);
        AppendFileTable(sb, largeFiles, "SLOC", f => f.Sloc.ToString(CultureInfo.InvariantCulture),
            f => SlocBadge(f.Sloc));
        sb.AppendLine("  </div>");

        // High complexity
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>🔀 High Complexity Areas</h2>
            """);
        AppendFileTable(sb, complexFiles, "Avg CC", f => f.CyclomaticComplexity.ToString("F1", CultureInfo.InvariantCulture),
            f => CcBadge(f.CyclomaticComplexity));
        sb.AppendLine("  </div>");

        // Smelly
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>🦨 Smelly Areas</h2>
            """);
        AppendFileTable(sb, smellyFiles, "Smells H/M/L", f => $"{f.SmellsHigh}/{f.SmellsMedium}/{f.SmellsLow}",
            f => SmellBadge(f.SmellsHigh, f.SmellsMedium, f.SmellsLow));
        sb.AppendLine("  </div>");

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
                      <thead><tr><th>Commit</th><th>Date</th><th>Entropy</th><th>Delta</th><th>Δ%</th></tr></thead>
                      <tbody>
            """);
        foreach (var d in deltas)
        {
            var hash = d.Commit.Hash[..Math.Min(8, d.Commit.Hash.Length)];
            var date = d.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var entropy = d.Metrics.EntropyScore.ToString("F4", CultureInfo.InvariantCulture);
            var deltaStr = FormatDelta(d.Delta);
            var relStr = FormatDelta(d.RelativeDelta * 100, suffix: "%");
            var cls = isHeroic ? "delta-neg" : "delta-pos";
            sb.AppendLine($$"""
                        <tr>
                          <td class="commit-hash">{{EscapeHtml(hash)}}</td>
                          <td>{{date}}</td>
                          <td>{{entropy}}</td>
                          <td class="{{cls}}">{{deltaStr}}</td>
                          <td class="{{cls}}">{{relStr}}</td>
                        </tr>
                """);
        }
        sb.AppendLine("      </tbody></table>");
    }

    private static void AppendCommitTableSection(StringBuilder sb, IReadOnlyList<CommitDelta> deltas)
    {
        sb.AppendLine("""
                <section>
                  <div class="card">
                    <h2>📋 Commit History</h2>
                    <table>
                      <thead><tr><th>Commit</th><th>Date</th><th>Files</th><th>SLOC</th><th>Entropy</th><th>Delta</th></tr></thead>
                      <tbody>
            """);
        foreach (var d in deltas.AsEnumerable().Reverse())
        {
            var hash = d.Commit.Hash[..Math.Min(8, d.Commit.Hash.Length)];
            var date = d.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var entropy = d.Metrics.EntropyScore.ToString("F4", CultureInfo.InvariantCulture);
            var deltaStr = FormatDelta(d.Delta);
            string cls = d.Delta > 0 ? "delta-pos" : d.Delta < 0 ? "delta-neg" : "delta-zero";
            sb.AppendLine($$"""
                        <tr>
                          <td class="commit-hash">{{EscapeHtml(hash)}}</td>
                          <td>{{date}}</td>
                          <td>{{d.Metrics.TotalFiles}}</td>
                          <td>{{d.Metrics.TotalSloc.ToString("N0", CultureInfo.InvariantCulture)}}</td>
                          <td>{{entropy}}</td>
                          <td class="{{cls}}">{{deltaStr}}</td>
                        </tr>
                """);
        }
        sb.AppendLine("      </tbody></table></div></section>");
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
