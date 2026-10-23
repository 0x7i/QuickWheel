using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RadialCommandWheel.Core;

public static partial class ConfigurationValidator
{
    public const int MaxWheelItems = 12;

    public static ValidationResult Validate(AppConfiguration configuration)
    {
        var result = new ValidationResult();
        if (configuration.SchemaVersion > AppConfiguration.CurrentSchemaVersion)
        {
            result.AddError($"Configuration version {configuration.SchemaVersion} is newer than this application supports.");
            return result;
        }

        if (configuration.SchemaVersion <= 0)
        {
            result.AddError("Configuration version must be positive.");
        }

        configuration.MainWheel ??= [];
        configuration.Themes ??= [];
        configuration.Shortcut ??= new ShortcutDefinition();
        configuration.Settings ??= new GeneralSettings();
        if (!Enum.IsDefined(configuration.Settings.Language))
        {
            result.AddError("The configured interface language is invalid.");
        }

        ValidateItems(configuration.MainWheel, "Main wheel", false, result);

        if (configuration.Themes.Count == 0)
        {
            result.AddError("At least one theme is required.");
        }

        var duplicateThemeIds = configuration.Themes
            .GroupBy(theme => theme.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var id in duplicateThemeIds)
        {
            result.AddError($"Duplicate theme ID '{id}'.");
        }

        foreach (var theme in configuration.Themes)
        {
            if (!IsHexColor(theme.Accent) || !IsHexColor(theme.Sector) ||
                !IsHexColor(theme.Text) || !IsHexColor(theme.Background))
            {
                result.AddError($"Theme '{theme.Name}' contains an invalid color. Use #RRGGBB.");
            }

            if (theme.Opacity is < 0.35 or > 1) result.AddError($"Theme '{theme.Name}' opacity is out of range.");
            if (theme.Dimming is < 0 or > 0.8) result.AddError($"Theme '{theme.Name}' dimming is out of range.");
            if (theme.WheelSize is < 190 or > 460) result.AddError($"Theme '{theme.Name}' wheel size is out of range.");
        }

        if (configuration.Themes.Count > 0 &&
            configuration.Themes.All(theme => theme.Id != configuration.ActiveThemeId))
        {
            result.AddWarning("The selected theme was missing; the first available theme will be used.");
        }

        if (configuration.Shortcut.VirtualKey <= 0 || configuration.Shortcut.VirtualKey > 0xFE)
        {
            result.AddError("The activation shortcut key is invalid.");
        }

        if (configuration.Shortcut.Modifiers == ShortcutModifiers.None)
        {
            result.AddWarning("A modifier-free activation shortcut may conflict with normal typing.");
        }

        return result;
    }

    public static bool TryAdd(WheelItem? parent, WheelItem item, out string error)
    {
        if (parent is { Type: not WheelItemType.Group })
        {
            error = "Only internal groups and the main wheel can contain items.";
            return false;
        }

        if (item.Type == WheelItemType.Group && parent is not null)
        {
            error = "Groups cannot contain other groups.";
            return false;
        }

        var collection = parent?.Children ?? throw new ArgumentNullException(nameof(parent),
            "Use TryAddToMain for top-level items.");
        if (collection.Count >= MaxWheelItems)
        {
            error = $"A wheel can contain at most {MaxWheelItems} items.";
            return false;
        }

        if (collection.Any(existing => existing.Id == item.Id))
        {
            item.Id = Guid.NewGuid().ToString("N");
        }

        item.Children.Clear();
        collection.Add(item);
        error = "";
        return true;
    }

    public static bool TryAddToMain(AppConfiguration configuration, WheelItem item, out string error)
    {
        if (configuration.MainWheel.Count >= MaxWheelItems)
        {
            error = $"The main wheel can contain at most {MaxWheelItems} items.";
            return false;
        }

        if (configuration.MainWheel.Any(existing => existing.Id == item.Id))
        {
            item.Id = Guid.NewGuid().ToString("N");
        }

        configuration.MainWheel.Add(item);
        error = "";
        return true;
    }

