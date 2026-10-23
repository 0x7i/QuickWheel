using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RadialCommandWheel.Tools;

internal static class GenerateIconTool
{
    public static int Run(string[] args)
    {
        var outputPath = Path.GetFullPath(ParseOutputPath(args));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                                  ?? throw new InvalidOperationException("The icon output path has no directory."));

        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(255, 9, 13, 20));
            graphics.FillEllipse(background, 2, 2, 60, 60);

            var colors = new[]
            {
                Color.FromArgb(255, 76, 201, 240),
                Color.FromArgb(255, 147, 112, 219),
                Color.FromArgb(255, 107, 203, 119),
                Color.FromArgb(255, 246, 200, 95)
            };
            for (var index = 0; index < colors.Length; index++)
            {
                using var brush = new SolidBrush(colors[index]);
                graphics.FillPie(brush, 6, 6, 52, 52, -90 + index * 90 + 3, 84);
            }

            using var center = new SolidBrush(Color.FromArgb(255, 9, 13, 20));
            graphics.FillEllipse(center, 22, 22, 20, 20);
            using var dot = new SolidBrush(Color.FromArgb(255, 245, 248, 252));
            graphics.FillEllipse(dot, 28, 28, 8, 8);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            icon.Save(stream);
        }
        finally
        {
            DestroyIcon(handle);
        }

        Console.WriteLine(outputPath);
        return 0;
    }

    private static string ParseOutputPath(string[] args)
    {
        var output = Path.Combine(Environment.CurrentDirectory, "src", "RadialCommandWheel", "assets",
            "app.ico");
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is "--output" or "-o")
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw new ArgumentException("--output requires a path.");
                }

                output = args[index];
            }
            else
            {
                throw new ArgumentException($"Unknown generate-icon option: {args[index]}");
            }
        }

        return output;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}

