using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RadialCommandWheel.Core;

namespace RadialCommandWheel.Services;

public static class ColorSuggestionService
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static readonly Color Fallback = Color.FromRgb(76, 201, 240);

    public static Color ResolveItemColor(WheelItem item, ThemeDefinition theme)
    {
        if (item.ColorMode == ItemColorMode.Manual &&
            ConfigurationValidator.TryNormalizeHexColor(item.Color, out var manual))
        {
            return (Color)ColorConverter.ConvertFromString(manual);
        }

        return Suggest(item, theme);
    }

    public static Color Suggest(WheelItem item, ThemeDefinition theme)
    {
        var key = BuildCacheKey(item, theme);
        var hex = Cache.GetOrAdd(key, _ =>
        {
            var icon = IconService.Resolve(item);
            var analyzed = icon.Image is null ? null : AnalyzeImage(icon.Image);
            return ToHex(EnsureThemeContrast(analyzed, theme));
        });
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    public static string SuggestHex(WheelItem item, ThemeDefinition theme) =>
        ToHex(Suggest(item, theme));

    public static void Refresh(WheelItem item, ThemeDefinition theme)
    {
        Cache.TryRemove(BuildCacheKey(item, theme), out _);
        IconService.ClearCache();
    }

    public static Color? AnalyzeImage(ImageSource image)
    {
        const int sampleSize = 32;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, sampleSize, sampleSize));
            context.DrawImage(image, new Rect(0, 0, sampleSize, sampleSize));
        }

        var rendered = new RenderTargetBitmap(sampleSize, sampleSize, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        var stride = sampleSize * 4;
        var pixels = new byte[stride * sampleSize];
        rendered.CopyPixels(pixels, stride, 0);

        var buckets = new Dictionary<int, HueBucket>();
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            if (alpha < 80)
            {
                continue;
            }

            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            RgbToHsl(red, green, blue, out var hue, out var saturation, out var lightness);
            if (saturation < 0.24 || lightness is < 0.12 or > 0.90)
            {
                continue;
            }

            var bucketIndex = (int)(hue / 20) % 18;
            if (!buckets.TryGetValue(bucketIndex, out var bucket))
            {
                bucket = new HueBucket();
                buckets[bucketIndex] = bucket;
            }

            var lightnessPreference = 1.0 - Math.Min(0.8, Math.Abs(lightness - 0.58));
            var weight = saturation * lightnessPreference;
            bucket.Weight += weight;
            bucket.Hue += hue * weight;
            bucket.Saturation += saturation * weight;
            bucket.Lightness += lightness * weight;
        }

        var winner = buckets.Values.OrderByDescending(bucket => bucket.Weight).FirstOrDefault();
        if (winner is null || winner.Weight <= 0.01)
        {
            return null;
        }

        return FromHsl(
            winner.Hue / winner.Weight,
            winner.Saturation / winner.Weight,
            winner.Lightness / winner.Weight);
    }

    private static Color EnsureThemeContrast(Color? analyzed, ThemeDefinition theme)
    {
        var candidate = analyzed ?? ParseColor(theme.Accent, Fallback);
        if (analyzed is null)
        {
            RgbToHsl(candidate.R, candidate.G, candidate.B, out _, out var themeSaturation,
                out var themeLightness);
            if (themeSaturation < 0.20 || themeLightness is < 0.22 or > 0.82)
            {
                candidate = Fallback;
            }
        }

        RgbToHsl(candidate.R, candidate.G, candidate.B, out var hue, out var saturation, out _);
        var background = ParseColor(theme.Background, Color.FromRgb(9, 13, 20));
        RgbToHsl(background.R, background.G, background.B, out _, out _, out var backgroundLightness);
        var lightness = backgroundLightness < 0.55 ? 0.62 : 0.36;
        return FromHsl(hue, Math.Clamp(Math.Max(saturation, 0.58), 0.58, 0.86), lightness);
    }

    private static string BuildCacheKey(WheelItem item, ThemeDefinition theme)
    {
        var iconPath = item.IconSource == IconSourceKind.File ? item.IconValue : item.Target;
        var stamp = "";
        try
        {
            if (File.Exists(iconPath))
            {
                stamp = File.GetLastWriteTimeUtc(iconPath).Ticks.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            stamp = "unavailable";
        }

        return string.Join('|', item.Type, item.IconSource, item.IconValue, iconPath, stamp,
            theme.Background, theme.Text);
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var section = hue / 60;
        var x = chroma * (1 - Math.Abs(section % 2 - 1));
        (var red1, var green1, var blue1) = section switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        var match = lightness - chroma / 2;
        return Color.FromRgb(
            (byte)Math.Round((red1 + match) * 255),
            (byte)Math.Round((green1 + match) * 255),
            (byte)Math.Round((blue1 + match) * 255));
    }

    private static void RgbToHsl(byte redByte, byte greenByte, byte blueByte,
        out double hue, out double saturation, out double lightness)
    {
        var red = redByte / 255.0;
        var green = greenByte / 255.0;
        var blue = blueByte / 255.0;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        lightness = (maximum + minimum) / 2;
        if (delta <= 0.0001)
        {
            hue = 0;
            saturation = 0;
            return;
        }

        saturation = delta / (1 - Math.Abs(2 * lightness - 1));
        if (maximum == red)
        {
            hue = 60 * (((green - blue) / delta) % 6);
        }
        else if (maximum == green)
        {
            hue = 60 * ((blue - red) / delta + 2);
        }
        else
        {
            hue = 60 * ((red - green) / delta + 4);
        }
    }

    private sealed class HueBucket
    {
        public double Weight { get; set; }
        public double Hue { get; set; }
        public double Saturation { get; set; }
        public double Lightness { get; set; }
    }
}
