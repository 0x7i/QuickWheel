using RadialCommandWheel.Core;
using RadialCommandWheel.Services;
using RadialCommandWheel.UI;
using WpfPoint = System.Windows.Point;

namespace RadialCommandWheel.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        Console.WriteLine("Radial Command Wheel automated test suite");
        Console.WriteLine("=========================================");

        Run("First launch default configuration is valid", () =>
        {
            var configuration = ConfigurationDefaults.Create();
            var result = ConfigurationValidator.Validate(configuration);
            Assert(result.IsValid, string.Join("; ", result.Errors));
            Assert(configuration.MainWheel.Count > 0, "Default wheel should not be empty.");
            Assert(configuration.Themes.Count >= 4, "Several built-in themes are expected.");
        });

        Run("Friendly display names hide technical extensions without changing targets", () =>
        {
            var urlTarget = @"C:\Games\Call of Duty Black Ops III.url";
            var lnkTarget = @"C:\Games\MyGame.lnk";
            var textTarget = @"C:\Notes\My Notes.txt";
            var dottedTarget = @"C:\Projects\release.v2";
            Assert(DisplayNameHelper.FromTarget(urlTarget, WheelItemType.File) ==
                   "Call of Duty Black Ops III", "URL extension was not hidden.");
            Assert(DisplayNameHelper.FromTarget(lnkTarget, WheelItemType.Application) ==
                   "MyGame", "LNK extension was not hidden.");
            Assert(DisplayNameHelper.FromTarget(textTarget, WheelItemType.File) ==
                   "My Notes", "Text extension was not hidden.");
            Assert(DisplayNameHelper.FromTarget(dottedTarget, WheelItemType.File) ==
                   "release.v2", "A meaningful dot was incorrectly removed.");

            var custom = new WheelItem
            {
                Type = WheelItemType.File,
                Target = textTarget,
                Name = "My deliberately custom.name.txt"
            };
            var automatic = new WheelItem { Type = WheelItemType.File, Target = urlTarget };
            Assert(custom.DisplayCaption == "My deliberately custom.name.txt",
                "A customized display name was modified.");
            Assert(automatic.Target == urlTarget, "The underlying URL target changed.");
            Assert(automatic.DisplayCaption == "Call of Duty Black Ops III",
                "The automatic caption was not cleaned.");
        });

        Run("Main wheel enforces the 12-item limit", () =>
        {
            var configuration = ConfigurationDefaults.Create();
            while (configuration.MainWheel.Count < ConfigurationValidator.MaxWheelItems)
            {
                Assert(ConfigurationValidator.TryAddToMain(configuration,
                    MakeItem(WheelItemType.File, Path.GetTempFileName()), out _), "Add should succeed.");
            }

            var accepted = ConfigurationValidator.TryAddToMain(configuration,
                MakeItem(WheelItemType.File, Path.GetTempFileName()), out var error);
            Assert(!accepted, "The thirteenth item must be rejected.");
            Assert(error.Contains("12", StringComparison.Ordinal), error);
            DeleteTempFiles(configuration);
        });

        Run("Groups reject nested groups and accept launch items", () =>
        {
            var group = new WheelItem { Type = WheelItemType.Group, Name = "Tools" };
            var nested = new WheelItem { Type = WheelItemType.Group, Name = "Nested" };
            var acceptedNested = ConfigurationValidator.TryAdd(group, nested, out var nestedError);
            Assert(!acceptedNested, "Nested groups must be rejected.");
            Assert(nestedError.Contains("cannot contain", StringComparison.OrdinalIgnoreCase), nestedError);

            var command = MakeItem(WheelItemType.Command, "notepad.exe");
            Assert(ConfigurationValidator.TryAdd(group, command, out var childError), childError);
            Assert(group.Children.Count == 1, "The launch item should be present.");
        });

        Run("Normalization removes nested groups and excess items", () =>
        {
            var configuration = ConfigurationDefaults.Create();
            var group = configuration.Groups.First();
            group.Children.Add(new WheelItem { Type = WheelItemType.Group, Name = "Illegal" });
            while (configuration.MainWheel.Count <= ConfigurationValidator.MaxWheelItems)
            {
                configuration.MainWheel.Add(MakeItem(WheelItemType.Command, "notepad.exe"));
            }

            ConfigurationValidator.Normalize(configuration);
            Assert(configuration.MainWheel.Count == ConfigurationValidator.MaxWheelItems,
                "Excess top-level items were not trimmed.");
            Assert(group.Children.All(child => child.Type != WheelItemType.Group),
                "Nested groups were not removed.");
        });

        Run("Configuration round-trips through atomic storage", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "config.json");
                var store = new ConfigurationStore(path);
                var configuration = ConfigurationDefaults.Create();
                store.Save(configuration);
                var loaded = store.Load();
                Assert(!loaded.RecoveredFromBackup, "A valid primary file should load normally.");
                Assert(loaded.Configuration.MainWheel.Count == configuration.MainWheel.Count,
                    "Wheel item count changed during persistence.");
                Assert(File.Exists(path), "Primary configuration file was not written.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Version 1 configuration migrates with manual colors and English UI", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "config.json");
                var legacy = ConfigurationDefaults.Create();
                legacy.SchemaVersion = 1;
                foreach (var item in ConfigurationValidator.EnumerateAll(legacy.MainWheel))
                {
                    item.ColorMode = ItemColorMode.Manual;
                }
                File.WriteAllText(path, ConfigurationStore.Serialize(legacy));
                var loaded = new ConfigurationStore(path).Load().Configuration;
                Assert(loaded.SchemaVersion == AppConfiguration.CurrentSchemaVersion,
                    "The loaded configuration was not upgraded.");
                Assert(loaded.MainWheel.Count == legacy.MainWheel.Count,
                    "Legacy wheel items were lost.");
                Assert(loaded.MainWheel.All(item => item.ColorMode == ItemColorMode.Manual),
                    "Legacy colors were unexpectedly switched to automatic.");
                Assert(loaded.Settings.Language == AppLanguage.English,
                    "Legacy configuration did not default to English.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Corrupt primary configuration recovers from backup", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "config.json");
                var store = new ConfigurationStore(path);
                var first = ConfigurationDefaults.Create();
                first.MainWheel[0].Name = "Backup marker";
                store.Save(first);
                var second = first.Clone();
                second.MainWheel[0].Name = "Current marker";
                store.Save(second);
                File.WriteAllText(path, "{ this is not valid json");
                var loaded = store.Load();
                Assert(loaded.RecoveredFromBackup, "The loader did not report backup recovery.");
                Assert(loaded.Configuration.MainWheel[0].Name == "Backup marker",
                    "The backup content was not restored.");
                var invalidCopies = Directory.GetFiles(directory, "*.invalid-*");
                Assert(invalidCopies.Length == 1, "The invalid primary file was not preserved.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Undo returns the previous configuration snapshot", () =>
        {
            var configuration = ConfigurationDefaults.Create();
            var history = new UndoHistory();
            history.Record(configuration);
            configuration.MainWheel[0].Name = "Changed";
            var restored = history.Undo();
            Assert(restored is not null, "Undo returned no snapshot.");
            Assert(restored!.MainWheel[0].Name != "Changed", "Undo did not restore the prior value.");
        });

        Run("Custom themes persist with their visual properties", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var configuration = ConfigurationDefaults.Create();
                var custom = configuration.ActiveTheme.Clone();
                custom.Id = "custom-test";
                custom.Name = "Ocean test";
                custom.IsBuiltIn = false;
                custom.Accent = "#22D3EE";
                custom.Opacity = 0.73;
                custom.WheelSize = 410;
                configuration.Themes.Add(custom);
                configuration.ActiveThemeId = custom.Id;
                var store = new ConfigurationStore(Path.Combine(directory, "config.json"));
                store.Save(configuration);
                var loaded = store.Load().Configuration;
                var restored = loaded.Themes.Single(theme => theme.Id == custom.Id);
                Assert(restored.Name == "Ocean test", restored.Name);
                Assert(restored.Accent == "#22D3EE", restored.Accent);
                Assert(Math.Abs(restored.Opacity - 0.73) < 0.001, restored.Opacity.ToString());
                Assert(Math.Abs(restored.WheelSize - 410) < 0.001, restored.WheelSize.ToString());
                Assert(loaded.ActiveThemeId == custom.Id, loaded.ActiveThemeId);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Windows argument parsing preserves quoted spaces and backslashes", () =>
        {
            var arguments = WindowsArgumentParser.Split("\"hello world\" C:\\Temp\\file.txt \"a\\\"b\"");
            Assert(arguments.Count == 3, $"Expected 3 arguments, got {arguments.Count}.");
            Assert(arguments[0] == "hello world", arguments[0]);
            Assert(arguments[1] == "C:\\Temp\\file.txt", arguments[1]);
            Assert(arguments[2] == "a\"b", arguments[2]);
            var roundTrip = WindowsArgumentParser.Split(WindowsArgumentParser.Join(arguments));
            Assert(roundTrip.SequenceEqual(arguments), "Quoted arguments did not round-trip.");
        });

        Run("Shortcut display and validation are deterministic", () =>
        {
            var shortcut = new ShortcutDefinition
            {
                Modifiers = ShortcutModifiers.Control | ShortcutModifiers.Alt,
                VirtualKey = 0x20
            };
            Assert(shortcut.DisplayText == "Ctrl + Alt + Space", shortcut.DisplayText);
            var configuration = ConfigurationDefaults.Create();
            configuration.Shortcut = shortcut;
            Assert(ConfigurationValidator.Validate(configuration).IsValid, "Default shortcut should validate.");
        });

        Run("Missing targets are visible errors but do not lose the configuration", () =>
        {
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");
            var item = MakeItem(WheelItemType.Application, missing);
            Assert(TargetValidator.Validate(item).IsError, "Missing application path was not an error.");
            var configuration = ConfigurationDefaults.Create();
            ConfigurationValidator.TryAddToMain(configuration, item, out _);
            var store = new ConfigurationStore(Path.Combine(CreateTempDirectory(), "config.json"));
            store.Save(configuration);
            var loaded = store.Load();
            Assert(loaded.Configuration.MainWheel.Any(candidate => candidate.Id == item.Id),
                "A pending item with a missing target should survive a reload.");
        });

        Run("Safe SVG renders and scriptable SVG is rejected", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var safePath = Path.Combine(directory, "safe.svg");
                File.WriteAllText(safePath,
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><rect x=\"2\" y=\"2\" width=\"20\" height=\"20\" rx=\"4\" fill=\"#4CC9F0\"/><path d=\"M7 7 L17 17\" stroke=\"#ffffff\" stroke-width=\"2\"/></svg>");
                var safe = IconService.LoadSvg(safePath, out var safeError);
                Assert(safe is not null, safeError ?? "Safe SVG failed to render.");

                var unsafePath = Path.Combine(directory, "unsafe.svg");
                File.WriteAllText(unsafePath,
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><script>alert(1)</script></svg>");
                var unsafeImage = IconService.LoadSvg(unsafePath, out var unsafeError);
                Assert(unsafeImage is null, "Scriptable SVG must be rejected.");
                Assert(unsafeError?.Contains("active content", StringComparison.OrdinalIgnoreCase) == true,
                    unsafeError ?? "Rejection did not explain the problem.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Invalid custom image falls back to the generic item icon", () =>
        {
            var invalidPath = Path.Combine(CreateTempDirectory(), "broken.png");
            File.WriteAllText(invalidPath, "not an image");
            var item = new WheelItem
            {
                Type = WheelItemType.File,
                IconSource = IconSourceKind.File,
                IconValue = invalidPath
            };
            var icon = IconService.Resolve(item);
            Assert(icon.Image is null, "Invalid image unexpectedly produced an icon.");
            Assert(!string.IsNullOrWhiteSpace(icon.Glyph), "Fallback glyph was missing.");
            Assert(icon.Error is not null, "Icon failure was not reported.");
        });

        Run("Icon color analysis ignores unsuitable pixels and remains stable", () =>
        {
            var bitmap = CreateColorBitmap(
                System.Windows.Media.Colors.White,
                System.Windows.Media.Color.FromRgb(32, 145, 225));
            var suggestion = ColorSuggestionService.AnalyzeImage(bitmap);
            Assert(suggestion is not null, "A dominant blue accent should be detected.");
            Assert(suggestion!.Value.B > suggestion.Value.R,
                $"The significant blue color was not preferred: {suggestion}");

            var directory = CreateTempDirectory();
            try
            {
                var iconPath = Path.Combine(directory, "blue.svg");
                File.WriteAllText(iconPath,
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><rect width=\"24\" height=\"24\" fill=\"#2091E1\"/></svg>");
                var item = new WheelItem
                {
                    Type = WheelItemType.File,
                    Target = Path.Combine(directory, "launch.dat"),
                    IconSource = IconSourceKind.File,
                    IconValue = iconPath,
                    ColorMode = ItemColorMode.Automatic
                };
                var theme = ConfigurationDefaults.Create().ActiveTheme;
                var first = ColorSuggestionService.SuggestHex(item, theme);
                var second = ColorSuggestionService.SuggestHex(item, theme);
                Assert(first == second, "Automatic color suggestions were not stable.");
                Assert(first != "#FFFFFF" && first != "#000000",
                    "An unsuitable monochrome accent was selected.");

                item.ColorMode = ItemColorMode.Manual;
                item.Color = "#FF0000";
                Assert(ColorSuggestionService.ResolveItemColor(item, theme) ==
                       System.Windows.Media.Color.FromRgb(255, 0, 0),
                    "A manual color override was replaced.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Sector hit testing and center back navigation are correct", () =>
        {
            var center = new WpfPoint(100, 100);
            Assert(RadialWheelLayout.HitTest(new WpfPoint(100, 0), center, 150, 4, false).Index == 0,
                "Top sector should be first.");
            Assert(RadialWheelLayout.HitTest(new WpfPoint(200, 100), center, 150, 4, false).Index == 1,
                "Right sector should be second.");
            Assert(RadialWheelLayout.HitTest(center, center, 150, 4, true).Kind == WheelHitKind.Back,
                "Center should be the back target.");
            Assert(RadialWheelLayout.HitTest(new WpfPoint(300, 300), center, 150, 4, true).Kind ==
                   WheelHitKind.Outside, "Distant point should be outside.");
        });

        Run("Adaptive geometry and hit regions work for 1, 2, 3, 4, and 12 items", () =>
        {
            var center = new WpfPoint(200, 200);
            foreach (var count in new[] { 1, 2, 3, 4, 12 })
            {
                var metrics = RadialWheelLayout.CalculateMetrics(150, count);
                Assert(metrics.SweepDegrees * count == 360, $"Sector angles do not close for {count}.");
                Assert(metrics.InnerRadius < metrics.IconRadius &&
                       metrics.IconRadius < metrics.OuterRadius, "Icon radius is outside the sector.");
                if (count == 1)
                {
                    Assert(metrics.IsSingleItem && metrics.GapDegrees == 0,
                        "One-item wheel does not use the complete-ring rule.");
                    Assert(RadialWheelLayout.HitTest(new WpfPoint(200, 60), center, 150, count, false).Index == 0,
                        "Single-item ring is not selectable.");
                    continue;
                }

                var radius = (metrics.InnerRadius + metrics.OuterRadius) / 2;
                for (var index = 0; index < count; index++)
                {
                    var angle = (-90 + (index + 0.5) * metrics.SweepDegrees) * Math.PI / 180;
                    var point = new WpfPoint(center.X + radius * Math.Cos(angle),
                        center.Y + radius * Math.Sin(angle));
                    Assert(RadialWheelLayout.HitTest(point, center, 150, count, false).Index == index,
                        $"Sector {index} hit test failed for {count} items.");
                }
            }

            var control = new RadialWheelControl
            {
                Items = [new WheelItem { Type = WheelItemType.Application, Name = "Solo", Target = "notepad.exe" }],
                Theme = ConfigurationDefaults.Create().ActiveTheme,
                DesiredDiameter = 280,
                IsOverlay = true
            };
            control.Measure(new System.Windows.Size(300, 300));
            control.Arrange(new System.Windows.Rect(0, 0, 300, 300));
            control.UpdateLayout();
            var render = new System.Windows.Media.Imaging.RenderTargetBitmap(300, 300, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            render.Render(control);
            var pixels = new byte[300 * 300 * 4];
            render.CopyPixels(pixels, 300 * 4, 0);
            foreach (var point in new[]
                     {
                         (150, 34), (266, 150), (150, 266), (34, 150)
                     })
            {
                var alpha = pixels[(point.Item2 * 300 + point.Item1) * 4 + 3];
                Assert(alpha > 0, $"Single-item ring is visually incomplete at {point}.");
            }
        });

        Run("Overlay placement follows the pointer and clamps at all work-area edges", () =>
        {
            var topLeft = OverlayPlacement.Calculate(1920, 1040, 0, 0, 1920, 1040, 340);
            Assert(topLeft.Center.X >= 184, $"Top-left center was not inset: {topLeft.Center.X}");
            Assert(topLeft.Center.Y >= 184, $"Top-left center was not inset: {topLeft.Center.Y}");

            var bottomRight = OverlayPlacement.Calculate(2560, 1400, 2559, 1399, 2560, 1400, 460);
            Assert(bottomRight.Center.X <= 2560 - 244, "Right edge was not clamped.");
            Assert(bottomRight.Center.Y <= 1400 - 244, "Bottom edge was not clamped.");

            var scaled = OverlayPlacement.Calculate(3840, 2160, 1920, 1080, 2560, 1440, 330);
            Assert(Math.Abs(scaled.Center.X - 1280) < 0.01, $"DPI mapping X was {scaled.Center.X}.");
            Assert(Math.Abs(scaled.Center.Y - 720) < 0.01, $"DPI mapping Y was {scaled.Center.Y}.");
            Assert(scaled.Diameter == 330, "Configured wheel size changed unexpectedly.");
        });

        Run("Missing launch targets fail without starting a process", () =>
        {
            var launcher = new LaunchService();
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");
            var fileResult = launcher.Launch(MakeItem(WheelItemType.File, missing));
            Assert(!fileResult.Success, "Missing file launch should fail.");
            Assert(fileResult.Message.Contains("missing", StringComparison.OrdinalIgnoreCase),
                fileResult.Message);
        });

        Run("Custom command executes as the current user with separate arguments", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var marker = Path.Combine(directory, "command-result.txt");
                var powershell = Path.Combine(Environment.SystemDirectory,
                    "WindowsPowerShell", "v1.0", "powershell.exe");
                var item = new WheelItem
                {
                    Type = WheelItemType.Command,
                    Name = "Automated command",
                    Target = powershell,
                    Arguments = $"-NoProfile -NonInteractive -Command \"Set-Content -LiteralPath '{marker}' -Value 'command-ok'\""
                };
                var result = new LaunchService().Launch(item);
                Assert(result.Success, result.Message);
                for (var attempt = 0; attempt < 50 && !File.Exists(marker); attempt++)
                {
                    System.Threading.Thread.Sleep(100);
                }

                Assert(File.Exists(marker), "The command did not create its marker file in time.");
                Assert(File.ReadAllText(marker).Trim().TrimStart('\uFEFF') == "command-ok",
                    "The command produced the wrong marker content.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Export/import round-trips groups, order, names, themes, colors, and shortcuts", () =>
        {
            var configuration = ConfigurationDefaults.Create();
            var group = configuration.Groups.First();
            group.Name = "Round trip group";
            group.Children[0].Name = "Custom command name";
            group.Children[0].ColorMode = ItemColorMode.Automatic;
            group.Children[0].Color = "#ABCDEF";
            configuration.MainWheel.Add(new WheelItem
            {
                Type = WheelItemType.File,
                Name = "Missing but preserved",
                Target = @"Z:\Not-On-This-PC\missing.dat",
                Color = "#123456",
                ColorMode = ItemColorMode.Manual
            });
            configuration.Themes[0].Name = "Travel theme";
            configuration.ActiveThemeId = configuration.Themes[0].Id;
            configuration.Shortcut = new ShortcutDefinition
            {
                Modifiers = ShortcutModifiers.Control | ShortcutModifiers.Shift,
                VirtualKey = 0x4B
            };
            configuration.Settings.Language = AppLanguage.Arabic;

            var json = ConfigurationTransferService.Export(configuration, "test");
            var inspection = ConfigurationTransferService.Inspect(json);
            Assert(inspection.IsValid && inspection.Configuration is not null,
                string.Join("; ", inspection.Errors));
            var restored = inspection.Configuration!;
            Assert(restored.MainWheel[0].Id == configuration.MainWheel[0].Id, "Main order changed.");
            Assert(restored.Groups.First().Name == "Round trip group", "Group name changed.");
            Assert(restored.Groups.First().Children[0].Name == "Custom command name",
                "Nested item name changed.");
            Assert(restored.Groups.First().Children[0].ColorMode == ItemColorMode.Automatic,
                "Color mode changed.");
            Assert(restored.Themes[0].Name == "Travel theme", "Theme changed.");
            Assert(restored.Shortcut.Modifiers == configuration.Shortcut.Modifiers &&
                   restored.Shortcut.VirtualKey == configuration.Shortcut.VirtualKey,
                "Shortcut changed.");
            Assert(restored.Settings.Language == AppLanguage.Arabic, "Language changed.");
            Assert(restored.MainWheel.Last().Target == @"Z:\Not-On-This-PC\missing.dat",
                "A missing path was not preserved.");
            Assert(inspection.MissingTargetCount >= 1 &&
                   inspection.Warnings.Any(warning => warning.Contains("Z:\\Not-On-This-PC",
                       StringComparison.OrdinalIgnoreCase)),
                $"Missing target was not reported. Items={inspection.ItemCount}; " +
                $"Warnings={string.Join(" | ", inspection.Warnings)}");
            Assert(inspection.MissingTargets.Any(target =>
                    target.Contains("Missing but preserved", StringComparison.Ordinal)),
                "The missing-target list omitted the imported item name.");
        });

        Run("Malformed, unsupported, and structurally invalid imports are rejected", () =>
        {
            Assert(!ConfigurationTransferService.Inspect("{bad json").IsValid, "Malformed JSON was accepted.");
            var json = ConfigurationTransferService.Export(ConfigurationDefaults.Create(), "test");
            Assert(!ConfigurationTransferService.Inspect(
                    json.Replace("\"ExportVersion\": 1", "\"ExportVersion\": 99",
                        StringComparison.Ordinal)).IsValid,
                "Unsupported export version was accepted.");
            var invalid = ConfigurationDefaults.Create();
            while (invalid.MainWheel.Count <= ConfigurationValidator.MaxWheelItems)
            {
                invalid.MainWheel.Add(MakeItem(WheelItemType.Command, "notepad.exe"));
            }
            Assert(!ConfigurationTransferService
                    .Inspect(ConfigurationTransferService.Export(invalid, "test")).IsValid,
                "A configuration over the item limit was accepted.");
        });

        Run("Import inspection never executes imported commands", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var marker = Path.Combine(directory, "executed.txt");
                var configuration = ConfigurationDefaults.Create();
                configuration.MainWheel.Add(new WheelItem
                {
                    Type = WheelItemType.Command,
                    Name = "Inert import command",
                    Target = Path.Combine(Environment.SystemDirectory,
                        "WindowsPowerShell", "v1.0", "powershell.exe"),
                    Arguments = $"-NoProfile -Command \"Set-Content -LiteralPath '{marker}' -Value executed\""
                });
                var result = ConfigurationTransferService
                    .Inspect(ConfigurationTransferService.Export(configuration, "test"));
                Assert(result.IsValid, string.Join("; ", result.Errors));
                System.Threading.Thread.Sleep(250);
                Assert(!File.Exists(marker), "Import inspection executed a command.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("Import recovery backup captures the current file", () =>
        {
            var directory = CreateTempDirectory();
            try
            {
                var path = Path.Combine(directory, "config.json");
                var store = new ConfigurationStore(path);
                store.Save(ConfigurationDefaults.Create());
                var backup = store.CreateRecoveryBackup("pre-import");
                Assert(File.Exists(backup), "The pre-import backup was not created.");
                Assert(File.ReadAllText(backup) == File.ReadAllText(path),
                    "The pre-import backup does not match the current configuration.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

        Run("English and Arabic catalogs cover every UI string", () =>
        {
            var missing = LocalizationService.English.Keys
                .Except(LocalizationService.Arabic.Keys, StringComparer.Ordinal)
                .ToArray();
            Assert(missing.Length == 0, $"Missing Arabic keys: {string.Join(", ", missing)}");
            Assert(LocalizationService.English["WheelTitle"] != LocalizationService.Arabic["WheelTitle"],
                "Arabic translation was not distinct.");
            LocalizationService.ApplyLanguage(AppLanguage.Arabic);
            Assert(LocalizationService.Get("NavWheel") == "إدارة العجلة", "Arabic lookup failed.");
            var named = new WheelItem { Name = "User-defined name", Target = @"C:\x.txt" };
            Assert(LocalizationService.ItemCaption(named) == "User-defined name",
                "Language switching altered a user-defined item name.");
            LocalizationService.ApplyLanguage(AppLanguage.English);
            Assert(LocalizationService.Get("NavWheel") == "Wheel management", "English lookup failed.");
            LocalizationService.ApplyLanguage(AppLanguage.Arabic);
            LocalizationService.ApplyLanguage(AppLanguage.English);
        });

        Console.WriteLine();
        Console.WriteLine($"Passed: {_passed}");
        Console.WriteLine($"Failed: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static WheelItem MakeItem(WheelItemType type, string target) => new()
    {
        Type = type,
        Name = Path.GetFileName(target),
        Target = target
    };

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RadialCommandWheel.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static System.Windows.Media.Imaging.BitmapSource CreateColorBitmap(
        System.Windows.Media.Color left, System.Windows.Media.Color right)
    {
        const int width = 32;
        const int height = 32;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = x < width / 2 ? left : right;
                var offset = y * stride + x * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        return System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
            null, pixels, stride);
    }

    private static void DeleteTempFiles(AppConfiguration configuration)
    {
        foreach (var target in ConfigurationValidator.EnumerateAll(configuration.MainWheel)
                     .Select(item => item.Target).Where(File.Exists))
        {
            try
            {
                File.Delete(target);
            }
            catch
            {
                // Test cleanup is best-effort.
            }
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine($"FAIL  {name}");
            Console.WriteLine($"      {exception.Message}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

