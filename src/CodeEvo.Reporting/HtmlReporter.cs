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

    /// <summary>Per-commit aggregate file-level statistics used for CC/Smell over-time charts.</summary>
    public record CommitFileStats(CommitInfo Commit, double AvgCc, double AvgSmell, double SlocPerFile);

    public string Generate(
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> history,
        IReadOnlyList<FileMetrics> latestFiles,
        IReadOnlyList<CommitFileStats>? commitStats = null,
        IReadOnlyList<FileMetrics>? prevFiles = null,
        string? repositoryName = null)
    {
        var ordered = history.OrderBy(h => h.Commit.Timestamp).ToList();
        var deltas = ComputeDeltas(ordered);
        var (troubled, heroic) = ClassifyCommits(deltas);

        var largeFiles = latestFiles.OrderByDescending(f => f.Sloc).Take(TopFilesCount).ToList();
        var complexFiles = latestFiles
            .Where(f => f.CyclomaticComplexity > 0)
            .OrderByDescending(f => f.CyclomaticComplexity)
            .Take(TopFilesCount).ToList();
        var smellyFiles = latestFiles
            .Where(f => f.SmellsHigh > 0 || f.SmellsMedium > 0 || f.SmellsLow > 0)
            .OrderByDescending(f => f.SmellsHigh * 3 + f.SmellsMedium * 2 + f.SmellsLow)
            .Take(TopFilesCount).ToList();
        var coupledFiles = latestFiles
            .Where(f => f.CouplingProxy > 0)
            .OrderByDescending(f => f.CouplingProxy)
            .Take(TopFilesCount).ToList();

        double[] badness = latestFiles.Count > 0 ? EntropyCalculator.ComputeBadness(latestFiles) : [];

        return BuildHtml(ordered, deltas, troubled, heroic, largeFiles, complexFiles, smellyFiles, coupledFiles, latestFiles, badness, commitStats, prevFiles, repositoryName);
    }

    public record CommitDelta(CommitInfo Commit, RepoMetrics Metrics, double Delta, double RelativeDelta, int SlocDelta = 0, int FilesDelta = 0);

    /// <summary>
    /// Generates a standalone HTML refactor report showing the top <paramref name="topN"/> files
    /// recommended for refactoring, ranked by the supplied per-file <paramref name="scores"/>.
    /// </summary>
    /// <param name="files">File metrics to rank.</param>
    /// <param name="scores">Parallel per-file scores from <see cref="EntropyCalculator.ComputeRefactorScores"/>; higher = higher priority.</param>
    /// <param name="focus">Metric(s) used for ranking (shown in the report header).</param>
    /// <param name="topN">Maximum number of files to include in the report.</param>
    public string GenerateRefactorReport(
        IReadOnlyList<FileMetrics> files,
        double[] scores,
        string focus,
        int topN = 10)
    {
        var ranked = files.Zip(scores)
            .OrderByDescending(x => x.Second)
            .Take(topN)
            .ToList();

        var sb = new StringBuilder();
        var reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        AppendHtmlHeader(sb, reportDate);
        AppendRefactorHeader(sb, focus, topN, reportDate);
        AppendRefactorTable(sb, ranked);
        AppendRefactorChart(sb, ranked, focus);
        AppendHtmlFooter(sb);

        return sb.ToString();
    }

    private static void AppendRefactorHeader(StringBuilder sb, string focus, int topN, string reportDate)
    {
        sb.AppendLine($$"""
              <header>
                <div>
                  <h1>🔧 EntropyX Refactor Report</h1>
                  <div class="subtitle">Top {{topN}} files by <strong style="color:var(--accent)">{{EscapeHtml(focus)}}</strong> &nbsp;·&nbsp; Generated {{reportDate}}</div>
                </div>
              </header>
              <div class="container">
            """);
    }

    private static void AppendRefactorTable(StringBuilder sb, List<(FileMetrics First, double Second)> ranked)
    {
        if (ranked.Count == 0)
        {
            sb.AppendLine("""<section><p class="empty-msg">No files found.</p></section>""");
            return;
        }

        double maxScore = ranked.Max(x => x.Second);
        if (maxScore == 0) maxScore = 1.0;

        sb.AppendLine("""
                <section>
                  <div class="card">
                    <h2>📋 Refactor Candidates</h2>
                    <table>
                      <thead>
                        <tr>
                          <th style="text-align:right">#</th>
                          <th>File</th>
                          <th>Language</th>
                          <th style="text-align:right">SLOC</th>
                          <th style="text-align:right">CC</th>
                          <th style="text-align:right">MI</th>
                          <th style="text-align:right">Smells H/M/L</th>
                          <th style="text-align:right">Coupling</th>
                          <th style="text-align:right">Score</th>
                        </tr>
                      </thead>
                      <tbody>
            """);

        for (int i = 0; i < ranked.Count; i++)
        {
            var (file, score) = ranked[i];
            float t = (float)(score / maxScore);
            string color = HeatColorHtml(t);
            string slocBadge = SlocBadge(file.Sloc);
            string ccBadge = CcBadge(file.CyclomaticComplexity);
            string smellBadge = SmellBadge(file.SmellsHigh, file.SmellsMedium, file.SmellsLow);
            string couplingBadge = CouplingBadge(file.CouplingProxy);

            sb.AppendLine($$"""
                        <tr>
                          <td style="text-align:right">{{i + 1}}</td>
                          <td>{{EscapeHtml(file.Path)}}</td>
                          <td>{{EscapeHtml(file.Language.Length > 0 ? file.Language : "—")}}</td>
                          <td style="text-align:right">{{file.Sloc.ToString("N0", CultureInfo.InvariantCulture)}} {{slocBadge}}</td>
                          <td style="text-align:right">{{file.CyclomaticComplexity.ToString("F1", CultureInfo.InvariantCulture)}} {{ccBadge}}</td>
                          <td style="text-align:right">{{file.MaintainabilityIndex.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                          <td style="text-align:right">{{file.SmellsHigh}}/{{file.SmellsMedium}}/{{file.SmellsLow}} {{smellBadge}}</td>
                          <td style="text-align:right">{{file.CouplingProxy.ToString("F0", CultureInfo.InvariantCulture)}} {{couplingBadge}}</td>
                          <td style="text-align:right"><span style="color:{{color}}">{{score.ToString("F3", CultureInfo.InvariantCulture)}}</span></td>
                        </tr>
                """);
        }

        sb.AppendLine("      </tbody></table></div></section>");
    }

    private static void AppendRefactorChart(StringBuilder sb, List<(FileMetrics First, double Second)> ranked, string focus)
    {
        if (ranked.Count == 0) return;

        var labels = string.Join(",", ranked.Select(x => JsonString(Path.GetFileName(x.First.Path))));
        var values = string.Join(",", ranked.Select(x => x.Second.ToString("F4", CultureInfo.InvariantCulture)));

        sb.AppendLine($$"""
                <section>
                  <div class="chart-card">
                    <h2>📊 Refactor Priority Score — {{EscapeHtml(focus)}}</h2>
                    <div class="chart-wrap" style="height:320px"><canvas id="refactorChart"></canvas></div>
                  </div>
                </section>
                <script>
                (function() {
                  var ctx = document.getElementById('refactorChart').getContext('2d');
                  new Chart(ctx, {
                    type: 'bar',
                    data: {
                      labels: [{{labels}}],
                      datasets: [{
                        label: 'Refactor Score',
                        data: [{{values}}],
                        backgroundColor: 'rgba(239,68,68,0.6)',
                        borderColor: '#ef4444',
                        borderWidth: 1
                      }]
                    },
                    options: {
                      indexAxis: 'y',
                      responsive: true,
                      maintainAspectRatio: false,
                      plugins: { legend: { labels: { color: '#888' } } },
                      scales: {
                        x: { ticks: { color: '#888' }, grid: { color: '#2d3044' }, beginAtZero: true },
                        y: { ticks: { color: '#e0e0e0' }, grid: { color: '#2d3044' } }
                      }
                    }
                  });
                })();
                </script>
            """);
    }


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
        RepoMetrics? previousMetrics,
        string? repositoryName = null)
    {
        var ordered = history.OrderBy(h => h.Commit.Timestamp).ToList();
        var deltas = ComputeDeltas(ordered);
        var (troubled, heroic) = ClassifyCommits(deltas);
        double[] badness = files.Count > 0 ? EntropyCalculator.ComputeBadness(files) : [];

        var sb = new StringBuilder();
        var reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        AppendHtmlHeader(sb, reportDate, string.IsNullOrWhiteSpace(repositoryName) ? "EntropyX Drilldown Report" : $"{repositoryName} - EntropyX Drilldown Report");
        AppendDrilldownHeader(sb, commit, metrics, reportDate, repositoryName);
        AppendDrilldownSummaryStats(sb, metrics, previousMetrics);
        var historicalScores = ordered.Select(h => h.Metrics.EntropyScore).ToList();
        AppendDrilldownAssessment(sb, metrics, previousMetrics, historicalScores);
        AppendDrilldownLanguageChart(sb, files);
        AppendHeatmapSection(sb, files, badness);
        AppendDrilldownFileTable(sb, files, badness);
        AppendTroubledSection(sb, troubled);
        AppendHeroicSection(sb, heroic);
        AppendHtmlFooter(sb);

        return sb.ToString();
    }

    private static void AppendDrilldownHeader(StringBuilder sb, CommitInfo commit, RepoMetrics metrics, string reportDate, string? repositoryName)
    {
        var hash = commit.Hash[..Math.Min(8, commit.Hash.Length)];
        var date = commit.Timestamp != DateTimeOffset.MinValue
            ? commit.Timestamp.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
            : "unknown";
        string badge = EntropyBadgeSvg(metrics.EntropyScore);
        var title = string.IsNullOrWhiteSpace(repositoryName)
            ? "⚡ EntropyX Drilldown"
            : $"⚡ EntropyX Drilldown — {EscapeHtml(repositoryName)}";
        sb.AppendLine($$"""
              <header>
                <div>
                  <h1>{{title}}</h1>
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

    private static void AppendDrilldownAssessment(StringBuilder sb, RepoMetrics metrics, RepoMetrics? previous, IReadOnlyList<double> historicalScores)
    {
        double e = metrics.EntropyScore;
        var (grade, color, description) = e switch
        {
            < 0.3 => ("Excellent", "#22c55e", "Entropy drift is very low – the codebase temperature is cool."),
            < 0.7 => ("Good",      "#86efac", "Entropy drift is low – only minor areas are contributing to structural drift."),
            < 1.2 => ("Fair",      "#f59e0b", "Entropy drift is moderate – structural complexity is accumulating over time."),
            < 2.0 => ("Poor",      "#f97316", "Entropy drift is high – structural complexity has spread significantly across the codebase."),
            _     => ("Critical",  "#ef4444", "Entropy drift is very high – complexity is broadly distributed. Use trend analysis to identify when this began.")
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

        string relativeContext = "";
        if (historicalScores.Count >= 3)
        {
            double min  = historicalScores.Min();
            double max  = historicalScores.Max();
            double mean = historicalScores.Average();
            int countAtOrBelow = historicalScores.Count(s => s <= e);
            double pct = (double)countAtOrBelow / historicalScores.Count * 100.0;

            string relativePos = pct switch
            {
                <= 25  => "near its historical low",
                <= 50  => "below its historical average",
                <= 75  => "above its historical average",
                <= 90  => "near its historical high",
                _      => "at or near its all-time high"
            };

            relativeContext = $"""
                    <p style="margin-top:0.75rem;padding:0.75rem 1rem;background:var(--surface);border-radius:8px;border-left:3px solid {color}">
                      <strong>📈 Relative to this repo's own history ({historicalScores.Count} snapshots):</strong><br>
                      This score is <em>{relativePos}</em> — at the {pct.ToString("F0", CultureInfo.InvariantCulture)}{OrdinalSuffix(pct)} percentile of recorded history.<br>
                      <span style="color:var(--muted);font-size:0.85em">Historical range: {min.ToString("F4", CultureInfo.InvariantCulture)} – {max.ToString("F4", CultureInfo.InvariantCulture)} &nbsp;·&nbsp; avg: {mean.ToString("F4", CultureInfo.InvariantCulture)}</span>
                    </p>
            """;
        }

        sb.AppendLine($$"""
                <section>
                  <div class="card">
                    <h2>📊 Assessment</h2>
                    <p style="font-size:1.1rem;margin-bottom:0.5rem">
                      Drift Level: <strong style="color:{{color}}">{{EscapeHtml(grade)}}</strong>
                      {{(trend.Length > 0 ? $"&nbsp;·&nbsp; {trend}" : "")}}
                    </p>
                    <p style="color:var(--muted)">{{EscapeHtml(description)}}</p>
                    {{relativeContext}}
                    <p style="color:var(--muted);font-size:0.8em;margin-top:0.5rem">ℹ️ EntropyX measures structural drift over time — not a pass/fail grade. A growing codebase naturally accumulates entropy; use trend analysis for actionable insights.</p>
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
                          <thead><tr><th>File</th><th>Language</th><th style="text-align:right">SLOC</th><th style="text-align:right">CC</th><th style="text-align:right">MI</th><th style="text-align:right">Coupling</th><th style="text-align:right">Smells H/M/L</th><th style="text-align:right">Badness</th></tr></thead>
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
                              <td style="text-align:right">{{file.CouplingProxy.ToString("F0", CultureInfo.InvariantCulture)}} {{CouplingBadge(file.CouplingProxy)}}</td>
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
        IReadOnlyList<FileMetrics> coupledFiles,
        IReadOnlyList<FileMetrics> latestFiles,
        double[] badness,
        IReadOnlyList<CommitFileStats>? commitStats = null,
        IReadOnlyList<FileMetrics>? prevFiles = null,
        string? repositoryName = null)
    {
        var sb = new StringBuilder();
        var latest = ordered.Count > 0 ? ordered[^1].Metrics : null;
        var reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        AppendHtmlHeader(sb, reportDate, string.IsNullOrWhiteSpace(repositoryName) ? "EntropyX Code Health Report" : $"{repositoryName} - EntropyX Code Health Report");
        AppendSummarySection(sb, latest, ordered.Count, reportDate, repositoryName);
        AppendGaugesSection(sb, latest, latestFiles, ordered);
        AppendEntropyChart(sb, ordered);
        AppendGrowthChart(sb, ordered);
        if (commitStats is { Count: > 0 })
            AppendCcSmellCharts(sb, commitStats);
        AppendHeatmapSection(sb, latestFiles, badness);
        AppendIssuesSection(sb, largeFiles, complexFiles, smellyFiles, coupledFiles);
        if (latestFiles.Count > 0)
            AppendDiffusionSection(sb, latestFiles, badness, prevFiles);
        AppendTroubledSection(sb, troubled);
        AppendHeroicSection(sb, heroic);
        AppendCommitTableSection(sb, deltas);
        AppendHtmlFooter(sb);

        return sb.ToString();
    }

    private static void AppendHtmlHeader(StringBuilder sb, string reportDate, string pageTitle = "EntropyX Code Health Report")
    {
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
            """);
        sb.Append("  <title>");
        sb.Append(EscapeHtml(pageTitle));
        sb.AppendLine("</title>");
        sb.Append("""
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
                .gauge-stats { display: flex; justify-content: center; gap: 0.75rem; margin-top: 0.6rem; flex-wrap: wrap; }
                .gauge-stat { display: flex; align-items: center; gap: 0.3rem; font-size: 0.75rem; color: var(--muted); }
                .stat-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
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

    private static void AppendSummarySection(StringBuilder sb, RepoMetrics? latest, int commitCount, string reportDate, string? repositoryName)
    {
        string entropy = latest is not null ? latest.EntropyScore.ToString("F4", CultureInfo.InvariantCulture) : "—";
        string files = latest is not null ? latest.TotalFiles.ToString(CultureInfo.InvariantCulture) : "—";
        string sloc = latest is not null ? latest.TotalSloc.ToString("N0", CultureInfo.InvariantCulture) : "—";
        string badge = latest is not null ? EntropyBadgeSvg(latest.EntropyScore) : "";

        var title = string.IsNullOrWhiteSpace(repositoryName)
            ? "⚡ EntropyX Report"
            : $"⚡ EntropyX Report — {EscapeHtml(repositoryName)}";
        sb.AppendLine($$"""
              <header>
                <div>
                  <h1>{{title}}</h1>
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
        var slocPerFileValues = chartData
            .Select(h => h.Metrics.TotalFiles > 0
                ? ((double)h.Metrics.TotalSloc / h.Metrics.TotalFiles).ToString("F1", CultureInfo.InvariantCulture)
                : "0")
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
                  <div class="grid-2" style="margin-top:1.5rem">
                    <div class="chart-card">
                      <h2>SLOC per File Over Time</h2>
                      <div class="chart-wrap"><canvas id="slocPerFileChart"></canvas></div>
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
                  mkChart('slocPerFileChart', 'SLOC / File', [{{string.Join(",", slocPerFileValues)}}], 'rgba(251,191,36,1)');
                })();
                </script>
            """);
    }

    private static void AppendCcSmellCharts(StringBuilder sb, IReadOnlyList<CommitFileStats> commitStats)
    {
        var chartData = Downsample(commitStats, MaxChartPoints);
        var labels = chartData
            .Select(s => JsonString(s.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToArray();
        var ccValues = chartData
            .Select(s => s.AvgCc.ToString("F2", CultureInfo.InvariantCulture))
            .ToArray();
        var smellValues = chartData
            .Select(s => s.AvgSmell.ToString("F2", CultureInfo.InvariantCulture))
            .ToArray();

        sb.AppendLine($$"""
                <section>
                  <div class="grid-2">
                    <div class="chart-card">
                      <h2>Avg Cyclomatic Complexity Over Time</h2>
                      <div class="chart-wrap"><canvas id="ccChart"></canvas></div>
                    </div>
                    <div class="chart-card">
                      <h2>Avg Smell Score Over Time</h2>
                      <div class="chart-wrap"><canvas id="smellChart"></canvas></div>
                    </div>
                  </div>
                </section>
                <script>
                (function() {
                  function mkChart2(id, label, data, color) {
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
                  mkChart2('ccChart',    'Avg Cyclomatic Complexity', [{{string.Join(",", ccValues)}}],    'rgba(239,68,68,1)');
                  mkChart2('smellChart', 'Avg Smell Score',           [{{string.Join(",", smellValues)}}], 'rgba(168,85,247,1)');
                })();
                </script>
            """);
    }

    private static void AppendIssuesSection(StringBuilder sb,
        IReadOnlyList<FileMetrics> largeFiles,
        IReadOnlyList<FileMetrics> complexFiles,
        IReadOnlyList<FileMetrics> smellyFiles,
        IReadOnlyList<FileMetrics> coupledFiles)
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

        // High coupling
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>🔗 High Coupling Areas</h2>
                    <details open>
                      <summary>Top files by Coupling (import count) <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        AppendFileTable(sb, coupledFiles, "Coupling", f => f.CouplingProxy.ToString("F0", CultureInfo.InvariantCulture),
            f => CouplingBadge(f.CouplingProxy));
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

    private static void AppendDiffusionSection(
        StringBuilder sb,
        IReadOnlyList<FileMetrics> files,
        double[] badness,
        IReadOnlyList<FileMetrics>? prevFiles)
    {
        double[] diffusion = EntropyCalculator.ComputeDiffusionContributions(badness);

        // Top diffusion contributors: highest -p_i·log₂(p_i)
        // Zip all three parallel arrays together once to avoid repeated index lookups
        var fileStats = files
            .Zip(badness, (f, b) => (File: f, Badness: b))
            .Zip(diffusion, (fb, d) => (fb.File, fb.Badness, Diffusion: d))
            .ToList();

        var topDiffusion = fileStats
            .Where(x => x.Diffusion > 0)
            .OrderByDescending(x => x.Diffusion)
            .Take(TopFilesCount)
            .ToList();

        // Top badness contributors: highest raw b_i
        var topBadness = fileStats
            .Where(x => x.Badness > 0)
            .OrderByDescending(x => x.Badness)
            .Take(TopFilesCount)
            .ToList();

        // Top delta contributors: largest increase in badness vs previous commit
        // Match files by path; new files (no prev) get Δb = b_i(new)
        List<(FileMetrics File, double Delta, double OldBadness, double NewBadness)> topDelta = [];
        if (prevFiles is { Count: > 0 })
        {
            double[] prevBadness = EntropyCalculator.ComputeBadness(prevFiles);
            var prevBadnessByPath = prevFiles.Zip(prevBadness)
                .ToDictionary(x => x.First.Path, x => x.Second);

            topDelta = files.Zip(badness)
                .Select(x =>
                {
                    double oldB = prevBadnessByPath.TryGetValue(x.First.Path, out var pb) ? pb : 0.0;
                    return (x.First, x.Second - oldB, oldB, x.Second);
                })
                .Where(x => x.Item2 > 1e-9)
                .OrderByDescending(x => x.Item2)
                .Take(TopFilesCount)
                .ToList();
        }

        sb.AppendLine("""
                <section>
                  <div class="section-title">📊 Entropy Contribution Analysis</div>
                  <p style="color:var(--muted);font-size:13px;margin-bottom:1.5rem">
                    These three lists decompose the entropy score into per-file signals.
                    A file can be a top diffusion spreader without being the worst in raw badness — and vice versa.
                  </p>
            """);

        // ── Diffusion contributors ─────────────────────────────────────────────
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>🌊 Top Diffusion Contributors</h2>
                    <p style="color:var(--muted);font-size:13px;margin-bottom:1rem">
                      Files with the highest <code style="color:var(--accent)">−p<sub>i</sub>·log₂(p<sub>i</sub>)</code> term.
                      These are the <strong>entropy spreaders</strong> — files pulling complexity
                      toward a uniform distribution and away from a single obvious hotspot.
                    </p>
                    <details open>
                      <summary>Top files by diffusion contribution <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        if (topDiffusion.Count == 0)
        {
            sb.AppendLine("""    <p class="empty-msg">No diffusion data available.</p>""");
        }
        else
        {
            sb.AppendLine("""
                        <table>
                          <thead><tr><th>File</th><th>Language</th><th>Diffusion</th><th>Badness</th><th>Status</th></tr></thead>
                          <tbody>
                """);
            foreach (var (f, b, contrib) in topDiffusion)
            {
                sb.AppendLine($$"""
                            <tr>
                              <td>{{EscapeHtml(f.Path)}}</td>
                              <td>{{EscapeHtml(f.Language)}}</td>
                              <td><strong style="color:var(--accent)">{{contrib.ToString("F4", CultureInfo.InvariantCulture)}}</strong></td>
                              <td>{{b.ToString("F3", CultureInfo.InvariantCulture)}}</td>
                              <td>{{DiffusionBadge(contrib)}}</td>
                            </tr>
                    """);
            }
            sb.AppendLine("      </tbody></table>");
        }
        sb.AppendLine("      </div></details></div>");

        // ── Badness contributors ───────────────────────────────────────────────
        sb.AppendLine("""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>☠ Top Badness Contributors</h2>
                    <p style="color:var(--muted);font-size:13px;margin-bottom:1rem">
                      Files with the highest raw <strong>badness score</strong> <code style="color:var(--accent)">b<sub>i</sub></code>.
                      These are the <strong>most problematic files</strong> by combined SLOC, complexity, smells, coupling, and maintainability.
                    </p>
                    <details open>
                      <summary>Top files by badness score <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        if (topBadness.Count == 0)
        {
            sb.AppendLine("""    <p class="empty-msg">No badness data available.</p>""");
        }
        else
        {
            sb.AppendLine("""
                        <table>
                          <thead><tr><th>File</th><th>Language</th><th>Badness</th><th>CC</th><th>MI</th><th>Status</th></tr></thead>
                          <tbody>
                """);
            foreach (var (f, b, _) in topBadness)
            {
                sb.AppendLine($$"""
                            <tr>
                              <td>{{EscapeHtml(f.Path)}}</td>
                              <td>{{EscapeHtml(f.Language)}}</td>
                              <td><strong style="color:var(--red)">{{b.ToString("F3", CultureInfo.InvariantCulture)}}</strong></td>
                              <td>{{f.CyclomaticComplexity.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                              <td>{{f.MaintainabilityIndex.ToString("F1", CultureInfo.InvariantCulture)}}</td>
                              <td>{{BadnessBadge(b)}}</td>
                            </tr>
                    """);
            }
            sb.AppendLine("      </tbody></table>");
        }
        sb.AppendLine("      </div></details></div>");

        // ── Delta contributors ─────────────────────────────────────────────────
        sb.AppendLine($$"""
                  <div class="card" style="margin-bottom:1.5rem">
                    <h2>📈 Top Delta Contributors</h2>
                    <p style="color:var(--muted);font-size:13px;margin-bottom:1rem">
                      Files with the largest <strong>increase in badness</strong> since the previous commit (Δ<code style="color:var(--accent)">b<sub>i</sub></code>).
                      {{(prevFiles is null ? "Not available — only one commit has been scanned." : "These files are driving the entropy upward.")}}
                    </p>
            """);
        if (topDelta.Count == 0)
        {
            string msg = prevFiles is null
                ? "Requires at least two scanned commits."
                : "No files increased in badness since the previous commit. 🎉";
            sb.AppendLine($"""    <p class="empty-msg">{msg}</p>""");
        }
        else
        {
            sb.AppendLine("""
                    <details open>
                      <summary>Top files by badness increase <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
                        <table>
                          <thead><tr><th>File</th><th>Language</th><th>Δ Badness</th><th>Prev</th><th>Now</th><th>Status</th></tr></thead>
                          <tbody>
                """);
            foreach (var (f, delta, oldB, newB) in topDelta)
            {
                sb.AppendLine($$"""
                            <tr>
                              <td>{{EscapeHtml(f.Path)}}</td>
                              <td>{{EscapeHtml(f.Language)}}</td>
                              <td><strong style="color:var(--red)">+{{delta.ToString("F3", CultureInfo.InvariantCulture)}}</strong></td>
                              <td>{{oldB.ToString("F3", CultureInfo.InvariantCulture)}}</td>
                              <td>{{newB.ToString("F3", CultureInfo.InvariantCulture)}}</td>
                              <td>{{DeltaBadge(delta)}}</td>
                            </tr>
                    """);
            }
            sb.AppendLine("      </tbody></table></div></details>");
        }
        sb.AppendLine("      </div>");

        sb.AppendLine("</section>");
    }

    private static string DiffusionBadge(double contrib) => contrib switch
    {
        > 0.3 => """<span class="badge badge-red">Dominant spreader</span>""",
        > 0.15 => """<span class="badge badge-yellow">High spread</span>""",
        > 0.05 => """<span class="badge badge-gray">Moderate</span>""",
        _ => """<span class="badge badge-green">Low</span>"""
    };

    private static string BadnessBadge(double b) => b switch
    {
        > 3.0 => """<span class="badge badge-red">Critical</span>""",
        > 2.0 => """<span class="badge badge-yellow">High</span>""",
        > 1.0 => """<span class="badge badge-gray">Moderate</span>""",
        _ => """<span class="badge badge-green">Low</span>"""
    };

    private static string DeltaBadge(double delta) => delta switch
    {
        > 1.0 => """<span class="badge badge-red">Large regression</span>""",
        > 0.5 => """<span class="badge badge-yellow">Regression</span>""",
        > 0.1 => """<span class="badge badge-gray">Minor regression</span>""",
        _ => """<span class="badge badge-green">Minimal</span>"""
    };

    private static void AppendTroubledSection(StringBuilder sb, IReadOnlyList<CommitDelta> troubled)
    {
        sb.AppendLine($$"""
                <section>
                  <div class="card">
                    <h2>😈 Troubled Commits</h2>
                    <p style="color:var(--muted);margin-bottom:1rem;font-size:13px">Commits that significantly worsened codebase entropy.</p>
                    <details open>
                      <summary>{{troubled.Count}} commit(s) that increased entropy <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        if (troubled.Count == 0)
        {
            sb.AppendLine("""    <p class="empty-msg">No troubled commits detected.</p>""");
        }
        else
        {
            AppendDeltaTable(sb, troubled, isHeroic: false);
        }
        sb.AppendLine("      </div></details></div></section>");
    }

    private static void AppendHeroicSection(StringBuilder sb, IReadOnlyList<CommitDelta> heroic)
    {
        sb.AppendLine($$"""
                <section>
                  <div class="card">
                    <h2>🦸 Heroic Commits</h2>
                    <p style="color:var(--muted);margin-bottom:1rem;font-size:13px">Commits that significantly improved codebase entropy.</p>
                    <details open>
                      <summary>{{heroic.Count}} commit(s) that improved entropy <span class="summary-meta">click to expand/collapse</span></summary>
                      <div class="details-body">
            """);
        if (heroic.Count == 0)
        {
            sb.AppendLine("""    <p class="empty-msg">No heroic commits detected.</p>""");
        }
        else
        {
            AppendDeltaTable(sb, heroic, isHeroic: true);
        }
        sb.AppendLine("      </div></details></div></section>");
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

    private static void AppendGaugesSection(StringBuilder sb, RepoMetrics? latest, IReadOnlyList<FileMetrics> files,
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> ordered)
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

        // Historical stats for entropy gauge
        double entropyMin = 0, entropyMean = 0, entropyMax = 0;
        if (ordered.Count > 0)
        {
            var scores = ordered.Select(h => h.Metrics.EntropyScore).ToList();
            entropyMin = scores.Min();
            entropyMean = scores.Average();
            entropyMax = scores.Max();
        }

        // Threshold reference values for CC and smells gauges
        double ccLow = 20.0 * 0.33;   // ~6.6 → green/yellow boundary
        double ccHigh = 20.0 * 0.66;  // ~13.2 → yellow/red boundary
        double smellLow = 15.0 * 0.33;
        double smellHigh = 15.0 * 0.66;

        string entropyHistoryStats = ordered.Count > 0
            ? $"<div class=\"gauge-stats\">" +
              $"<span class=\"gauge-stat\"><span class=\"stat-dot\" style=\"background:#22c55e\"></span>Min: {entropyMin.ToString("F4", CultureInfo.InvariantCulture)}</span>" +
              $"<span class=\"gauge-stat\"><span class=\"stat-dot\" style=\"background:#7c6af7\"></span>Avg: {entropyMean.ToString("F4", CultureInfo.InvariantCulture)}</span>" +
              $"<span class=\"gauge-stat\"><span class=\"stat-dot\" style=\"background:#ef4444\"></span>Max: {entropyMax.ToString("F4", CultureInfo.InvariantCulture)}</span>" +
              "</div>"
            : string.Empty;

        sb.AppendLine($$"""
                <section>
                  <div class="grid-3">
                    <div class="chart-card">
                      <h2>Entropy Health</h2>
                      <div class="gauge-wrap"><canvas id="gaugeEntropy"></canvas></div>
                      <div class="gauge-value" style="color:{{entropyColor}}">{{entropy.ToString("F4", CultureInfo.InvariantCulture)}}</div>
                      <div class="gauge-label">EntropyX Score</div>
                      {{entropyHistoryStats}}
                    </div>
                    <div class="chart-card">
                      <h2>Complexity Health</h2>
                      <div class="gauge-wrap"><canvas id="gaugeCc"></canvas></div>
                      <div class="gauge-value" style="color:{{ccColor}}">{{avgCc.ToString("F2", CultureInfo.InvariantCulture)}}</div>
                      <div class="gauge-label">Avg Cyclomatic Complexity</div>
                      <div class="gauge-stats">
                        <span class="gauge-stat"><span class="stat-dot" style="background:#22c55e"></span>Low: {{ccLow.ToString("F1", CultureInfo.InvariantCulture)}}</span>
                        <span class="gauge-stat"><span class="stat-dot" style="background:#ef4444"></span>High: {{ccHigh.ToString("F1", CultureInfo.InvariantCulture)}}</span>
                      </div>
                    </div>
                    <div class="chart-card">
                      <h2>Smell Health</h2>
                      <div class="gauge-wrap"><canvas id="gaugeSmells"></canvas></div>
                      <div class="gauge-value" style="color:{{smellsColor}}">{{avgSmells.ToString("F1", CultureInfo.InvariantCulture)}}</div>
                      <div class="gauge-label">Avg Weighted Smell Score</div>
                      <div class="gauge-stats">
                        <span class="gauge-stat"><span class="stat-dot" style="background:#22c55e"></span>Low: {{smellLow.ToString("F1", CultureInfo.InvariantCulture)}}</span>
                        <span class="gauge-stat"><span class="stat-dot" style="background:#ef4444"></span>High: {{smellHigh.ToString("F1", CultureInfo.InvariantCulture)}}</span>
                      </div>
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
    /// Exports standalone SVG line-chart figures to <paramref name="outputDir"/>.
    /// Generates: entropy-over-time.svg, sloc-over-time.svg, sloc-per-file-over-time.svg,
    /// and (when <paramref name="commitStats"/> is supplied) cc-over-time.svg and smell-over-time.svg.
    /// </summary>
    public static void ExportSvgFigures(
        string outputDir,
        IReadOnlyList<(CommitInfo Commit, RepoMetrics Metrics)> history,
        IReadOnlyList<CommitFileStats>? commitStats = null,
        string? repositoryName = null)
    {
        Directory.CreateDirectory(outputDir);
        var ordered = history.OrderBy(h => h.Commit.Timestamp).ToList();
        var sampled = Downsample(ordered, MaxChartPoints);

        var entropyPts = sampled
            .Select(h => (h.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                          h.Metrics.EntropyScore))
            .ToList();
        File.WriteAllText(Path.Combine(outputDir, "entropy-over-time.svg"),
            BuildLineSvg(WithRepositoryName("Entropy Over Time", repositoryName), "Date", "Entropy Score", entropyPts, "#7c6af7"));

        var slocPts = sampled
            .Select(h => (h.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                          (double)h.Metrics.TotalSloc))
            .ToList();
        File.WriteAllText(Path.Combine(outputDir, "sloc-over-time.svg"),
            BuildLineSvg(WithRepositoryName("SLOC Over Time", repositoryName), "Date", "SLOC", slocPts, "#22c55e"));

        var slocPerFilePts = sampled
            .Select(h => (h.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                          h.Metrics.TotalFiles > 0 ? (double)h.Metrics.TotalSloc / h.Metrics.TotalFiles : 0.0))
            .ToList();
        File.WriteAllText(Path.Combine(outputDir, "sloc-per-file-over-time.svg"),
            BuildLineSvg(WithRepositoryName("SLOC per File Over Time", repositoryName), "Date", "SLOC / File", slocPerFilePts, "#f59e0b"));

        if (commitStats is { Count: > 0 })
        {
            var sampledStats = Downsample(commitStats, MaxChartPoints);

            var ccPts = sampledStats
                .Select(s => (s.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                              s.AvgCc))
                .ToList();
            File.WriteAllText(Path.Combine(outputDir, "cc-over-time.svg"),
                BuildLineSvg(WithRepositoryName("Avg Cyclomatic Complexity Over Time", repositoryName), "Date", "Avg CC", ccPts, "#ef4444"));

            var smellPts = sampledStats
                .Select(s => (s.Commit.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                              s.AvgSmell))
                .ToList();
            File.WriteAllText(Path.Combine(outputDir, "smell-over-time.svg"),
                BuildLineSvg(WithRepositoryName("Avg Smell Score Over Time", repositoryName), "Date", "Avg Smell", smellPts, "#a855f7"));
        }
    }

    private static string WithRepositoryName(string title, string? repositoryName) =>
        string.IsNullOrWhiteSpace(repositoryName) ? title : $"{repositoryName} — {title}";

    /// <summary>
    /// Generates a standalone SVG line chart suitable for embedding in papers/whitepapers.
    /// </summary>
    private static string BuildLineSvg(
        string title,
        string xLabel,
        string yLabel,
        IReadOnlyList<(string Label, double Value)> points,
        string lineColor)
    {
        const int W = 900, H = 420;
        const int padL = 72, padR = 30, padT = 50, padB = 60;
        int chartW = W - padL - padR;
        int chartH = H - padT - padB;

        if (points.Count == 0)
        {
            return $"""<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}"><rect width="{W}" height="{H}" fill="#0f1117"/><text x="{W / 2}" y="{H / 2}" text-anchor="middle" fill="#888" font-family="sans-serif" font-size="16">{EscapeHtml(title)} — no data</text></svg>""";
        }

        double minV = points.Min(p => p.Value);
        double maxV = points.Max(p => p.Value);
        if (Math.Abs(maxV - minV) < 1e-12) { minV -= 1; maxV += 1; }

        // Nice round Y-axis ticks
        int tickCount = 5;
        double rawStep = (maxV - minV) / (tickCount - 1);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double niceStep = rawStep / mag switch { <= 1 => 1, <= 2 => 2, <= 5 => 5, _ => 10 } * mag;
        double yAxisMin = Math.Floor(minV / niceStep) * niceStep;
        double yAxisMax = Math.Ceiling(maxV / niceStep) * niceStep;
        if (Math.Abs(yAxisMax - yAxisMin) < 1e-12) yAxisMax = yAxisMin + niceStep;

        double xScale = (double)chartW / Math.Max(1, points.Count - 1);
        double yScale = chartH / (yAxisMax - yAxisMin);

        // Map a data point to SVG coordinates
        double Px(int i) => padL + i * xScale;
        double Py(double v) => padT + chartH - (v - yAxisMin) * yScale;
        static string FormatInvariantF1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">""");

        // Background
        sb.Append($"""<rect width="{W}" height="{H}" fill="#0f1117"/>""");

        // Title
        sb.Append($"""<text x="{W / 2}" y="28" text-anchor="middle" fill="#e0e0e0" font-family="Segoe UI,sans-serif" font-size="16" font-weight="bold">{EscapeHtml(title)}</text>""");

        // Y label (rotated)
        sb.Append($"""<text x="14" y="{padT + chartH / 2}" text-anchor="middle" fill="#888" font-family="Segoe UI,sans-serif" font-size="12" transform="rotate(-90,14,{padT + chartH / 2})">{EscapeHtml(yLabel)}</text>""");

        // X label
        sb.Append($"""<text x="{padL + chartW / 2}" y="{H - 8}" text-anchor="middle" fill="#888" font-family="Segoe UI,sans-serif" font-size="12">{EscapeHtml(xLabel)}</text>""");

        // Grid lines + Y-axis ticks
        // Format: values ≥ 1 000 are shown as "Xk", values ≥ 1 use one decimal, smaller values use four decimals (e.g. entropy scores like 0.0012)
        const double ThousandsThreshold = 1_000;
        const double DecimalThreshold   = 1;
        int gridSteps = tickCount;
        for (int t = 0; t <= gridSteps; t++)
        {
            double v = yAxisMin + t * (yAxisMax - yAxisMin) / gridSteps;
            double sy = Py(v);
            sb.Append($"""<line x1="{padL}" y1="{FormatInvariantF1(sy)}" x2="{padL + chartW}" y2="{FormatInvariantF1(sy)}" stroke="#2d3044" stroke-width="1"/>""");
            string tickLbl = v >= ThousandsThreshold ? (v / ThousandsThreshold).ToString("F1", CultureInfo.InvariantCulture) + "k"
                           : v >= DecimalThreshold   ? v.ToString("F1", CultureInfo.InvariantCulture)
                           :                           v.ToString("F4", CultureInfo.InvariantCulture);
            sb.Append($"""<text x="{padL - 6}" y="{FormatInvariantF1(sy + 4)}" text-anchor="end" fill="#888" font-family="Segoe UI,sans-serif" font-size="11">{EscapeHtml(tickLbl)}</text>""");
        }

        // X-axis tick labels (show at most 10 evenly spaced)
        int xTickCount = Math.Min(10, points.Count);
        double xTickStep = (double)(points.Count - 1) / Math.Max(1, xTickCount - 1);
        for (int t = 0; t < xTickCount; t++)
        {
            int idx = (int)Math.Round(t * xTickStep);
            if (idx >= points.Count) idx = points.Count - 1;
            double sx = Px(idx);
            sb.Append($"""<text x="{FormatInvariantF1(sx)}" y="{padT + chartH + 18}" text-anchor="middle" fill="#888" font-family="Segoe UI,sans-serif" font-size="11">{EscapeHtml(points[idx].Label)}</text>""");
        }

        // Axes
        sb.Append($"""<line x1="{padL}" y1="{padT}" x2="{padL}" y2="{padT + chartH}" stroke="#888" stroke-width="1"/>""");
        sb.Append($"""<line x1="{padL}" y1="{padT + chartH}" x2="{padL + chartW}" y2="{padT + chartH}" stroke="#888" stroke-width="1"/>""");

        // Filled area under the line
        var fillPts = new StringBuilder();
        fillPts.Append($"{FormatInvariantF1(Px(0))},{padT + chartH} ");
        foreach (var (i, (_, v)) in points.Select((p, i) => (i, p)))
            fillPts.Append($"{FormatInvariantF1(Px(i))},{FormatInvariantF1(Py(v))} ");
        fillPts.Append($"{FormatInvariantF1(Px(points.Count - 1))},{padT + chartH}");

        // Parse lineColor to derive fill opacity variant (e.g. #7c6af7 → rgba)
        string fillColor = lineColor.StartsWith('#')
            ? $"rgba({Convert.ToInt32(lineColor[1..3], 16)},{Convert.ToInt32(lineColor[3..5], 16)},{Convert.ToInt32(lineColor[5..7], 16)},0.2)"
            : lineColor;
        sb.Append($"""<polygon points="{fillPts}" fill="{fillColor}"/>""");

        // The line itself
        var linePts = string.Join(" ", points.Select((p, i) => $"{FormatInvariantF1(Px(i))},{FormatInvariantF1(Py(p.Value))}"));
        sb.Append($"""<polyline points="{linePts}" fill="none" stroke="{EscapeHtml(lineColor)}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>""");

        sb.Append("</svg>");
        return sb.ToString();
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
                    couplingProxy = x.First.CouplingProxy,
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

    private static string CouplingBadge(double coupling) => coupling switch
    {
        > 20 => """<span class="badge badge-red">Very High</span>""",
        > 10 => """<span class="badge badge-yellow">High</span>""",
        > 5 => """<span class="badge badge-gray">Moderate</span>""",
        _ => """<span class="badge badge-green">Low</span>"""
    };

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string JsonString(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>Returns the correct English ordinal suffix (st/nd/rd/th) for a percentile value.</summary>
    private static string OrdinalSuffix(double value)
    {
        int n = (int)Math.Round(value);
        if (n % 100 is 11 or 12 or 13)
            return "th";
        return (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }

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
