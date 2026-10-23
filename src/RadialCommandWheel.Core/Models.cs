using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RadialCommandWheel.Core;

public enum WheelItemType
{
    Application,
    File,
    Folder,
    Command,
    Group
}

public enum IconSourceKind
{
    Automatic,
    BuiltIn,
    File
}

public enum ItemColorMode
{
    Manual,
    Automatic
}

public enum AppLanguage
{
    English,
    Arabic
}

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed class WheelItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private WheelItemType _type = WheelItemType.Application;
    private string _name = "";
    private string _target = "";
    private string _arguments = "";
    private bool _showName = true;
    private string _color = "#4CC9F0";
    private ItemColorMode _colorMode = ItemColorMode.Manual;
    private IconSourceKind _iconSource = IconSourceKind.Automatic;
    private string _iconValue = "";
    private ObservableCollection<WheelItem> _children = [];

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    public WheelItemType Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value))
            {
                OnPropertyChanged(nameof(TypeLabel));
                OnPropertyChanged(nameof(DisplayCaption));
                OnPropertyChanged(nameof(IsGroup));
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayCaption));
            }
        }
    }

    public string Target
    {
        get => _target;
        set
        {
            if (SetField(ref _target, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayCaption));
                OnPropertyChanged(nameof(FallbackCaption));
            }
        }
    }

    public string Arguments
    {
        get => _arguments;
        set => SetField(ref _arguments, value ?? "");
    }

    public bool ShowName
    {
        get => _showName;
        set => SetField(ref _showName, value);
    }

    public string Color
    {
        get => _color;
        set => SetField(ref _color, string.IsNullOrWhiteSpace(value) ? "#4CC9F0" : value);
    }

    public ItemColorMode ColorMode
    {
        get => _colorMode;
        set => SetField(ref _colorMode, value);
    }

    public IconSourceKind IconSource
    {
        get => _iconSource;
        set => SetField(ref _iconSource, value);
    }

    public string IconValue
    {
        get => _iconValue;
        set => SetField(ref _iconValue, value ?? "");
    }

    public ObservableCollection<WheelItem> Children
    {
        get => _children;
        set => SetField(ref _children, value ?? []);
    }

    [JsonIgnore]
    public bool IsGroup => Type == WheelItemType.Group;

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        WheelItemType.Application => "Application",
        WheelItemType.File => "File",
        WheelItemType.Folder => "Folder",
        WheelItemType.Command => "Command",
        WheelItemType.Group => "Group",
        _ => Type.ToString()
    };

    [JsonIgnore]
    public string FallbackCaption
    {
        get
        {
            if (Type == WheelItemType.Group)
            {
                return "Group";
            }

            if (string.IsNullOrWhiteSpace(Target))
            {
                return TypeLabel;
            }

            return DisplayNameHelper.FromTarget(Target, Type);
        }
    }

    [JsonIgnore]
    public string DisplayCaption => string.IsNullOrWhiteSpace(Name) ? FallbackCaption : Name.Trim();

    public event PropertyChangedEventHandler? PropertyChanged;

    public WheelItem Clone()
    {
        return new WheelItem
        {
            Id = Id,
            Type = Type,
            Name = Name,
            Target = Target,
            Arguments = Arguments,
            ShowName = ShowName,
            Color = Color,
            ColorMode = ColorMode,
            IconSource = IconSource,
            IconValue = IconValue,
            Children = new ObservableCollection<WheelItem>(Children.Select(child => child.Clone()))
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ThemeDefinition : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "Custom theme";
    private string _accent = "#4CC9F0";
    private string _sector = "#18202C";
    private string _text = "#F7FAFC";
    private string _background = "#090D14";
    private double _opacity = 0.96;
    private double _dimming = 0.24;
    private double _wheelSize = 330;
    private bool _animationEnabled = true;
    private double _animationDurationMs = 150;
    private bool _isBuiltIn;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "Custom theme" : value.Trim());
    }

    public string Accent
    {
        get => _accent;
        set => SetField(ref _accent, value ?? "#4CC9F0");
    }

    public string Sector
    {
        get => _sector;
        set => SetField(ref _sector, value ?? "#18202C");
    }

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value ?? "#F7FAFC");
    }

    public string Background
    {
        get => _background;
        set => SetField(ref _background, value ?? "#090D14");
    }

    public double Opacity
    {
        get => _opacity;
        set => SetField(ref _opacity, Math.Clamp(value, 0.35, 1.0));
    }

    public double Dimming
    {
        get => _dimming;
        set => SetField(ref _dimming, Math.Clamp(value, 0.0, 0.80));
    }

    public double WheelSize
    {
        get => _wheelSize;
        set => SetField(ref _wheelSize, Math.Clamp(value, 190, 460));
    }

    public bool AnimationEnabled
    {
        get => _animationEnabled;
        set => SetField(ref _animationEnabled, value);
    }

    public double AnimationDurationMs
    {
        get => _animationDurationMs;
        set => SetField(ref _animationDurationMs, Math.Clamp(value, 60, 400));
    }

    public bool IsBuiltIn
    {
        get => _isBuiltIn;
        set => SetField(ref _isBuiltIn, value);
    }

    [JsonIgnore]
    public string BuiltInLabel => IsBuiltIn ? "Built in" : "Custom";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemeDefinition Clone()
    {
        return new ThemeDefinition
        {
            Id = Id,
            Name = Name,
            Accent = Accent,
            Sector = Sector,
            Text = Text,
            Background = Background,
            Opacity = Opacity,
            Dimming = Dimming,
            WheelSize = WheelSize,
            AnimationEnabled = AnimationEnabled,
            AnimationDurationMs = AnimationDurationMs,
            IsBuiltIn = IsBuiltIn
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class ShortcutDefinition
{
    public ShortcutModifiers Modifiers { get; set; } = ShortcutModifiers.Control | ShortcutModifiers.Alt;

    public int VirtualKey { get; set; } = 0x20;

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ShortcutModifiers.Windows)) parts.Add("Win");
            parts.Add(VirtualKeyName(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public static string VirtualKeyName(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        return virtualKey switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Esc",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => $"VK {virtualKey:X2}"
        };
    }

    public ShortcutDefinition Clone() => new() { Modifiers = Modifiers, VirtualKey = VirtualKey };
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = true;

    public bool ShowTrayNotifications { get; set; } = true;

    public AppLanguage Language { get; set; } = AppLanguage.English;
}

public sealed class AppConfiguration
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ObservableCollection<WheelItem> MainWheel { get; set; } = [];

    public ObservableCollection<ThemeDefinition> Themes { get; set; } = [];

    public string ActiveThemeId { get; set; } = "";

    public ShortcutDefinition Shortcut { get; set; } = new();

    public GeneralSettings Settings { get; set; } = new();

    public ThemeDefinition ActiveTheme =>
        Themes.FirstOrDefault(theme => theme.Id == ActiveThemeId) ?? Themes.First();

    [JsonIgnore]
    public IEnumerable<WheelItem> Groups => MainWheel.Where(item => item.Type == WheelItemType.Group);

    public AppConfiguration Clone()
    {
        return new AppConfiguration
        {
            SchemaVersion = SchemaVersion,
            MainWheel = new ObservableCollection<WheelItem>(MainWheel.Select(item => item.Clone())),
            Themes = new ObservableCollection<ThemeDefinition>(Themes.Select(theme => theme.Clone())),
            ActiveThemeId = ActiveThemeId,
            Shortcut = Shortcut.Clone(),
            Settings = new GeneralSettings
            {
                StartWithWindows = Settings.StartWithWindows,
                ShowTrayNotifications = Settings.ShowTrayNotifications,
                Language = Settings.Language
            }
        };
    }
}

