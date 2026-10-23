using System.Collections.ObjectModel;

namespace RadialCommandWheel.Core;

public static class ConfigurationDefaults
{
    public static AppConfiguration Create()
    {
        var themes = new ObservableCollection<ThemeDefinition>
        {
            new()
            {
                Id = "aurora",
                Name = "Aurora",
                Accent = "#4CC9F0",
                Sector = "#172433",
                Text = "#F5FAFF",
                Background = "#080D14",
                Opacity = 0.97,
                Dimming = 0.24,
                WheelSize = 340,
                AnimationEnabled = true,
                AnimationDurationMs = 150,
                IsBuiltIn = true
            },
            new()
            {
                Id = "ember",
                Name = "Ember",
                Accent = "#FF8A5B",
                Sector = "#33201D",
                Text = "#FFF7F2",
                Background = "#120908",
                Opacity = 0.96,
                Dimming = 0.30,
                WheelSize = 330,
                AnimationEnabled = true,
                AnimationDurationMs = 140,
                IsBuiltIn = true
            },
            new()
            {
                Id = "violet-glass",
                Name = "Violet glass",
                Accent = "#A78BFA",
                Sector = "#241D3A",
                Text = "#FAF8FF",
                Background = "#0D0A17",
                Opacity = 0.94,
                Dimming = 0.20,
                WheelSize = 350,
                AnimationEnabled = true,
                AnimationDurationMs = 170,
                IsBuiltIn = true
            },
            new()
            {
                Id = "mono",
                Name = "Monochrome",
                Accent = "#E7ECF3",
                Sector = "#23262B",
                Text = "#FFFFFF",
                Background = "#090A0C",
                Opacity = 0.98,
                Dimming = 0.34,
                WheelSize = 320,
                AnimationEnabled = false,
                AnimationDurationMs = 100,
                IsBuiltIn = true
            }
        };

        var toolsGroup = new WheelItem
        {
            Id = "group-tools",
            Type = WheelItemType.Group,
            Name = "System tools",
            Color = "#F9C74F",
            ColorMode = ItemColorMode.Manual,
            ShowName = true,
            Children =
            [
                new WheelItem
                {
                    Id = "item-settings",
                    Type = WheelItemType.Command,
                    Name = "Windows Settings",
                    Target = "explorer.exe",
                    Arguments = "ms-settings:",
                    Color = "#F9C74F",
                    ColorMode = ItemColorMode.Manual,
                    ShowName = true
                },
                new WheelItem
                {
                    Id = "item-task-manager",
                    Type = WheelItemType.Application,
                    Name = "Task Manager",
                    Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Taskmgr.exe"),
                    Color = "#FF6B6B",
                    ColorMode = ItemColorMode.Manual,
                    ShowName = true
                }
            ]
        };

        var mainWheel = new ObservableCollection<WheelItem>
        {
            new()
            {
                Id = "item-documents",
                Type = WheelItemType.Folder,
                Name = "Documents",
                Target = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Color = "#4CC9F0",
                ColorMode = ItemColorMode.Manual,
                ShowName = true
            },
            new()
            {
                Id = "item-notepad",
                Type = WheelItemType.Application,
                Name = "Notepad",
                Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"),
                Color = "#4D96FF",
                ColorMode = ItemColorMode.Manual,
                ShowName = true
            },
            new()
            {
                Id = "item-downloads",
                Type = WheelItemType.Folder,
                Name = "Downloads",
                Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Color = "#6BCB77",
                ColorMode = ItemColorMode.Manual,
                ShowName = true
            },
            toolsGroup,
            new()
            {
                Id = "item-explorer",
                Type = WheelItemType.Application,
                Name = "File Explorer",
                Target = "explorer.exe",
                Color = "#FFD166",
                ColorMode = ItemColorMode.Manual,
                ShowName = true
            }
        };

        return new AppConfiguration
        {
            MainWheel = mainWheel,
            Themes = themes,
            ActiveThemeId = "aurora",
            Shortcut = new ShortcutDefinition
            {
                Modifiers = ShortcutModifiers.Control | ShortcutModifiers.Alt,
                VirtualKey = 0x20
            },
            Settings = new GeneralSettings
            {
                StartWithWindows = true,
                ShowTrayNotifications = true,
                Language = AppLanguage.English
            }
        };
    }
}

