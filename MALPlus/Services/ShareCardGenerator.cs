using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.Utils;
using Microsoft.Maui.Storage;
using SkiaSharp;

namespace MALPlus.Services;

public static class ShareCardGenerator
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static SKTypeface _boldTypeface;
    private static SKTypeface _semiBoldTypeface;
    private static SKTypeface _mediumTypeface;
    private static SKTypeface _regularTypeface;
    private static SKBitmap _brandLogoBitmap;
    private static bool _assetsLoaded;

    private static async Task EnsureAssetsLoadedAsync()
    {
        if (_assetsLoaded) return;
        try
        {
            var asm = typeof(ShareCardGenerator).Assembly;

            // 1. Load fonts (try embedded manifest first, then FileSystem package asset)
            _boldTypeface = TryLoadTypeface(asm, "inter_bold.ttf") 
                ?? await TryLoadTypefaceFromPackageAsync("inter_bold.ttf").ConfigureAwait(false);

            _semiBoldTypeface = TryLoadTypeface(asm, "inter_semibold.ttf") 
                ?? await TryLoadTypefaceFromPackageAsync("inter_semibold.ttf").ConfigureAwait(false);

            _mediumTypeface = TryLoadTypeface(asm, "inter_medium.ttf") 
                ?? await TryLoadTypefaceFromPackageAsync("inter_medium.ttf").ConfigureAwait(false);

            _regularTypeface = TryLoadTypeface(asm, "inter_regular.ttf") 
                ?? await TryLoadTypefaceFromPackageAsync("inter_regular.ttf").ConfigureAwait(false);

            // 2. Load brand wordmark logo
            try
            {
                Stream logoStream = asm.GetManifestResourceStream("MALPlus.Resources.Raw.brand_wordmark_primary.png")
                    ?? asm.GetManifestResourceStream("brand_wordmark_primary.png");

                if (logoStream == null)
                {
                    try
                    {
                        logoStream = await FileSystem.OpenAppPackageFileAsync("brand_wordmark_primary.png").ConfigureAwait(false);
                    }
                    catch { }
                }

                if (logoStream != null)
                {
                    using (logoStream)
                    {
                        var rawLogo = SKBitmap.Decode(logoStream);
                        _brandLogoBitmap = CropTransparentBorders(rawLogo);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS ShareCardGenerator logo load error: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ShareCardGenerator EnsureAssetsLoaded failed: " + ex.Message);
        }
        finally
        {
            _boldTypeface ??= SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold) ?? SKTypeface.Default;
            _semiBoldTypeface ??= SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold) ?? _boldTypeface;
            _mediumTypeface ??= SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal) ?? SKTypeface.Default;
            _regularTypeface ??= SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal) ?? SKTypeface.Default;
            _assetsLoaded = true;
        }
    }

    private static SKTypeface TryLoadTypeface(Assembly asm, string fontName)
    {
        try
        {
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(fontName, StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = asm.GetManifestResourceStream(name);
                    if (stream != null) return SKTypeface.FromStream(stream);
                }
            }
        }
        catch { }
        return null;
    }

    private static async Task<SKTypeface> TryLoadTypefaceFromPackageAsync(string fontName)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(fontName).ConfigureAwait(false);
            if (stream != null) return SKTypeface.FromStream(stream);
        }
        catch { }
        return null;
    }

    public static async Task<string> GenerateCardAsync(ShareCardModel model)
    {
        if (model == null)
            return null;

        await EnsureAssetsLoadedAsync().ConfigureAwait(false);

        const int width = 780;

        // Poster & Layout Measurements
        const float cardBorderPad = 12f;
        const float cardRadius = 30f;
        const float topPad = 32f;

        const float posterW = 500f;
        const float posterH = 710f;
        const float posX = (width - posterW) / 2f; // 140
        const float posY = topPad; // 32

        const float gapAfterPoster = 18f;
        const float pillH = 38f;
        const float gapAfterPill = 16f;

        // Wrap Title lines
        using var titlePaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 33,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = _boldTypeface
        };

        var titleLines = WrapText(model.Title ?? string.Empty, titlePaint, width - 120);
        float titleTotalH = titleLines.Count * 42f;

        const float gapAfterTitle = 12f;
        const float progTextH = 22f;
        const float gapProgBar = 7f;
        const float progBarH = 8f;
        const float gapAfterProg = 18f;

        const float boxH = 100f;
        const float gapAfterBoxes = 22f;
        const float logoH = 38f;
        const float bottomPad = 34f;

        int height = (int)(posY + posterH + gapAfterPoster + pillH + gapAfterPill +
                           titleTotalH + gapAfterTitle + progTextH + gapProgBar +
                           progBarH + gapAfterProg + boxH + gapAfterBoxes + logoH + bottomPad);

        SKBitmap coverBitmap = null;
        var normalizedUrl = MalImageUrl.Normalize(model.ImageUrl);
        if (!string.IsNullOrWhiteSpace(normalizedUrl))
        {
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(normalizedUrl).ConfigureAwait(false);
                if (bytes != null && bytes.Length > 0)
                {
                    coverBitmap = SKBitmap.Decode(bytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS ShareCardGenerator normalized image fetch failed: " + ex.Message);
                if (!string.IsNullOrWhiteSpace(model.ImageUrl) && model.ImageUrl != normalizedUrl)
                {
                    try
                    {
                        var fallbackBytes = await _httpClient.GetByteArrayAsync(model.ImageUrl).ConfigureAwait(false);
                        if (fallbackBytes != null && fallbackBytes.Length > 0)
                        {
                            coverBitmap = SKBitmap.Decode(fallbackBytes);
                        }
                    }
                    catch { }
                }
            }
        }

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // 1. Base dark background (#051522)
        canvas.Clear(new SKColor(0x05, 0x15, 0x22));

        // 2. Blurred cover backdrop + lighter tint (so the anime art colors pop nicely!)
        if (coverBitmap != null)
        {
            try
            {
                using var blurFilter = SKImageFilter.CreateBlur(45, 45);
                using var blurPaint = new SKPaint
                {
                    ImageFilter = blurFilter,
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.Medium
                };
                canvas.DrawBitmap(coverBitmap, new SKRect(0, 0, width, height), blurPaint);
            }
            catch { }

            // Lighter tint overlay (alpha 100 instead of 190) so colors shine through
            using var tintPaint = new SKPaint { Color = new SKColor(0x05, 0x15, 0x22, 100) };
            canvas.DrawRect(new SKRect(0, 0, width, height), tintPaint);

            // Bottom vignette (only darkens the lower ~42% for legibility of score/logo)
            using var gradShader = SKShader.CreateLinearGradient(
                new SKPoint(0, height * 0.58f),
                new SKPoint(0, height),
                new[]
                {
                    new SKColor(0x05, 0x15, 0x22, 0),
                    new SKColor(0x05, 0x15, 0x22, 160)
                },
                null,
                SKShaderTileMode.Clamp);
            using var gradPaint = new SKPaint { Shader = gradShader };
            canvas.DrawRect(new SKRect(0, height * 0.58f, width, height), gradPaint);
        }

        // 3. Collectible Card Outer Double Bezel
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = new SKColor(0x1E, 0x3A, 0x52),
            IsAntialias = true
        };
        canvas.DrawRoundRect(new SKRect(cardBorderPad, cardBorderPad, width - cardBorderPad, height - cardBorderPad), cardRadius, cardRadius, borderPaint);

        const float innerPad = cardBorderPad + 5f;
        using var innerBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = new SKColor(0x2D, 0x50, 0x6E, 140),
            IsAntialias = true
        };
        canvas.DrawRoundRect(new SKRect(innerPad, innerPad, width - innerPad, height - innerPad), cardRadius - 4, cardRadius - 4, innerBorderPaint);

        // 4. Poster Window: Framed Art
        var posterRect = new SKRect(posX, posY, posX + posterW, posY + posterH);

        // Ambient drop shadow
        using (var shadowPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 240),
            ImageFilter = SKImageFilter.CreateDropShadow(0, 8, 22, 22, new SKColor(0, 0, 0, 240)),
            IsAntialias = true
        })
        {
            canvas.DrawRoundRect(posterRect, 18, 18, shadowPaint);
        }

        // Draw cover or placeholder
        if (coverBitmap != null)
        {
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(posterRect, 18, 18), antialias: true);

            float scale = Math.Max(posterRect.Width / coverBitmap.Width, posterRect.Height / coverBitmap.Height);
            float w = coverBitmap.Width * scale;
            float h = coverBitmap.Height * scale;
            float cx = posterRect.Left + (posterRect.Width - w) / 2f;
            float cy = posterRect.Top + (posterRect.Height - h) / 2f;

            using var coverPaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };
            canvas.DrawBitmap(coverBitmap, new SKRect(cx, cy, cx + w, cy + h), coverPaint);

            canvas.Restore();
        }
        else
        {
            using var placeholderPaint = new SKPaint { Color = new SKColor(0x0A, 0x1F, 0x2E), IsAntialias = true };
            canvas.DrawRoundRect(posterRect, 18, 18, placeholderPaint);
        }

        // Poster outline
        using (var posterOutline = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = new SKColor(0xFF, 0xFF, 0xFF, 45),
            IsAntialias = true
        })
        {
            canvas.DrawRoundRect(posterRect, 18, 18, posterOutline);
        }

        coverBitmap?.Dispose();

        // 6. Unified Tags Row (Capsule Pill Style matching Watching, Title Cased!)
        float currentY = posY + posterH + gapAfterPoster;
        var statusLabel = CleanStatusLabel(model.StatusText, model.IsAnime);
        var statusColor = GetStatusColor(statusLabel);

        using var pillFontPaint = new SKPaint
        {
            TextSize = 17,
            IsAntialias = true,
            Typeface = _boldTypeface
        };

        const float padPillX = 18f;
        float wStatus = pillFontPaint.MeasureText(statusLabel) + padPillX * 2f;

        string typeLabel = CleanTypeLabel(model.Type, model.IsAnime);
        float wType = !string.IsNullOrWhiteSpace(typeLabel) ? pillFontPaint.MeasureText(typeLabel) + padPillX * 2f : 0;

        string seasonLabel = CleanSeasonLabel(model.SeasonTag);
        float wSeason = !string.IsNullOrWhiteSpace(seasonLabel) ? pillFontPaint.MeasureText(seasonLabel) + padPillX * 2f : 0;

        const float pillGap = 10f;
        float totalRowW = wStatus + (wType > 0 ? wType + pillGap : 0) + (wSeason > 0 ? wSeason + pillGap : 0);
        float rx = (width - totalRowW) / 2f;

        // Draw 1. Status Pill
        DrawCapsulePill(canvas, rx, currentY, wStatus, pillH, statusColor, null, SKColors.White, statusLabel, pillFontPaint);
        rx += wStatus + pillGap;

        // Draw 2. Type Pill (Same pill shape, dark sheet + border)
        if (wType > 0)
        {
            DrawCapsulePill(canvas, rx, currentY, wType, pillH, new SKColor(0x0A, 0x1F, 0x2E), new SKColor(0x1E, 0x3A, 0x52), new SKColor(0xC8, 0xDA, 0xEB), typeLabel, pillFontPaint);
            rx += wType + pillGap;
        }

        // Draw 3. Season Pill (Same pill shape, dark sheet + border)
        if (wSeason > 0)
        {
            DrawCapsulePill(canvas, rx, currentY, wSeason, pillH, new SKColor(0x0A, 0x1F, 0x2E), new SKColor(0x1E, 0x3A, 0x52), new SKColor(0xC8, 0xDA, 0xEB), seasonLabel, pillFontPaint);
        }

        // 7. Title
        currentY += pillH + gapAfterPill;
        foreach (var line in titleLines)
        {
            canvas.DrawText(line, width / 2f, currentY + 30, titlePaint);
            currentY += 42f;
        }

        // 8. Episode / Chapter Progress Bar
        currentY += gapAfterTitle;
        const float marginSide = 65f;
        const float contentW = width - marginSide * 2f;

        int watched = model.WatchedEpisodes;
        int total = model.TotalEpisodes;

        // If completed, ensure watched reflects total if known
        if (statusLabel.Equals("Completed", StringComparison.OrdinalIgnoreCase) && total > 0 && (watched == 0 || watched < total))
        {
            watched = total;
        }

        using (var progLblPaint = new SKPaint
        {
            Color = new SKColor(0x8F, 0xA8, 0xC0),
            TextSize = 16,
            IsAntialias = true,
            Typeface = _mediumTypeface
        })
        using (var progValPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 16,
            IsAntialias = true,
            TextAlign = SKTextAlign.Right,
            Typeface = _boldTypeface
        })
        {
            string progTitle = model.IsAnime ? "Episode progress" : "Chapter progress";
            canvas.DrawText(progTitle, marginSide, currentY + 16, progLblPaint);

            string progValStr;
            if (total > 0)
                progValStr = $"{watched} / {total}";
            else if (watched > 0)
                progValStr = $"{watched} {(model.IsAnime ? "eps" : "ch")}";
            else if (statusLabel.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                progValStr = "Completed";
            else
                progValStr = $"{watched} / ?";

            canvas.DrawText(progValStr, width - marginSide, currentY + 16, progValPaint);
        }

        currentY += progTextH + gapProgBar;
        float progressRatio = total > 0
            ? Math.Clamp((float)watched / total, 0f, 1f)
            : (statusLabel.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? 1f : (watched > 0 ? 0.5f : 0f));

        // Progress bar track
        using (var trackPaint = new SKPaint { Color = new SKColor(0x0A, 0x1F, 0x2E), IsAntialias = true })
        using (var trackBorder = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = new SKColor(0x1E, 0x3A, 0x52),
            IsAntialias = true
        })
        {
            var trackRect = new SKRect(marginSide, currentY, marginSide + contentW, currentY + progBarH);
            canvas.DrawRoundRect(trackRect, 4, 4, trackPaint);
            canvas.DrawRoundRect(trackRect, 4, 4, trackBorder);

            // Progress bar fill
            float fillW = Math.Max(progBarH, contentW * progressRatio);
            if (progressRatio > 0)
            {
                SKColor fillCol = statusColor.Alpha > 0 ? statusColor : new SKColor(0x00, 0x66, 0xFF);
                using var fillPaint = new SKPaint { Color = fillCol, IsAntialias = true };
                var fillRect = new SKRect(marginSide, currentY, Math.Min(marginSide + fillW, marginSide + contentW), currentY + progBarH);
                canvas.DrawRoundRect(fillRect, 4, 4, fillPaint);
            }
        }

        // 9. Score Area: DYNAMIC
        // If user scored > 0: Dual boxes side-by-side
        // If user has NOT scored: Single centered Community box!
        currentY += progBarH + gapAfterProg;

        using var boxBgPaint = new SKPaint { Color = new SKColor(0x0A, 0x1F, 0x2E, 230), IsAntialias = true };
        using var boxBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = new SKColor(0x1E, 0x3A, 0x52),
            IsAntialias = true
        };
        using var scoreNumPaint = new SKPaint
        {
            TextSize = 36,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = _boldTypeface
        };
        using var scoreLblPaint = new SKPaint
        {
            Color = new SKColor(0x8F, 0xA8, 0xC0),
            TextSize = 15,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = _mediumTypeface
        };

        bool hasUserScore = model.Score > 0;

        if (hasUserScore)
        {
            const float boxGap = 14f;
            float boxW = (contentW - boxGap) / 2f;

            // Box 1: My Score
            var b1Rect = new SKRect(marginSide, currentY, marginSide + boxW, currentY + boxH);
            canvas.DrawRoundRect(b1Rect, 14, 14, boxBgPaint);
            canvas.DrawRoundRect(b1Rect, 14, 14, boxBorderPaint);

            scoreNumPaint.Color = new SKColor(0xFB, 0xBF, 0x24); // Gold
            string myScoreStr = $"★ {model.Score}";
            canvas.DrawText(myScoreStr, b1Rect.MidX, currentY + 44, scoreNumPaint);
            canvas.DrawText("My score", b1Rect.MidX, currentY + 76, scoreLblPaint);

            // Box 2: Community
            float b2X = marginSide + boxW + boxGap;
            var b2Rect = new SKRect(b2X, currentY, b2X + boxW, currentY + boxH);
            canvas.DrawRoundRect(b2Rect, 14, 14, boxBgPaint);
            canvas.DrawRoundRect(b2Rect, 14, 14, boxBorderPaint);

            scoreNumPaint.Color = new SKColor(0x10, 0xB9, 0x81); // Emerald
            string commStr = model.GlobalScore > 0 ? $"{model.GlobalScore:F2}" : "N/A";
            canvas.DrawText(commStr, b2Rect.MidX, currentY + 44, scoreNumPaint);
            canvas.DrawText("Community", b2Rect.MidX, currentY + 76, scoreLblPaint);
        }
        else
        {
            // Single Centered Community Box
            const float singleW = 340f;
            float singleX = (width - singleW) / 2f;
            var singleRect = new SKRect(singleX, currentY, singleX + singleW, currentY + boxH);

            canvas.DrawRoundRect(singleRect, 14, 14, boxBgPaint);
            canvas.DrawRoundRect(singleRect, 14, 14, boxBorderPaint);

            scoreNumPaint.Color = new SKColor(0x10, 0xB9, 0x81); // Emerald
            string commStr = model.GlobalScore > 0 ? $"★ {model.GlobalScore:F2}" : "N/A";
            canvas.DrawText(commStr, singleRect.MidX, currentY + 44, scoreNumPaint);
            canvas.DrawText("MAL Community Score", singleRect.MidX, currentY + 76, scoreLblPaint);
        }

        // 10. Official MAL+ Brand Wordmark Logo at bottom
        currentY += boxH + gapAfterBoxes;
        if (_brandLogoBitmap != null)
        {
            float maxLogoW = 180f;
            float maxLogoH = logoH;
            float scale = Math.Min(maxLogoW / _brandLogoBitmap.Width, maxLogoH / _brandLogoBitmap.Height);
            float nw = _brandLogoBitmap.Width * scale;
            float nh = _brandLogoBitmap.Height * scale;
            float lx = (width - nw) / 2f;
            var logoRect = new SKRect(lx, currentY, lx + nw, currentY + nh);
            using var logoBmpPaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };
            canvas.DrawBitmap(_brandLogoBitmap, logoRect, logoBmpPaint);
        }
        else
        {
            using var logoPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0x66, 0xFF),
                TextSize = 28,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                Typeface = _boldTypeface
            };
            canvas.DrawText("MAL+", width / 2f, currentY + 26, logoPaint);
        }

        // 11. Encode to PNG file
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var cacheDir = FileSystem.CacheDirectory;
        var filePath = Path.Combine(cacheDir, $"share_card_{model.Id}_{DateTime.UtcNow.Ticks}.png");
        await using var fileStream = File.OpenWrite(filePath);
        data.SaveTo(fileStream);

        return filePath;
    }

    private static void DrawCapsulePill(SKCanvas canvas, float x, float y, float w, float h, SKColor bg, SKColor? border, SKColor textCol, string text, SKPaint fontPaint)
    {
        float radius = h / 2f;
        var rect = new SKRect(x, y, x + w, y + h);

        using (var bgPaint = new SKPaint { Color = bg, IsAntialias = true })
        {
            canvas.DrawRoundRect(rect, radius, radius, bgPaint);
        }

        if (border.HasValue)
        {
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                Color = border.Value,
                IsAntialias = true
            };
            canvas.DrawRoundRect(rect, radius, radius, borderPaint);
        }

        using var txtPaint = new SKPaint
        {
            Color = textCol,
            TextSize = fontPaint.TextSize,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = fontPaint.Typeface
        };
        canvas.DrawText(text, rect.MidX, rect.MidY + 6, txtPaint);
    }

    private static string CleanStatusLabel(string status, bool isAnime)
    {
        if (string.IsNullOrWhiteSpace(status))
            return isAnime ? "Watching" : "Reading";

        var s = status.Trim();
        if (s.Equals("completed", StringComparison.OrdinalIgnoreCase))
            return "Completed";
        if (s.Equals("watching", StringComparison.OrdinalIgnoreCase) || s.Equals("started watching", StringComparison.OrdinalIgnoreCase))
            return "Watching";
        if (s.Equals("reading", StringComparison.OrdinalIgnoreCase) || s.Equals("started reading", StringComparison.OrdinalIgnoreCase))
            return "Reading";
        if (s.Contains("hold", StringComparison.OrdinalIgnoreCase))
            return "On-Hold";
        if (s.Contains("drop", StringComparison.OrdinalIgnoreCase))
            return "Dropped";
        if (s.Contains("plan", StringComparison.OrdinalIgnoreCase) || s.Contains("ptw", StringComparison.OrdinalIgnoreCase) || s.Contains("ptr", StringComparison.OrdinalIgnoreCase))
            return "Plan to Watch";
        if (s.Contains("rewatch", StringComparison.OrdinalIgnoreCase))
            return "Rewatching";
        if (s.Contains("reread", StringComparison.OrdinalIgnoreCase))
            return "Rereading";

        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    private static SKColor GetStatusColor(string status)
    {
        var s = (status ?? "").ToLowerInvariant();
        if (s.Contains("completed"))
            return new SKColor(0x10, 0xB9, 0x81); // #10B981 Emerald
        if (s.Contains("rewatch") || s.Contains("reread"))
            return new SKColor(0x8B, 0x5C, 0xF6); // #8B5CF6 Purple
        if (s.Contains("watch") || s.Contains("read"))
            return new SKColor(0x00, 0x66, 0xFF); // #0066FF Electric Blue
        if (s.Contains("drop"))
            return new SKColor(0xEF, 0x44, 0x44); // #EF4444 Red
        if (s.Contains("hold") || s.Contains("plan"))
            return new SKColor(0xF5, 0x9E, 0x0B); // #F59E0B Amber

        return new SKColor(0x00, 0x66, 0xFF);
    }

    private static string CleanTypeLabel(string type, bool isAnime)
    {
        if (string.IsNullOrWhiteSpace(type))
            return isAnime ? "TV" : "Manga";

        var t = type.Trim();
        if (t.Equals("anime", StringComparison.OrdinalIgnoreCase))
            return "TV";
        return t.ToUpperInvariant();
    }

    private static string CleanSeasonLabel(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
            return null;

        return season.Trim();
    }

    private static SKBitmap CropTransparentBorders(SKBitmap source)
    {
        if (source == null) return null;
        int minX = source.Width, minY = source.Height, maxX = 0, maxY = 0;
        bool found = false;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                if (color.Alpha > 10)
                {
                    found = true;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (!found || minX > maxX || minY > maxY)
            return source;

        int cropW = maxX - minX + 1;
        int cropH = maxY - minY + 1;
        var cropped = new SKBitmap(cropW, cropH);
        source.ExtractSubset(cropped, new SKRectI(minX, minY, minX + cropW, minY + cropH));
        return cropped;
    }

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;

        var words = text.Split(' ');
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
            if (paint.MeasureText(testLine) <= maxWidth)
            {
                currentLine = testLine;
            }
            else
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                    if (lines.Count >= 2)
                        break;
                }
                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine) && lines.Count < 2)
        {
            lines.Add(currentLine);
        }

        if (lines.Count == 2 && words.Length > 2)
        {
            var last = lines[1];
            if (paint.MeasureText(last + "...") > maxWidth)
            {
                while (last.Length > 0 && paint.MeasureText(last + "...") > maxWidth)
                    last = last[..^1];
                lines[1] = last + "...";
            }
        }

        return lines;
    }
}
