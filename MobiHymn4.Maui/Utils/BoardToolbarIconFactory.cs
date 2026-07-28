using System;
using System.Collections.Concurrent;
using System.IO;
using FontAwesome;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using SkiaSharp;

namespace MobiHymn4.Utils;

/// <summary>
/// Builds the reader toolbar board icon, optionally with an unread count badge.
/// </summary>
public static class BoardToolbarIconFactory
{
    const int CanvasSize = 72;
    static SKTypeface faSolid;
    static readonly object FontLock = new();
    static readonly ConcurrentDictionary<string, byte[]> PngCache = new();

    public static ImageSource Create(bool unread, Color iconColor) =>
        Create(unread ? 1 : 0, iconColor);

    public static ImageSource Create(int unreadCount, Color iconColor)
    {
        var key = CacheKey(unreadCount, iconColor);
        var bytes = PngCache.GetOrAdd(key, _ => RenderPng(unreadCount, iconColor));
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    static string CacheKey(int unreadCount, Color iconColor) =>
        $"{unreadCount}|{(int)(iconColor.Red * 255)}|{(int)(iconColor.Green * 255)}|{(int)(iconColor.Blue * 255)}|{(int)(iconColor.Alpha * 255)}";

    static byte[] RenderPng(int unreadCount, Color iconColor)
    {
        using var bitmap = new SKBitmap(CanvasSize, CanvasSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = ToSk(iconColor),
            TextSize = 46,
            Typeface = GetFaSolid(),
            TextAlign = SKTextAlign.Center,
        };

        // fa-clipboard-list
        var glyph = FontAwesomeIcons.ClipboardList;
        var y = CanvasSize * 0.72f;
        canvas.DrawText(glyph, CanvasSize * 0.45f, y, textPaint);

        if (unreadCount > 0)
        {
            var label = unreadCount > 99 ? "99+" : unreadCount.ToString();
            var badgeR = label.Length > 1 ? 14f : 12f;
            var cx = CanvasSize - badgeR - 2f;
            var cy = badgeR + 2f;

            using var ring = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawCircle(cx, cy, badgeR + 2.5f, ring);

            using var badge = new SKPaint
            {
                IsAntialias = true,
                Color = SKColor.Parse("#C62828"),
                Style = SKPaintStyle.Fill,
            };
            canvas.DrawCircle(cx, cy, badgeR, badge);

            using var countPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                TextSize = label.Length > 2 ? 14f : 16f,
                Typeface = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold)
                    ?? SKTypeface.Default,
                TextAlign = SKTextAlign.Center,
            };
            var metrics = countPaint.FontMetrics;
            var textY = cy - (metrics.Ascent + metrics.Descent) / 2f;
            canvas.DrawText(label, cx, textY, countPaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    static SKTypeface GetFaSolid()
    {
        if (faSolid != null)
            return faSolid;

        lock (FontLock)
        {
            if (faSolid != null)
                return faSolid;

            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("faSolid.ttf")
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                faSolid = SKTypeface.FromStream(stream) ?? SKTypeface.Default;
            }
            catch
            {
                faSolid = SKTypeface.Default;
            }

            return faSolid;
        }
    }

    static SKColor ToSk(Color color) =>
        new(
            (byte)(color.Red * 255),
            (byte)(color.Green * 255),
            (byte)(color.Blue * 255),
            (byte)(color.Alpha * 255));
}
