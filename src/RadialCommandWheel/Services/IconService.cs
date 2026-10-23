using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using RadialCommandWheel.Core;
using DrawingIcon = System.Drawing.Icon;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingSolidBrush = System.Drawing.SolidBrush;
using DrawingImage = System.Windows.Media.DrawingImage;
using WpfBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace RadialCommandWheel.Services;

public sealed record ItemIcon(ImageSource? Image, string Glyph, string? Error, string? ErrorKey = null);

public static class IconService
{
    private static readonly ConcurrentDictionary<string, ItemIcon> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ItemIcon Resolve(WheelItem item)
    {
        var key = BuildCacheKey(item);
        return Cache.GetOrAdd(key, _ => ResolveCore(item));
    }

    public static string TypeGlyph(WheelItemType type) => type switch
    {
        WheelItemType.Application => "\uE768",
        WheelItemType.File => "\uE8A5",
        WheelItemType.Folder => "\uE8B7",
        WheelItemType.Command => "\uE756",
        WheelItemType.Group => "\uE8B7",
        _ => "\uE783"
    };

    public static string BuiltInGlyph(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "launch" or "play" => "\uE768",
        "file" => "\uE8A5",
        "folder" => "\uE8B7",
        "terminal" or "code" => "\uE756",
        "game" => "\uE7FC",
        "browser" or "web" => "\uE774",
        "settings" or "gear" => "\uE713",
        "star" or "favorite" => "\uE734",
        "tool" or "tools" => "\uE90F",
        "music" => "\uE8D6",
        "video" => "\uE714",
        "shield" or "security" => "\uE72E",
        _ => "\uE783"
    };

    public static IReadOnlyList<string> BuiltInIconNames { get; } =
    [
        "launch", "file", "folder", "terminal", "game", "browser", "settings",
        "star", "tool", "music", "video", "shield"
    ];

    public static void ClearCache(string? target = null)
    {
        if (target is null)
        {
            Cache.Clear();
            return;
        }

        foreach (var key in Cache.Keys.Where(key => key.Contains(target, StringComparison.OrdinalIgnoreCase)))
        {
            Cache.TryRemove(key, out _);
        }
    }

    public static ImageSource? LoadSvg(string path, out string? error)
    {
        error = null;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2_000_000
            };
            using var reader = XmlReader.Create(path, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null || root.Name.LocalName != "svg")
            {
                error = "The SVG file does not have an <svg> root element.";
                return null;
            }

            foreach (var element in document.Descendants())
            {
                var name = element.Name.LocalName.ToLowerInvariant();
                if (name is "script" or "foreignobject" or "image" or "iframe" or "object" or "embed")
                {
                    error = $"The SVG contains unsupported active content (<{name}>).";
                    return null;
                }

                foreach (var attribute in element.Attributes())
                {
                    var attributeName = attribute.Name.LocalName.ToLowerInvariant();
                    var value = attribute.Value.Trim();
                    if ((attributeName is "href" or "src" || attributeName.StartsWith("on", StringComparison.Ordinal)) ||
                        value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "The SVG references external or scriptable content and was rejected.";
                        return null;
                    }
                }
            }

