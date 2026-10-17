using SkiaSharp;

namespace Jellyfin.Plugin.Showtime.Trivia;

/// <summary>
/// Draws 1920x1080 slide frames: letterboxed source images and generated trivia cards.
/// </summary>
public static class SlideCanvas
{
    public const int Width = 1920;
    public const int Height = 1080;

    private static readonly SKColor Gold = new(0xE8, 0xC1, 0x6A);
    private static readonly SKColor Ink = new(0xF4, 0xF1, 0xEA);
    private static readonly SKColor Muted = new(0xA8, 0xA2, 0x96);

    private static readonly Lazy<SKTypeface> Regular = new(() => LoadTypeface(SKFontStyle.Normal));
    private static readonly Lazy<SKTypeface> Bold = new(() => LoadTypeface(SKFontStyle.Bold));

    /// <summary>Copies an existing slide image onto a black 1080p frame, scaled to fit.</summary>
    public static void Letterbox(string source, string destination)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using (var bitmap = SKBitmap.Decode(source))
        {
            if (bitmap is not null)
            {
                DrawFitted(canvas, bitmap, new SKRect(0, 0, Width, Height));
            }
        }

        Save(surface, destination);
    }

    /// <summary>A question or clue card: header, big wrapped text, optional sub line.</summary>
    public static void TextCard(string destination, string header, string text, string? sub, string? backdrop)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        DrawBackground(canvas, backdrop);

        using var paint = new SKPaint { IsAntialias = true };
        DrawHeader(canvas, header, paint);

        using var font = new SKFont(Bold.Value, 84);
        var lines = Wrap(text, font, Width - 360);
        while ((lines.Count > 6 || lines.Any(l => font.MeasureText(l) > Width - 360)) && font.Size > 40)
        {
            font.Size -= 6;
            lines = Wrap(text, font, Width - 360);
        }

        var lineHeight = font.Size * 1.25f;
        var y = (Height - (lines.Count * lineHeight)) / 2 + font.Size;
        paint.Color = Ink;
        foreach (var line in lines)
        {
            canvas.DrawText(line, Width / 2f, y, SKTextAlign.Center, font, paint);
            y += lineHeight;
        }

        if (!string.IsNullOrWhiteSpace(sub))
        {
            using var subFont = new SKFont(Regular.Value, 44);
            paint.Color = Muted;
            canvas.DrawText(sub, Width / 2f, Math.Min(Height - 120, y + 50), SKTextAlign.Center, subFont, paint);
        }

        Save(surface, destination);
    }

    /// <summary>An answer card: poster on the left, answer and details on the right.</summary>
    public static void AnswerCard(string destination, string answer, string? detail, string? poster, string? backdrop)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        DrawBackground(canvas, backdrop);

        using var paint = new SKPaint { IsAntialias = true };
        DrawHeader(canvas, "ANSWER", paint);

        var textLeft = 180f;
        if (!string.IsNullOrEmpty(poster) && File.Exists(poster))
        {
            using var bitmap = SKBitmap.Decode(poster);
            if (bitmap is not null)
            {
                // Fit inside a portrait-ish box so wide fallback images can't crowd out the text.
                var scale = Math.Min(600f / bitmap.Width, 720f / bitmap.Height);
                var w = bitmap.Width * scale;
                var h = bitmap.Height * scale;
                var rect = SKRect.Create(180, (Height - h) / 2 + 40, w, h);
                using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 160), ImageFilter = SKImageFilter.CreateBlur(24, 24) };
                canvas.DrawRect(rect, shadow);
                canvas.DrawBitmap(bitmap, rect);
                textLeft = rect.Right + 90;
            }
        }

        var maxWidth = Width - textLeft - 160;
        using var font = new SKFont(Bold.Value, 96);
        var lines = Wrap(answer, font, maxWidth);
        while ((lines.Count > 4 || lines.Any(l => font.MeasureText(l) > maxWidth)) && font.Size > 40)
        {
            font.Size -= 6;
            lines = Wrap(answer, font, maxWidth);
        }

        var lineHeight = font.Size * 1.2f;
        var y = (Height - (lines.Count * lineHeight)) / 2 + font.Size;
        paint.Color = Ink;
        foreach (var line in lines)
        {
            canvas.DrawText(line, textLeft, y, SKTextAlign.Left, font, paint);
            y += lineHeight;
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            using var detailFont = new SKFont(Regular.Value, 46);
            paint.Color = Gold;
            canvas.DrawText(detail, textLeft, y + 30, SKTextAlign.Left, detailFont, paint);
        }

        Save(surface, destination);
    }

    private static void DrawBackground(SKCanvas canvas, string? backdrop)
    {
        canvas.Clear(new SKColor(0x0B, 0x0B, 0x10));
        if (!string.IsNullOrEmpty(backdrop) && File.Exists(backdrop))
        {
            using var bitmap = SKBitmap.Decode(backdrop);
            if (bitmap is not null)
            {
                using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(18, 18) };
                canvas.Save();
                canvas.ClipRect(new SKRect(0, 0, Width, Height));
                DrawFill(canvas, bitmap, new SKRect(-40, -40, Width + 40, Height + 40), blur);
                canvas.Restore();
                canvas.DrawColor(new SKColor(0, 0, 0, 185));
            }
        }

        // Soft vignette toward the bottom, like a theatre screen.
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, Height),
            new[] { new SKColor(0x1A, 0x14, 0x24, 90), new SKColor(0, 0, 0, 200) },
            SKShaderTileMode.Clamp);
        using var vignette = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, Width, Height), vignette);
    }

    private static void DrawHeader(SKCanvas canvas, string header, SKPaint paint)
    {
        using var font = new SKFont(Bold.Value, 40) { Embolden = false };
        paint.Color = Gold;
        var spaced = string.Join(' ', header.ToUpperInvariant().ToCharArray());
        canvas.DrawText(spaced, Width / 2f, 130, SKTextAlign.Center, font, paint);
        canvas.DrawRect(SKRect.Create((Width / 2f) - 60, 160, 120, 3), paint);
    }

    private static void DrawFitted(SKCanvas canvas, SKBitmap bitmap, SKRect bounds)
    {
        var scale = Math.Min(bounds.Width / bitmap.Width, bounds.Height / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        var rect = SKRect.Create(bounds.Left + ((bounds.Width - w) / 2), bounds.Top + ((bounds.Height - h) / 2), w, h);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private static void DrawFill(SKCanvas canvas, SKBitmap bitmap, SKRect bounds, SKPaint paint)
    {
        var scale = Math.Max(bounds.Width / bitmap.Width, bounds.Height / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        var rect = SKRect.Create(bounds.Left + ((bounds.Width - w) / 2), bounds.Top + ((bounds.Height - h) / 2), w, h);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    private static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (font.MeasureText(candidate) > maxWidth && line.Length > 0)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    private static void Save(SKSurface surface, string destination)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using var stream = File.Create(destination);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Server containers often have few fonts; fall back to any TrueType file on disk so text never renders blank.
    /// </summary>
    private static SKTypeface LoadTypeface(SKFontStyle style)
    {
        foreach (var family in new[] { "Inter", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Roboto", "Arial" })
        {
            var face = SKTypeface.FromFamilyName(family, style);
            if (face is not null && string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return face;
            }
        }

        var wantBold = style.Weight >= (int)SKFontStyleWeight.Bold;
        foreach (var dir in new[] { "/usr/share/fonts", "/usr/local/share/fonts", "/jellyfin/jellyfin-web/assets/fonts" })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => Path.GetFileName(f).Contains("Sans", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => Path.GetFileName(f).Contains("Bold", StringComparison.OrdinalIgnoreCase) == wantBold)
                .ToList();
            foreach (var file in files)
            {
                var face = SKTypeface.FromFile(file);
                if (face is not null)
                {
                    return face;
                }
            }
        }

        return SKTypeface.Default;
    }
}