public static class DisplayNameHelper
{
    private static readonly HashSet<string> ConventionalExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".rtf",
        ".csv", ".json", ".xml", ".html", ".htm", ".md", ".log", ".exe", ".com", ".msi",
        ".appx", ".msix", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".py", ".cs", ".png",
        ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp", ".mp3", ".wav", ".flac", ".m4a",
        ".mp4", ".mkv", ".avi", ".mov", ".zip", ".7z", ".rar", ".tar", ".gz", ".iso"
    };

    public static string FromTarget(string target, WheelItemType type)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return "";
        }

        var trimmed = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return trimmed;
        }

        var extension = Path.GetExtension(leaf);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length <= 1)
        {
            return leaf;
        }

        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            ShouldHideConventionalExtension(extension, type))
        {
            var withoutExtension = leaf[..^extension.Length];
            return string.IsNullOrWhiteSpace(withoutExtension) ? leaf : withoutExtension;
        }

        return leaf;
    }

    private static bool ShouldHideConventionalExtension(string extension, WheelItemType type)
    {
        if (type is not (WheelItemType.File or WheelItemType.Application))
        {
            return false;
        }

        return ConventionalExtensions.Contains(extension);
    }
}

public sealed class ValidationResult
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;
    public bool IsValid => _errors.Count == 0;

    public void AddError(string message) => _errors.Add(message);
    public void AddWarning(string message) => _warnings.Add(message);
}