            var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
            var group = new DrawingGroup
            {
                ClipGeometry = new RectangleGeometry(viewBox)
            };
            using (var context = group.Open())
            {
                context.PushClip(new RectangleGeometry(viewBox));
                context.PushTransform(new MatrixTransform(1, 0, 0, 1, -viewBox.X, -viewBox.Y));
                AddSvgElement(root, context, null);
                context.Pop();
                context.Pop();
            }

            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException
                                               or FormatException or XmlSchemaException
                                               or System.Windows.Markup.XamlParseException)
        {
            error = $"Invalid SVG: {exception.Message}";
            return null;
        }
    }

    public static ImageSource? LoadRaster(string path, out string? error)
    {
        error = null;
        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".ico" or ".bmp"))
            {
                error = "Supported icon formats are PNG, JPG, ICO, BMP, and safe SVG.";
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            if (image.PixelWidth is <= 0 or > 8192 || image.PixelHeight is <= 0 or > 8192)
            {
                error = "The image dimensions are invalid or too large.";
                return null;
            }

            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException
                                               or UnauthorizedAccessException or UriFormatException
                                               or System.Windows.Markup.XamlParseException)
        {
            error = $"The image could not be loaded: {exception.Message}";
            return null;
        }
    }

    private static ItemIcon ResolveCore(WheelItem item)
    {
        try
        {
            if (item.IconSource == IconSourceKind.BuiltIn)
            {
                return new ItemIcon(null, BuiltInGlyph(item.IconValue), null);
            }

            if (item.IconSource == IconSourceKind.File)
            {
                if (!File.Exists(item.IconValue))
                {
                    return new ItemIcon(null, TypeGlyph(item.Type), $"Icon file not found: {item.IconValue}",
                        "IconFileMissing");
                }

                var extension = Path.GetExtension(item.IconValue).ToLowerInvariant();
                var custom = extension == ".svg"
                    ? LoadSvg(item.IconValue, out var svgError)
                    : LoadRaster(item.IconValue, out svgError);
                var errorKey = extension == ".svg"
                    ? svgError?.Contains("root element", StringComparison.OrdinalIgnoreCase) == true
                        ? "IconSvgRoot"
                        : svgError?.Contains("active content", StringComparison.OrdinalIgnoreCase) == true
                            ? "IconSvgActive"
                            : svgError?.Contains("external or scriptable", StringComparison.OrdinalIgnoreCase) == true
                                ? "IconSvgExternal"
                                : "IconSvgInvalid"
                    : "IconLoadFailed";
                return custom is null
                    ? new ItemIcon(null, TypeGlyph(item.Type), svgError ?? "The icon could not be rendered.",
                        errorKey)
                    : new ItemIcon(custom, TypeGlyph(item.Type), null);
            }

            if (item.Type == WheelItemType.Group || string.IsNullOrWhiteSpace(item.Target))
            {
                return new ItemIcon(null, TypeGlyph(item.Type), null);
            }

            if (!File.Exists(item.Target) && !Directory.Exists(item.Target))
            {
                if (Path.IsPathFullyQualified(item.Target))
                {
                    return new ItemIcon(null, TypeGlyph(item.Type),
                        $"Target not found; using the default icon: {item.Target}", "IconTargetMissing");
                }

                return new ItemIcon(null, TypeGlyph(item.Type), null);
            }

            var icon = GetShellIcon(item.Target);
            return icon is null
                ? new ItemIcon(null, TypeGlyph(item.Type), "Windows did not return an icon for this target.",
                    "IconShellUnavailable")
                : new ItemIcon(icon, TypeGlyph(item.Type), null);
        }
        catch (Exception exception)
        {
            return new ItemIcon(null, TypeGlyph(item.Type), $"Icon unavailable: {exception.Message}",
                "IconUnavailable");
        }
    }

    private static ImageSource? GetShellIcon(string path)
    {
        const uint shgfiIcon = 0x000000100;
        const uint shgfiUseFileAttributes = 0x000000010;
        var fileInfo = new ShellFileInfo();
        var result = SHGetFileInfo(path, 0, ref fileInfo, (uint)Marshal.SizeOf<ShellFileInfo>(),
            shgfiIcon | shgfiUseFileAttributes);
        if (result == IntPtr.Zero || fileInfo.IconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(fileInfo.IconHandle, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(fileInfo.IconHandle);
        }
    }

    private static void AddSvgElement(XElement element, DrawingContext context, Transform? parentTransform)
    {
        var transform = parentTransform;
        var transformText = element.Attribute("transform")?.Value;
        if (!string.IsNullOrWhiteSpace(transformText))
        {
            var current = Transform.Parse(transformText);
            if (transform is null)
            {
                transform = current;
            }
            else
            {
                var combined = new TransformGroup();
                combined.Children.Add(transform);
                combined.Children.Add(current);
                transform = combined;
            }
        }

        if (transform is not null)
        {
            context.PushTransform(transform);
        }

        var name = element.Name.LocalName.ToLowerInvariant();
        if (name == "g" || name == "svg")
        {
            foreach (var child in element.Elements())
            {
                AddSvgElement(child, context, null);
            }
        }
        else
        {
            var geometry = CreateSvgGeometry(element, name);
            if (geometry is not null)
            {
                var fill = ParseBrush(element.Attribute("fill")?.Value, true);
                var stroke = ParseBrush(element.Attribute("stroke")?.Value, false);
                var penWidth = ParseDouble(element.Attribute("stroke-width")?.Value, 1.0);
                var drawing = new GeometryDrawing(fill,
                    stroke is null ? null : new System.Windows.Media.Pen(stroke, penWidth), geometry);
                if (drawing.CanFreeze)
                {
                    drawing.Freeze();
                }

                context.DrawDrawing(drawing);
            }
        }

        if (transform is not null)
        {
            context.Pop();
        }
    }

    private static Geometry? CreateSvgGeometry(XElement element, string name)
    {
        return name switch
        {
            "path" when !string.IsNullOrWhiteSpace(element.Attribute("d")?.Value) =>
                Geometry.Parse(element.Attribute("d")!.Value),
            "rect" => new RectangleGeometry(new WpfRect(
                ParseDouble(element.Attribute("x")?.Value, 0),
                ParseDouble(element.Attribute("y")?.Value, 0),
                ParseDouble(element.Attribute("width")?.Value, 0),
                ParseDouble(element.Attribute("height")?.Value, 0)),
                ParseDouble(element.Attribute("rx")?.Value, 0),
                ParseDouble(element.Attribute("ry")?.Value, 0)),
            "circle" => new EllipseGeometry(new WpfPoint(
                    ParseDouble(element.Attribute("cx")?.Value, 0),
                    ParseDouble(element.Attribute("cy")?.Value, 0)),
                ParseDouble(element.Attribute("r")?.Value, 0),
                ParseDouble(element.Attribute("r")?.Value, 0)),
            "ellipse" => new EllipseGeometry(new WpfPoint(
                    ParseDouble(element.Attribute("cx")?.Value, 0),
                    ParseDouble(element.Attribute("cy")?.Value, 0)),
                ParseDouble(element.Attribute("rx")?.Value, 0),
                ParseDouble(element.Attribute("ry")?.Value, 0)),
            "line" => new LineGeometry(new WpfPoint(
                    ParseDouble(element.Attribute("x1")?.Value, 0),
                    ParseDouble(element.Attribute("y1")?.Value, 0)),
                new WpfPoint(
                    ParseDouble(element.Attribute("x2")?.Value, 0),
                    ParseDouble(element.Attribute("y2")?.Value, 0))),
            "polygon" or "polyline" => CreatePolygon(element, name == "polygon"),
            _ => null
        };
    }

    private static Geometry CreatePolygon(XElement element, bool closed)
    {
        var values = (element.Attribute("points")?.Value ?? "")
            .Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length < 4 || values.Length % 2 != 0)
        {
            throw new FormatException("An SVG polygon has invalid points.");
        }

        var figure = new PathFigure
        {
            StartPoint = new WpfPoint(values[0], values[1]),
            IsClosed = closed,
            IsFilled = closed
        };
        for (var index = 2; index < values.Length; index += 2)
        {
            figure.Segments.Add(new LineSegment(new WpfPoint(values[index], values[index + 1]), true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static WpfBrush? ParseBrush(string? value, bool defaultToBlack)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultToBlack ? Brushes.Black : null;
        }

        if (string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var brush = (WpfBrush?)new BrushConverter().ConvertFromInvariantString(value);
        brush?.Freeze();
        return brush;
    }

    private static WpfRect ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new WpfRect(0, 0, 24, 24);
        }

        var parts = value.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
        if (parts.Length != 4 || parts[2] <= 0 || parts[3] <= 0)
        {
            throw new FormatException("The SVG viewBox must have four values and positive width and height.");
        }

        return new WpfRect(parts[0], parts[1], parts[2], parts[3]);
    }

    private static double ParseDouble(string? value, double fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : double.Parse(value.Trim().TrimEnd('p', 'x'), CultureInfo.InvariantCulture);

    private static string BuildCacheKey(WheelItem item)
    {
        var detail = item.IconSource == IconSourceKind.BuiltIn ? item.IconValue : item.IconValue;
        var path = item.IconSource == IconSourceKind.File ? item.IconValue : item.Target;
        var stamp = "";
        try
        {
            if (File.Exists(path))
            {
                stamp = File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            stamp = "unknown";
        }

        return $"{item.Type}|{item.IconSource}|{detail}|{path}|{stamp}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShellFileInfo fileInfo,
        uint fileInfoSize, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}

public static class TrayIconFactory
{
    public static DrawingIcon Create()
    {
        using var bitmap = new DrawingBitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = DrawingGraphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(DrawingColor.Transparent);
            using var background = new DrawingSolidBrush(DrawingColor.FromArgb(255, 9, 13, 20));
            graphics.FillEllipse(background, 1, 1, 30, 30);
            var colors = new[]
            {
                DrawingColor.FromArgb(255, 76, 201, 240),
                DrawingColor.FromArgb(255, 147, 112, 219),
                DrawingColor.FromArgb(255, 107, 203, 119),
                DrawingColor.FromArgb(255, 255, 209, 102)
            };
            for (var index = 0; index < colors.Length; index++)
            {
                using var brush = new DrawingSolidBrush(colors[index]);
                graphics.FillPie(brush, 3, 3, 26, 26, -90 + index * 90 + 3, 84);
            }

            using var center = new DrawingSolidBrush(DrawingColor.FromArgb(255, 9, 13, 20));
            graphics.FillEllipse(center, 11, 11, 10, 10);
            using var centerDot = new DrawingSolidBrush(DrawingColor.FromArgb(255, 245, 248, 252));
            graphics.FillEllipse(centerDot, 14, 14, 4, 4);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = DrawingIcon.FromHandle(handle);
            return (DrawingIcon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}