    public static void Normalize(AppConfiguration configuration)
    {
        configuration.MainWheel ??= [];
        configuration.Themes ??= [];
        if (configuration.Themes.Count == 0)
        {
            configuration.Themes = ConfigurationDefaults.Create().Themes;
        }

        if (configuration.Themes.All(theme => theme.Id != configuration.ActiveThemeId))
        {
            configuration.ActiveThemeId = configuration.Themes[0].Id;
        }

        TrimCollection(configuration.MainWheel);

        foreach (var item in EnumerateAll(configuration.MainWheel))
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }

            if (!IsHexColor(item.Color))
            {
                item.Color = "#4CC9F0";
            }

            item.Name ??= "";
            item.Target ??= "";
            item.Arguments ??= "";
            item.IconValue ??= "";
            if (!Enum.IsDefined(item.ColorMode))
            {
                item.ColorMode = ItemColorMode.Manual;
            }
            item.Children ??= [];
            if (item.Type != WheelItemType.Group)
            {
                item.Children.Clear();
            }
            else
            {
                for (var index = item.Children.Count - 1; index >= 0; index--)
                {
                    if (item.Children[index].Type == WheelItemType.Group)
                    {
                        item.Children.RemoveAt(index);
                    }
                }

                TrimCollection(item.Children);
            }
        }

        DeduplicateIds(configuration.MainWheel);
        if (!Enum.IsDefined(configuration.Settings.Language))
        {
            configuration.Settings.Language = AppLanguage.English;
        }
        configuration.SchemaVersion = AppConfiguration.CurrentSchemaVersion;
    }

    public static IEnumerable<WheelItem> EnumerateAll(IEnumerable<WheelItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in EnumerateAll(item.Children))
            {
                yield return child;
            }
        }
    }

    public static bool IsHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColorRegex().IsMatch(value);

    public static bool TryNormalizeHexColor(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        if (!IsHexColor(value))
        {
            return false;
        }

        normalized = value.ToUpper(CultureInfo.InvariantCulture);
        return true;
    }

    private static void ValidateItems(IEnumerable<WheelItem> items, string location, bool insideGroup,
        ValidationResult result)
    {
        var list = items.ToList();
        if (list.Count > MaxWheelItems)
        {
            result.AddError($"{location} has {list.Count} items; the limit is {MaxWheelItems}.");
        }

        var duplicateIds = list.GroupBy(item => item.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var id in duplicateIds)
        {
            result.AddError($"{location} has duplicate item ID '{id}'.");
        }

        foreach (var item in list)
        {
            var label = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name;
            if (insideGroup && item.Type == WheelItemType.Group)
            {
                result.AddError($"Group '{label}' contains a nested group. Nested groups are not allowed.");
            }

            if (item.Type != WheelItemType.Group && string.IsNullOrWhiteSpace(item.Target))
            {
                result.AddWarning($"Item '{label}' has no target path or command yet.");
            }

            if (!IsHexColor(item.Color))
            {
                result.AddError($"Item '{label}' has an invalid color. Use #RRGGBB.");
            }

            if (!Enum.IsDefined(item.ColorMode))
            {
                result.AddError($"Item '{label}' has an invalid color mode.");
            }

            if (item.IconSource == IconSourceKind.File && string.IsNullOrWhiteSpace(item.IconValue))
            {
                result.AddError($"Item '{label}' is set to use a custom icon but no icon file is selected.");
            }

            if (item.Type == WheelItemType.Group)
            {
                ValidateItems(item.Children, $"Group '{label}'", true, result);
                if (item.Target.Length > 0)
                {
                    result.AddWarning($"Group '{label}' has a filesystem target that will be ignored.");
                }
            }
            else if (item.Children.Count > 0)
            {
                result.AddError($"Item '{label}' is not a group but contains child items.");
            }
        }
    }

    private static void DeduplicateIds(IEnumerable<WheelItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in EnumerateAll(items))
        {
            if (!seen.Add(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
                seen.Add(item.Id);
            }
        }
    }

    private static void TrimCollection(ObservableCollection<WheelItem> items)
    {
        while (items.Count > MaxWheelItems)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}

