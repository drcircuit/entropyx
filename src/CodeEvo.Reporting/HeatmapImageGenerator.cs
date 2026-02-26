using CodeEvo.Core.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CodeEvo.Reporting;

/// <summary>
/// Generates a PNG complexity heatmap image using an IR camera color palette.
/// Each file occupies a row whose background color reflects its normalized
/// badness score: black → purple → blue → cyan → green → yellow → orange → red → white.
/// </summary>
public static class HeatmapImageGenerator
{
    private const int RowHeight  = 28;
    private const int HeatWidth  = 80;
    private const int LabelPad   = 8;
    private const int ScaleHeight = 40;
    private const int ScalePad   = 20;
    private const int MinWidth   = 600;

    // IR camera "thermal" palette key-stops (t ∈ [0,1] → RGB)
    private static readonly (float t, byte r, byte g, byte b)[] IrPalette =
    [
        (0.00f,   0,   0,   0),   // black
        (0.12f,  80,   0, 130),   // indigo / dark purple
        (0.25f,   0,   0, 200),   // blue
        (0.38f,   0, 200, 200),   // cyan
        (0.50f,   0, 180,   0),   // green
        (0.62f, 220, 220,   0),   // yellow
        (0.75f, 255, 140,   0),   // orange
        (0.88f, 220,   0,   0),   // red
        (1.00f, 255, 255, 255),   // white
    ];

    /// <summary>
    /// Renders the heatmap and saves it to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="files">Source file metrics, in display order.</param>
    /// <param name="badness">Parallel badness array from <see cref="Core.EntropyCalculator.ComputeBadness"/>.</param>
    /// <param name="outputPath">Destination PNG file path.</param>
    public static void Generate(IReadOnlyList<FileMetrics> files, double[] badness, string outputPath)
    {
        if (files.Count == 0)
            return;

        double max = badness.Length > 0 ? badness.Max() : 1.0;
        if (max == 0) max = 1.0;

        // Sort descending by badness
        var sorted = files.Zip(badness)
            .OrderByDescending(x => x.Second)
            .ToList();

        // Measure the longest path to size the image
        int maxLabelLen = sorted.Max(x => x.First.Path.Length);
        int labelWidth  = Math.Max(MinWidth - HeatWidth - LabelPad * 2, maxLabelLen * 7 + LabelPad * 2);
        int imgWidth    = HeatWidth + labelWidth + LabelPad * 2;
        int imgHeight   = sorted.Count * RowHeight + ScaleHeight + ScalePad * 2;

        using var img = new Image<Rgba32>(imgWidth, imgHeight);

        img.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgb(30, 30, 30)); // dark background

            // Draw file rows
            for (int i = 0; i < sorted.Count; i++)
            {
                var (file, b) = sorted[i];
                float t = (float)(b / max);
                var heat = IrColor(t);

                int y = i * RowHeight;

                // Heat swatch
                ctx.Fill(heat, new Rectangle(0, y, HeatWidth, RowHeight));

                // Alternating label background for readability
                var labelBg = i % 2 == 0
                    ? Color.FromRgb(45, 45, 45)
                    : Color.FromRgb(38, 38, 38);
                ctx.Fill(labelBg, new Rectangle(HeatWidth, y, labelWidth + LabelPad * 2, RowHeight));

                // Score text inside heat swatch
                DrawText(ctx, $"{b:F2}", Color.FromRgb(255, 255, 255),
                    new PointF(LabelPad / 2f, y + 7));

                // File path label
                string label = file.Path;
                int maxChars = (labelWidth + LabelPad * 2 - LabelPad) / 7;
                if (label.Length > maxChars)
                    label = "…" + label[^(maxChars - 1)..];
                DrawText(ctx, label, Color.FromRgb(220, 220, 220),
                    new PointF(HeatWidth + LabelPad, y + 7));
            }

            // Scale bar
            int scaleY = sorted.Count * RowHeight + ScalePad;
            DrawColorScale(ctx, scaleY, imgWidth, ScaleHeight - ScalePad);
        });

        img.SaveAsPng(outputPath);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Interpolates the IR palette at position <paramref name="t"/> ∈ [0, 1].</summary>
    public static Color IrColor(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        for (int i = 1; i < IrPalette.Length; i++)
        {
            var (t0, r0, g0, b0) = IrPalette[i - 1];
            var (t1, r1, g1, b1) = IrPalette[i];
            if (t > t1) continue;
            float alpha = (t - t0) / (t1 - t0);
            return Color.FromRgb(
                Lerp(r0, r1, alpha),
                Lerp(g0, g1, alpha),
                Lerp(b0, b1, alpha));
        }
        var last = IrPalette[^1];
        return Color.FromRgb(last.r, last.g, last.b);
    }

    private static void DrawColorScale(IImageProcessingContext ctx, int y, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            float t = (float)x / (width - 1);
            ctx.Fill(IrColor(t), new Rectangle(x, y, 1, height));
        }
        DrawText(ctx, "Cool (low badness)",  Color.White, new PointF(4,            y + height + 2));
        DrawText(ctx, "Hot (high badness)",  Color.White, new PointF(width - 130f, y + height + 2));
    }

    private static void DrawText(IImageProcessingContext ctx, string text, Color color, PointF position)
    {
        // Use the system-default font family; fall back gracefully when none is available.
        if (!SystemFonts.TryGet("Arial", out var family) &&
            !SystemFonts.TryGet("DejaVu Sans", out family) &&
            !SystemFonts.TryGet("Liberation Sans", out family))
        {
            // No system font found – skip text rendering (colours still correct)
            return;
        }

        var font    = family.CreateFont(11, FontStyle.Regular);
        var options = new RichTextOptions(font) { Origin = position };
        ctx.DrawText(options, text, color);
    }

    private static byte Lerp(byte a, byte b, float t) =>
        (byte)(a + (b - a) * t);
}
