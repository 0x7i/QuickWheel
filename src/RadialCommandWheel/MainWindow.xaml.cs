using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RadialCommandWheel.Core;
using RadialCommandWheel.Services;
using RadialCommandWheel.UI;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace RadialCommandWheel;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _saveTimer;
    private WheelTargetOption? _currentTarget;
    private WheelItem? _selectedItem;
    private ThemeDefinition? _selectedTheme;
    private bool _isLoading;
    private bool _recordingShortcut;
    private WpfPoint _dragStart;
    private WheelItem? _dragItem;
    private IReadOnlyList<LocalizedValueOption<WheelItemType>> _itemTypeOptions = [];
    private IReadOnlyList<LocalizedValueOption<IconSourceKind>> _iconSourceOptions = [];
    private IReadOnlyList<LocalizedIconOption> _builtInIconOptions = [];
    private IReadOnlyList<LocalizedValueOption<AppLanguage>> _languageOptions = [];

    public MainWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _controller.SaveNow();
        };

        PopulateLocalizedOptions();
        ConfigurationPathBox.Text = _controller.ConfigurationPath;
        PreviewWheel.GroupActivated += group =>
            PreviewWheel.SetWheel(LocalizationService.ItemCaption(group), group.Children, true);
        PreviewWheel.BackActivated += () =>
            ShowPreviewTarget(null);
        PreviewWheel.ItemActivated += _ => { };
        PreviewKeyDown += ShortcutRecorder_PreviewKeyDown;
        SourceInitialized += OnSourceInitialized;
        ApplyLayoutDirection();
        ReloadConfiguration();
        ShowSection(WheelPanel);
    }

    public void ApplyLanguage()
    {
        ApplyLayoutDirection();
        PopulateLocalizedOptions();
        var selectedItemId = _selectedItem?.Id;
        var selectedThemeId = _selectedTheme?.Id;
        var targetId = _currentTarget?.Group?.Id;
        _isLoading = true;
        try
        {
            LanguageBox.ItemsSource = _languageOptions;
            LanguageBox.SelectedItem = _languageOptions.First(option =>
                option.Value == _controller.Configuration.Settings.Language);
            BuildTargetOptions(targetId);
            RefreshThemeList(selectedThemeId);
            ItemsList.Items.Refresh();
            ThemeList.Items.Refresh();
            if (selectedItemId is not null)
            {
                ItemsList.SelectedItem = ItemsList.Items.Cast<WheelItem>()
                    .FirstOrDefault(item => item.Id == selectedItemId);
            }

            if (_selectedItem is not null)
            {
                LoadItemEditor(_selectedItem);
            }
            else
            {
                UpdateEmptyEditor();
            }

            UpdateCountText();
            UpdateThemeLabels();
            UpdateEditorHeading();
            ShortcutDisplayText.Text = LocalizationService.ShortcutDisplay(
                _controller.Configuration.Shortcut);
            UpdateShortcutStatus(_controller.ShortcutRegistered);
            ShowSaveStatus(LocalizationService.Get("SavedStatus"), false);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ApplyLayoutDirection()
    {
        FlowDirection = LocalizationService.CurrentLanguage == AppLanguage.Arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        foreach (var textBox in new[]
                 {
                     TargetBox, ArgumentsBox, ItemColorBox, IconValueBox, ThemeAccentBox,
                     ThemeSectorBox, ThemeTextBox, ThemeBackgroundBox, ConfigurationPathBox
                 })
        {
            textBox.FlowDirection = FlowDirection.LeftToRight;
        }
    }

    public void ReloadConfiguration()
    {
        var previousTargetId = _currentTarget?.Group?.Id;
        var previousThemeId = _selectedTheme?.Id;
        var previousItemId = _selectedItem?.Id;
        _isLoading = true;
        try
        {
            BuildTargetOptions(previousTargetId);
            if (previousItemId is not null)
            {
                ItemsList.SelectedItem = ItemsList.Items.Cast<WheelItem>()
                    .FirstOrDefault(item => item.Id == previousItemId);
            }
            RefreshThemeList(previousThemeId);
            StartWithWindowsCheckbox.IsChecked = _controller.Configuration.Settings.StartWithWindows;
            TrayNotificationsCheckbox.IsChecked = _controller.Configuration.Settings.ShowTrayNotifications;
            LanguageBox.ItemsSource = _languageOptions;
            LanguageBox.SelectedItem = _languageOptions.First(option =>
                option.Value == _controller.Configuration.Settings.Language);
            ConfigurationPathBox.Text = _controller.ConfigurationPath;
            ShortcutDisplayText.Text = LocalizationService.ShortcutDisplay(
                _controller.Configuration.Shortcut);
            ShortcutRegistrationText.Text = _controller.ShortcutRegistered
                ? LocalizationService.Get("ShortcutAccepted")
                : _controller.LocalizedShortcutStatus;
            UndoButton.IsEnabled = _controller.CanUndo;
            UpdatePreview();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void ShowSaveStatus(string message, bool isError)
    {
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = new SolidColorBrush(isError
            ? WpfColor.FromRgb(255, 124, 134)
            : WpfColor.FromRgb(143, 155, 173));
        UndoButton.IsEnabled = _controller.CanUndo;
    }

    public void UpdateShortcutStatus(bool registered)
    {
        ShortcutRegistrationText.Text = registered
            ? LocalizationService.Format("ShortcutActiveFormat",
                LocalizationService.ShortcutDisplay(_controller.Configuration.Shortcut))
            : _controller.LocalizedShortcutStatus;
        ShortcutRegistrationText.Foreground = new SolidColorBrush(registered
            ? WpfColor.FromRgb(107, 213, 154)
            : WpfColor.FromRgb(255, 122, 135));
    }

    private void BuildTargetOptions(string? preferredGroupId)
    {
        var options = new List<WheelTargetOption>
        {
            new(LocalizationService.Get("MainWheel"), null)
        };
        options.AddRange(_controller.Configuration.Groups.Select(group =>
            new WheelTargetOption(LocalizationService.ItemCaption(group), group)));
        TargetList.ItemsSource = options;
        var preferred = options.FirstOrDefault(option =>
            string.Equals(option.Group?.Id, preferredGroupId, StringComparison.Ordinal)) ?? options[0];
        TargetList.SelectedItem = preferred;
        ActivateTarget(preferred);
    }

    private void ActivateTarget(WheelTargetOption target)
    {
        _currentTarget = target;
        ItemsList.ItemsSource = target.Group?.Children ?? _controller.Configuration.MainWheel;
        AddGroupButton.Visibility = target.Group is null ? Visibility.Visible : Visibility.Collapsed;
        ItemsList.SelectedItem = null;
        _selectedItem = null;
        UpdateEmptyEditor();
        UpdateCountText();
        UpdatePreview();
    }

    private void TargetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || TargetList.SelectedItem is not WheelTargetOption target)
        {
            return;
        }

        ActivateTarget(target);
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedItem = ItemsList.SelectedItem as WheelItem;
        LoadItemEditor(_selectedItem);
    }

    private void PopulateLocalizedOptions()
    {
        _itemTypeOptions = Enum.GetValues<WheelItemType>()
            .Select(type => new LocalizedValueOption<WheelItemType>(
                LocalizationService.TypeName(type), type))
            .ToArray();
        _iconSourceOptions = Enum.GetValues<IconSourceKind>()
            .Select(source => new LocalizedValueOption<IconSourceKind>(
                LocalizationService.IconSourceName(source), source))
            .ToArray();
        _builtInIconOptions = IconService.BuiltInIconNames
            .Select(name => new LocalizedIconOption(name,
                LocalizationService.Get($"IconLibrary_{name}")))
            .ToArray();
        _languageOptions = new[]
        {
            new LocalizedValueOption<AppLanguage>(LocalizationService.Get("English"), AppLanguage.English),
            new LocalizedValueOption<AppLanguage>(LocalizationService.Get("Arabic"), AppLanguage.Arabic)
        };
        ItemTypeBox.ItemsSource = _itemTypeOptions;
        IconSourceBox.ItemsSource = _iconSourceOptions;
        BuiltInIconBox.ItemsSource = _builtInIconOptions;
    }

    private void LoadItemEditor(WheelItem? item)
    {
        var previousLoading = _isLoading;
        _isLoading = true;
        try
        {
            if (item is null)
            {
                UpdateEmptyEditor();
                return;
            }

            EmptyEditorPanel.Visibility = Visibility.Collapsed;
            ItemEditorScroll.Visibility = Visibility.Visible;
            ItemEditorPanel.Visibility = Visibility.Visible;
            DeleteItemButton.Visibility = Visibility.Visible;
            UpdateEditorHeading();
            EditorSubheading.Text = LocalizationService.Format("EditorSaved",
                LocalizationService.TypeName(item.Type));
            ItemTypeBox.SelectedItem = _itemTypeOptions.First(option => option.Value == item.Type);
            ItemNameBox.Text = item.Name;
            TargetBox.Text = item.Target;
            ArgumentsBox.Text = item.Arguments;
            ShowNameCheckbox.IsChecked = item.ShowName;
            ItemColorBox.Text = item.Color;
            AutoColorCheckbox.IsChecked = item.ColorMode == ItemColorMode.Automatic;
            IconSourceBox.SelectedItem =
                _iconSourceOptions.First(option => option.Value == item.IconSource);
            IconValueBox.Text = item.IconValue;
            BuiltInIconBox.SelectedItem = _builtInIconOptions.FirstOrDefault(option =>
                string.Equals(option.Name, item.IconValue, StringComparison.OrdinalIgnoreCase));
            UpdateItemTypeUi();
            UpdateTargetFeedback();
            UpdateIconSourceUi();
            UpdateIconFeedback();
            UpdateItemColorUi();
        }
        finally
        {
            _isLoading = previousLoading;
        }
    }

    private void UpdateEmptyEditor()
    {
        EmptyEditorPanel.Visibility = Visibility.Visible;
        ItemEditorScroll.Visibility = Visibility.Collapsed;
        DeleteItemButton.Visibility = Visibility.Collapsed;
        EditorHeading.Text = LocalizationService.Get("SelectItem");
        EditorSubheading.Text = LocalizationService.Get("SelectItemHelp");
    }

    private void UpdateEditorHeading()
    {
        EditorHeading.Text = _selectedItem is null
            ? LocalizationService.Get("SelectItem")
            : LocalizationService.ItemCaption(_selectedItem);
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var collection = _currentTarget?.Group?.Children ?? _controller.Configuration.MainWheel;
        if (collection.Count >= ConfigurationValidator.MaxWheelItems)
        {
            MessageBox.Show(this,
                LocalizationService.Format("SlotLimitCurrent", ConfigurationValidator.MaxWheelItems),
                LocalizationService.Get("SlotLimitTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _controller.RecordUndo();
        var item = new WheelItem
        {
            Type = _currentTarget?.Group is null ? WheelItemType.Application : WheelItemType.File,
            Color = "#4CC9F0",
            ColorMode = ItemColorMode.Automatic,
            ShowName = true
        };
        if (_currentTarget?.Group is null)
        {
            ConfigurationValidator.TryAddToMain(_controller.Configuration, item, out _);
        }
        else
        {
            ConfigurationValidator.TryAdd(_currentTarget.Group, item, out _);
        }

        ItemsList.SelectedItem = item;
        ItemsList.ScrollIntoView(item);
        QueueSave();
        UpdateCountText();
        UpdatePreview();
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTarget?.Group is not null)
        {
            return;
        }

        if (_controller.Configuration.MainWheel.Count >= ConfigurationValidator.MaxWheelItems)
        {
            MessageBox.Show(this,
                LocalizationService.Format("SlotLimitMain", ConfigurationValidator.MaxWheelItems),
                LocalizationService.Get("SlotLimitTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _controller.RecordUndo();
        var group = new WheelItem
        {
            Type = WheelItemType.Group,
            Name = LocalizationService.Get("NewGroupName"),
            Color = "#A78BFA",
            ColorMode = ItemColorMode.Manual,
            ShowName = true
        };
        ConfigurationValidator.TryAddToMain(_controller.Configuration, group, out _);
        QueueSave();
        BuildTargetOptions(group.Id);
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        var deletingGroup = _selectedItem.Type == WheelItemType.Group;
        var collection = _currentTarget?.Group?.Children ?? _controller.Configuration.MainWheel;
        collection.Remove(_selectedItem);
        _selectedItem = null;
        ItemsList.SelectedItem = null;
        if (deletingGroup)
        {
            BuildTargetOptions(null);
        }

        QueueSave();
        UpdateCountText();
        UpdatePreview();
    }

    private void ItemTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null ||
            ItemTypeBox.SelectedItem is not LocalizedValueOption<WheelItemType> selected)
        {
            return;
        }

        var type = selected.Value;
        if (type == WheelItemType.Group && _currentTarget?.Group is not null)
        {
            _isLoading = true;
            ItemTypeBox.SelectedItem = _itemTypeOptions.First(option =>
                option.Value == (_selectedItem.Type == WheelItemType.Group
                    ? WheelItemType.File
                    : _selectedItem.Type));
            _isLoading = false;
            ShowSaveStatus(LocalizationService.Get("NestedGroupRejected"), true);
            return;
        }

        _controller.RecordUndo();
        _selectedItem.Type = type;
        if (type != WheelItemType.Group)
        {
            _selectedItem.Children.Clear();
        }

        UpdateItemTypeUi();
        UpdateTargetFeedback();
        UpdatePreview();
        QueueSave();
    }

    private void UpdateItemTypeUi()
    {
        if (_selectedItem is null)
        {
            return;
        }

        var type = _selectedItem.Type;
        TargetLabel.Visibility = type == WheelItemType.Group ? Visibility.Collapsed : Visibility.Visible;
        TargetBox.Visibility = TargetLabel.Visibility;
        BrowseTargetButton.Visibility = TargetLabel.Visibility;
        TargetLabel.Text = type switch
        {
            WheelItemType.Application => LocalizationService.Get("ApplicationPath"),
            WheelItemType.File => LocalizationService.Get("FilePath"),
            WheelItemType.Folder => LocalizationService.Get("FolderPath"),
            WheelItemType.Command => LocalizationService.Get("CommandPath"),
            _ => LocalizationService.Get("TargetPath")
        };
        ArgumentsPanel.Visibility = type is WheelItemType.Application or WheelItemType.Command
            ? Visibility.Visible
            : Visibility.Collapsed;
        GroupNotice.Visibility = type == WheelItemType.Group ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ItemNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedItem.Name = ItemNameBox.Text;
        UpdateEditorHeading();
        UpdatePreview();
        QueueSave();
    }

    private void TargetBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedItem.Target = TargetBox.Text;
        UpdateEditorHeading();
        UpdateTargetFeedback();
        UpdateIconFeedback();
        if (_selectedItem.ColorMode == ItemColorMode.Automatic)
        {
            UpdateItemColorUi();
        }
        UpdatePreview();
        QueueSave();
    }

    private void ArgumentsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedItem.Arguments = ArgumentsBox.Text;
        QueueSave();
    }

    private void ShowNameCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedItem.ShowName = ShowNameCheckbox.IsChecked == true;
        UpdatePreview();
        QueueSave();
    }

    private void ItemColorBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null || _selectedItem.ColorMode == ItemColorMode.Automatic)
        {
            return;
        }

        if (!ConfigurationValidator.TryNormalizeHexColor(ItemColorBox.Text, out var normalized))
        {
            ItemColorBox.ToolTip = LocalizationService.Get("ColorInvalid");
            ItemColorBox.Text = _selectedItem.Color;
            return;
        }

        _controller.RecordUndo();
        _selectedItem.Color = normalized;
        _selectedItem.ColorMode = ItemColorMode.Manual;
        ItemColorBox.Text = normalized;
        AutoColorCheckbox.IsChecked = false;
        UpdateItemColorSwatch();
        UpdateColorModeHint();
        UpdatePreview();
        QueueSave();
    }

    private void PickItemColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.ColorTranslator.FromHtml(
                ConfigurationValidator.IsHexColor(_selectedItem.Color) ? _selectedItem.Color : "#4CC9F0")
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedItem.Color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        _selectedItem.ColorMode = ItemColorMode.Manual;
        _isLoading = true;
        ItemColorBox.Text = _selectedItem.Color;
        AutoColorCheckbox.IsChecked = false;
        _isLoading = false;
        UpdateItemColorSwatch();
        UpdateColorModeHint();
        UpdatePreview();
        QueueSave();
    }

    private void UpdateItemColorSwatch()
    {
        try
        {
            ItemColorSwatch.Background = new SolidColorBrush(
                (WpfColor)ColorConverter.ConvertFromString(ItemColorBox.Text));
        }
        catch
        {
            ItemColorSwatch.Background = Brushes.Transparent;
        }
    }

    private void UpdateItemColorUi()
    {
        if (_selectedItem is null)
        {
            return;
        }

        var previousLoading = _isLoading;
        var automatic = _selectedItem.ColorMode == ItemColorMode.Automatic;
        _isLoading = true;
        AutoColorCheckbox.IsChecked = automatic;
        ItemColorBox.IsReadOnly = automatic;
        ItemColorBox.Text = automatic
            ? ColorSuggestionService.SuggestHex(_selectedItem, _selectedTheme ?? _controller.Configuration.ActiveTheme)
            : _selectedItem.Color;
        _isLoading = previousLoading;
        UpdateItemColorSwatch();
        UpdateColorModeHint();
    }

    private void UpdateColorModeHint()
    {
        if (_selectedItem is null)
        {
            ColorModeHintText.Text = "";
            return;
        }

        ColorModeHintText.Text = LocalizationService.Get(
            _selectedItem.ColorMode == ItemColorMode.Automatic ? "AutoColorHint" : "ManualColorHint");
    }

    private void AutoColorCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        if (AutoColorCheckbox.IsChecked == true)
        {
            _selectedItem.ColorMode = ItemColorMode.Automatic;
            ColorSuggestionService.Refresh(_selectedItem, _selectedTheme ?? _controller.Configuration.ActiveTheme);
        }
        else
        {
            if (ConfigurationValidator.TryNormalizeHexColor(ItemColorBox.Text, out var current))
            {
                _selectedItem.Color = current;
            }

            _selectedItem.ColorMode = ItemColorMode.Manual;
        }

        UpdateItemColorUi();
        UpdatePreview();
        QueueSave();
    }

    private void SuggestColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        var theme = _selectedTheme ?? _controller.Configuration.ActiveTheme;
        ColorSuggestionService.Refresh(_selectedItem, theme);
        var suggestion = ColorSuggestionService.SuggestHex(_selectedItem, theme);
        if (_selectedItem.ColorMode == ItemColorMode.Manual)
        {
            _selectedItem.Color = suggestion;
        }

        UpdateItemColorUi();
        UpdatePreview();
        QueueSave();
    }

    private void IconSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null ||
            IconSourceBox.SelectedItem is not LocalizedValueOption<IconSourceKind> selected)
        {
            return;
        }

        var source = selected.Value;
        _controller.RecordUndo();
        _selectedItem.IconSource = source;
        if (source == IconSourceKind.BuiltIn && string.IsNullOrWhiteSpace(_selectedItem.IconValue))
        {
            _selectedItem.IconValue = "launch";
        }

        _isLoading = true;
        IconValueBox.Text = _selectedItem.IconValue;
        if (source == IconSourceKind.BuiltIn)
        {
            BuiltInIconBox.SelectedItem = _builtInIconOptions.FirstOrDefault(option =>
                string.Equals(option.Name, _selectedItem.IconValue, StringComparison.OrdinalIgnoreCase))
                ?? _builtInIconOptions.First();
        }

        _isLoading = false;
        UpdateIconSourceUi();
        UpdateIconFeedback();
        if (_selectedItem.ColorMode == ItemColorMode.Automatic)
        {
            ColorSuggestionService.Refresh(_selectedItem, _selectedTheme ?? _controller.Configuration.ActiveTheme);
            UpdateItemColorUi();
        }
        UpdatePreview();
        QueueSave();
    }

    private void UpdateIconSourceUi()
    {
        if (_selectedItem is null)
        {
            return;
        }

        var source = _selectedItem.IconSource;
        BuiltInIconBox.Visibility = source == IconSourceKind.BuiltIn ? Visibility.Visible : Visibility.Collapsed;
        IconValueBox.Visibility = source == IconSourceKind.File ? Visibility.Visible : Visibility.Collapsed;
        BrowseIconButton.Visibility = source == IconSourceKind.File ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuiltInIconBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null ||
            BuiltInIconBox.SelectedItem is not LocalizedIconOption option)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedItem.IconValue = option.Name;
        UpdateIconFeedback();
        if (_selectedItem.ColorMode == ItemColorMode.Automatic)
        {
            ColorSuggestionService.Refresh(_selectedItem, _selectedTheme ?? _controller.Configuration.ActiveTheme);
            UpdateItemColorUi();
        }
        UpdatePreview();
        QueueSave();
    }

    private void IconValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedItem is null)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedItem.IconValue = IconValueBox.Text;
        UpdateIconFeedback();
        if (_selectedItem.ColorMode == ItemColorMode.Automatic)
        {
            ColorSuggestionService.Refresh(_selectedItem, _selectedTheme ?? _controller.Configuration.ActiveTheme);
            UpdateItemColorUi();
        }
        UpdatePreview();
        QueueSave();
    }

    private void UpdateIconFeedback()
    {
        if (_selectedItem is null)
        {
            IconFeedbackText.Text = "";
            return;
        }

        IconService.ClearCache();
        var icon = IconService.Resolve(_selectedItem);
        IconFeedbackText.Text = icon.ErrorKey is null
            ? LocalizationService.Get("IconValid")
            : LocalizeIconError(icon, _selectedItem);
        IconFeedbackText.Foreground = new SolidColorBrush(icon.Error is null
            ? WpfColor.FromRgb(125, 158, 143)
            : WpfColor.FromRgb(255, 142, 117));
    }

    private void UpdateTargetFeedback()
    {
        if (_selectedItem is null)
        {
            TargetFeedbackText.Text = "";
            return;
        }

        var validation = TargetValidator.Validate(_selectedItem);
        TargetFeedbackText.Text = LocalizeTargetValidation(validation, _selectedItem);
        TargetFeedbackText.Foreground = new SolidColorBrush(validation.State switch
        {
            TargetValidationState.Valid => WpfColor.FromRgb(107, 213, 154),
            TargetValidationState.Warning => WpfColor.FromRgb(246, 200, 95),
            TargetValidationState.Error => WpfColor.FromRgb(255, 142, 117),
            _ => WpfColor.FromRgb(127, 141, 161)
        });
    }

    private static string LocalizeIconError(ItemIcon icon, WheelItem item) => icon.ErrorKey switch
    {
        "IconFileMissing" => LocalizationService.Format("IconFileMissing", item.IconValue),
        "IconTargetMissing" => LocalizationService.Format("IconTargetMissing", item.Target),
        "IconLoadFailed" => LocalizationService.Get("IconLoadFailed"),
        "IconSvgRoot" => LocalizationService.Get("IconSvgRoot"),
        "IconSvgActive" => LocalizationService.Get("IconSvgActive"),
        "IconSvgExternal" => LocalizationService.Get("IconSvgExternal"),
        "IconSvgInvalid" => LocalizationService.Get("IconSvgInvalid"),
        "IconShellUnavailable" => LocalizationService.Get("IconShellUnavailable"),
        "IconUnavailable" => LocalizationService.Get("IconUnavailable"),
        _ => icon.Error ?? LocalizationService.Get("IconUnavailable")
    };

    private static string LocalizeTargetValidation(TargetValidation validation, WheelItem item)
    {
        return validation.MessageKey switch
        {
            "TargetMissing" => LocalizationService.Get("TargetMissing"),
            "TargetGroup" => LocalizationService.Get("TargetGroup"),
            "TargetRelative" => LocalizationService.Get("TargetRelative"),
            "TargetCommandName" => LocalizationService.Get("TargetCommandName"),
            "TargetValidFile" => LocalizationService.Get("TargetValidFile"),
            "TargetValidFolder" => LocalizationService.Get("TargetValidFolder"),
            "TargetValidExecutable" => LocalizationService.Get("TargetValidExecutable"),
            _ when validation.State == TargetValidationState.Error && item.Type == WheelItemType.Folder =>
                LocalizationService.Format("TargetMissingFolder", item.Target),
            _ when validation.State == TargetValidationState.Error &&
                   item.Type is WheelItemType.Application or WheelItemType.Command =>
                LocalizationService.Format("TargetMissingExecutable", item.Target),
            _ when validation.State == TargetValidationState.Error =>
                LocalizationService.Format("TargetMissingFile", item.Target),
            _ => validation.Message
        };
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        if (_selectedItem.Type == WheelItemType.Folder)
        {
            using var folderDialog = new Forms.FolderBrowserDialog
            {
                Description = LocalizationService.Get("ChooseFolderDialog"),
                ShowNewFolderButton = false
            };
            if (Directory.Exists(_selectedItem.Target))
            {
                folderDialog.SelectedPath = _selectedItem.Target;
            }

            if (folderDialog.ShowDialog() == Forms.DialogResult.OK)
            {
                TargetBox.Text = folderDialog.SelectedPath;
            }

            return;
        }

        using var dialog = new Forms.OpenFileDialog
        {
            Title = LocalizationService.Get("ChooseTargetDialog"),
            CheckFileExists = true,
            Multiselect = false
        };
        dialog.Filter = _selectedItem.Type switch
        {
            WheelItemType.Application => LocalizationService.Get("ApplicationsFilter"),
            WheelItemType.Command => LocalizationService.Get("CommandsFilter"),
            _ => LocalizationService.Get("AllFilesFilter")
        };
        if (File.Exists(_selectedItem.Target))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_selectedItem.Target);
        }

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            TargetBox.Text = dialog.FileName;
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        using var dialog = new Forms.OpenFileDialog
        {
            Title = LocalizationService.Get("ChooseIconDialog"),
            CheckFileExists = true,
            Multiselect = false,
            Filter = LocalizationService.Get("ImagesFilter")
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            IconValueBox.Text = dialog.FileName;
        }
    }

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ItemsList);
        _dragItem = GetItemFromMouse(e);
    }

    private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
        {
            return;
        }

        var current = e.GetPosition(ItemsList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(ItemsList, new DataObject(typeof(WheelItem), dragged), DragDropEffects.Move);
    }

    private void ItemsList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(WheelItem)) ||
            e.Data.GetData(typeof(WheelItem)) is not WheelItem dragged ||
            _currentTarget is null)
        {
            return;
        }

        var target = GetItemFromDrop(e);
        var collection = _currentTarget.Group?.Children ?? _controller.Configuration.MainWheel;
        var oldIndex = collection.IndexOf(dragged);
        if (oldIndex < 0)
        {
            return;
        }

        var newIndex = target is null ? collection.Count - 1 : collection.IndexOf(target);
        if (newIndex < 0 || newIndex == oldIndex)
        {
            return;
        }

        _controller.RecordUndo();
        collection.RemoveAt(oldIndex);
        if (newIndex > oldIndex)
        {
            newIndex--;
        }

        collection.Insert(Math.Clamp(newIndex, 0, collection.Count), dragged);
        ItemsList.SelectedItem = dragged;
        UpdatePreview();
        QueueSave();
    }

    private WheelItem? GetItemFromMouse(MouseEventArgs e) =>
        FindDataContext((DependencyObject)e.OriginalSource);

    private WheelItem? GetItemFromDrop(DragEventArgs e)
    {
        var hit = ItemsList.InputHitTest(e.GetPosition(ItemsList)) as DependencyObject;
        return FindDataContext(hit);
    }

    private static WheelItem? FindDataContext(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: WheelItem item })
            {
                return item;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ThemeList.SelectedItem is not ThemeDefinition theme)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedTheme = theme;
        _controller.Configuration.ActiveThemeId = theme.Id;
        LoadThemeEditor(theme);
        UpdateItemColorUi();
        UpdatePreview();
        QueueSave();
    }

    private void RefreshThemeList(string? preferredThemeId)
    {
        ThemeList.ItemsSource = _controller.Configuration.Themes;
        var theme = _controller.Configuration.Themes.FirstOrDefault(candidate =>
                        candidate.Id == preferredThemeId)
                    ?? _controller.Configuration.ActiveTheme;
        ThemeList.SelectedItem = theme;
        _selectedTheme = theme;
        LoadThemeEditor(theme);
    }

    private void LoadThemeEditor(ThemeDefinition theme)
    {
        _isLoading = true;
        try
        {
            ThemeNameBox.Text = theme.Name;
            ThemeAccentBox.Text = theme.Accent;
            ThemeSectorBox.Text = theme.Sector;
            ThemeTextBox.Text = theme.Text;
            ThemeBackgroundBox.Text = theme.Background;
            OpacitySlider.Value = theme.Opacity;
            DimmingSlider.Value = theme.Dimming;
            WheelSizeSlider.Value = theme.WheelSize;
            AnimationDurationSlider.Value = theme.AnimationDurationMs;
            AnimationCheckbox.IsChecked = theme.AnimationEnabled;
            UpdateThemeLabels();
            ThemeValidationText.Text = "";
            PreviewWheel.Theme = theme;
            PreviewWheel.DesiredDiameter = Math.Min(theme.WheelSize, 320);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void AddTheme_Click(object sender, RoutedEventArgs e)
    {
        var source = _selectedTheme ?? _controller.Configuration.ActiveTheme;
        _controller.RecordUndo();
        var theme = source.Clone();
        theme.Id = Guid.NewGuid().ToString("N");
        theme.Name = LocalizationService.Format("ThemeNameSuffix", source.Name);
        theme.IsBuiltIn = false;
        _controller.Configuration.Themes.Add(theme);
        _controller.Configuration.ActiveThemeId = theme.Id;
        RefreshThemeList(theme.Id);
        UpdatePreview();
        QueueSave();
    }

    private void ThemeField_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isLoading || _selectedTheme is null)
        {
            return;
        }

        var colors = new[]
        {
            ThemeAccentBox.Text,
            ThemeSectorBox.Text,
            ThemeTextBox.Text,
            ThemeBackgroundBox.Text
        };
        if (colors.Any(value => !ConfigurationValidator.IsHexColor(value)))
        {
            ThemeValidationText.Text = LocalizationService.Get("ColorInvalid");
            return;
        }

        _controller.RecordUndo();
        _selectedTheme.Name = ThemeNameBox.Text;
        _selectedTheme.Accent = ThemeAccentBox.Text.ToUpperInvariant();
        _selectedTheme.Sector = ThemeSectorBox.Text.ToUpperInvariant();
        _selectedTheme.Text = ThemeTextBox.Text.ToUpperInvariant();
        _selectedTheme.Background = ThemeBackgroundBox.Text.ToUpperInvariant();
        ThemeValidationText.Text = "";
        ThemeList.Items.Refresh();
        UpdateItemColorUi();
        UpdatePreview();
        QueueSave();
    }

    private void ThemeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isLoading)
        {
            _controller.RecordUndo();
        }
    }

    private void ThemeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading || _selectedTheme is null)
        {
            return;
        }

        _selectedTheme.Opacity = OpacitySlider.Value;
        _selectedTheme.Dimming = DimmingSlider.Value;
        _selectedTheme.WheelSize = WheelSizeSlider.Value;
        _selectedTheme.AnimationDurationMs = AnimationDurationSlider.Value;
        UpdateThemeLabels();
        UpdateItemColorUi();
        UpdatePreview();
        QueueSave();
    }

    private void AnimationCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _selectedTheme is null)
        {
            return;
        }

        _controller.RecordUndo();
        _selectedTheme.AnimationEnabled = AnimationCheckbox.IsChecked == true;
        UpdatePreview();
        QueueSave();
    }

    private void UpdateThemeLabels()
    {
        OpacityLabel.Text = LocalizationService.Format("OpacityFormat", OpacitySlider.Value);
        DimmingLabel.Text = LocalizationService.Format("DimmingFormat", DimmingSlider.Value);
        WheelSizeLabel.Text = LocalizationService.Format("WheelSizeFormat", WheelSizeSlider.Value);
        AnimationDurationLabel.Text = LocalizationService.Format("AnimationFormat",
            AnimationDurationSlider.Value);
    }

    private void UpdatePreview()
    {
        var items = _currentTarget?.Group?.Children ?? _controller.Configuration.MainWheel;
        var title = _currentTarget?.Group is null
            ? LocalizationService.Get("MainCenter")
            : LocalizationService.ItemCaption(_currentTarget.Group);
        PreviewWheel.SetWheel(title, items, _currentTarget?.Group is not null);
        PreviewWheel.Theme = _selectedTheme ?? _controller.Configuration.ActiveTheme;
        PreviewWheel.Items = items;
    }

    private void ShowPreviewTarget(WheelItem? group)
    {
        if (group is null)
        {
            PreviewWheel.SetWheel(LocalizationService.Get("MainCenter"),
                _controller.Configuration.MainWheel, false);
        }
        else if (_currentTarget?.Group is not null &&
                 group.Id == _currentTarget.Group.Id)
        {
            PreviewWheel.SetWheel(LocalizationService.ItemCaption(group), group.Children, true);
        }
    }

    private void RecordShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingShortcut)
        {
            StopShortcutRecording(savePrevious: false);
            return;
        }

        _recordingShortcut = true;
        RecordShortcutButton.Content = LocalizationService.Get("CancelRecording");
        ShortcutDisplayText.Text = LocalizationService.Get("PressShortcut");
        ShortcutValidationText.Text = LocalizationService.Get("ShortcutRecordingHelp");
        _controller.UnregisterShortcut();
        Focus();
    }

    private void ShortcutRecorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingShortcut)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            e.Handled = true;
            StopShortcutRecording(savePrevious: true);
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var shortcutModifiers = ShortcutModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) shortcutModifiers |= ShortcutModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) shortcutModifiers |= ShortcutModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) shortcutModifiers |= ShortcutModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) shortcutModifiers |= ShortcutModifiers.Windows;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0)
        {
            ShortcutValidationText.Text = LocalizationService.Get("ShortcutKeyInvalid");
            e.Handled = true;
            return;
        }

        if (shortcutModifiers == ShortcutModifiers.None)
        {
            ShortcutValidationText.Text = LocalizationService.Get("ShortcutNeedsModifier");
            e.Handled = true;
            return;
        }

        _controller.RecordUndo();
        _controller.Configuration.Shortcut = new ShortcutDefinition
        {
            Modifiers = shortcutModifiers,
            VirtualKey = virtualKey
        };
        _recordingShortcut = false;
        RecordShortcutButton.Content = LocalizationService.Get("RecordShortcut");
        ShortcutDisplayText.Text = LocalizationService.ShortcutDisplay(
            _controller.Configuration.Shortcut);
        ShortcutValidationText.Text = "";
        e.Handled = true;
        _controller.SaveNow();
        var status = _controller.RegisterShortcut();
        if (!string.IsNullOrWhiteSpace(status))
        {
            ShortcutValidationText.Text = status;
        }

        UndoButton.IsEnabled = _controller.CanUndo;
    }

    private void StopShortcutRecording(bool savePrevious)
    {
        _recordingShortcut = false;
        RecordShortcutButton.Content = LocalizationService.Get("RecordShortcut");
        ShortcutDisplayText.Text = LocalizationService.ShortcutDisplay(
            _controller.Configuration.Shortcut);
        ShortcutValidationText.Text = "";
        _controller.RegisterShortcut();
        if (savePrevious)
        {
            _controller.SaveNow();
        }
    }

    private void StartWithWindowsCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _controller.RecordUndo();
        _controller.Configuration.Settings.StartWithWindows = StartWithWindowsCheckbox.IsChecked == true;
        _controller.SaveNow();
        _controller.TryApplyStartupSetting();
    }

    private void TrayNotificationsCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _controller.RecordUndo();
        _controller.Configuration.Settings.ShowTrayNotifications =
            TrayNotificationsCheckbox.IsChecked == true;
        QueueSave();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading ||
            LanguageBox.SelectedItem is not LocalizedValueOption<AppLanguage> option ||
            option.Value == _controller.Configuration.Settings.Language)
        {
            return;
        }

        _controller.RecordUndo();
        _controller.ApplyLanguage(option.Value);
    }

    private void ExportConfiguration_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = LocalizationService.Get("ExportDialogTitle"),
            Filter = LocalizationService.Get("JsonFilter"),
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"RadialCommandWheel-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            var json = ConfigurationTransferService.Export(
                _controller.Configuration,
                typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "1.0.0");
            File.WriteAllText(dialog.FileName, json, new System.Text.UTF8Encoding(false));
            MessageBox.Show(this, dialog.FileName, LocalizationService.Get("ExportCompleted"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, LocalizationService.Get("ExportFailed"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportConfiguration_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = LocalizationService.Get("ImportDialogTitle"),
            Filter = LocalizationService.Get("JsonFilter"),
            DefaultExt = ".json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        ConfigurationTransferInspection inspection;
        try
        {
            inspection = ConfigurationTransferService.Inspect(File.ReadAllText(dialog.FileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            inspection = ConfigurationTransferInspection.Invalid(exception.Message);
        }

        if (!inspection.IsValid || inspection.Configuration is null)
        {
            MessageBox.Show(this, LocalizeImportErrors(inspection.Errors),
                LocalizationService.Get("ImportInvalidTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var preview = new ImportPreviewWindow(this, inspection);
        if (preview.ShowDialog() != true)
        {
            return;
        }

        var result = _controller.CommitImportedConfiguration(inspection.Configuration);
        if (!result.Success)
        {
            MessageBox.Show(this, result.ErrorMessage ?? "",
                LocalizationService.Get("ImportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var message = result.MissingTargetCount > 0
            ? LocalizationService.Format("MissingTargetsFormat", result.MissingTargetCount)
            : $"{LocalizationService.Get("ImportBackupCreated")}\n{result.BackupPath}";
        if (!result.ShortcutRegistered)
        {
            message += $"\n\n{LocalizationService.Get("ShortcutImportedConflict")}";
        }

        MessageBox.Show(this, message, LocalizationService.Get("ImportSucceeded"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string LocalizeImportErrors(IReadOnlyList<string> errors)
    {
        var localized = errors.Select(error =>
        {
            if (error.Contains("Malformed JSON", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Get("ImportErrMalformed");
            if (error.Contains("not a Radial Command Wheel", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("no configuration", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Get("ImportErrWrongFormat");
            if (error.Contains("version", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Get("ImportErrUnsupportedVersion");
            if (error.Contains("payload", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("empty", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Get("ImportErrEmpty");
            if (error.Contains("nested group", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Get("NestedGroupRejected");
            if (error.Contains("limit", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Format("SlotLimitMain", ConfigurationValidator.MaxWheelItems);
            if (error.Contains("color", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Get("ColorInvalid");
            return LocalizationService.Get("ImportErrStructure");
        }).Distinct();
        return string.Join(Environment.NewLine, localized);
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _controller.OpenConfigurationFolder();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, LocalizationService.Get("CouldNotOpenFolder"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => _controller.ExitApplication();

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.UndoLastChange())
        {
            UndoButton.IsEnabled = _controller.CanUndo;
            ShowSaveStatus(LocalizationService.Get("LastChangeUndone"), false);
        }
    }

    private void QueueSave()
    {
        if (_isLoading)
        {
            return;
        }

        SaveStatusText.Text = LocalizationService.Get("Saving");
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateCountText()
    {
        var count = (_currentTarget?.Group?.Children ?? _controller.Configuration.MainWheel).Count;
        ItemCountText.Text = LocalizationService.Format("ItemCountFormat", count,
            ConfigurationValidator.MaxWheelItems);
    }

    private void WheelNav_Click(object sender, RoutedEventArgs e) => ShowSection(WheelPanel);
    private void AppearanceNav_Click(object sender, RoutedEventArgs e) => ShowSection(AppearancePanel);
    private void ShortcutNav_Click(object sender, RoutedEventArgs e) => ShowSection(ShortcutPanel);
    private void GeneralNav_Click(object sender, RoutedEventArgs e) => ShowSection(GeneralPanel);

    private void ShowSection(UIElement section)
    {
        foreach (var panel in new FrameworkElement[] { WheelPanel, AppearancePanel, ShortcutPanel, GeneralPanel })
        {
            panel.Visibility = ReferenceEquals(panel, section) ? Visibility.Visible : Visibility.Collapsed;
        }

        var buttons = new[] { WheelNavButton, AppearanceNavButton, ShortcutNavButton, GeneralNavButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            buttons[index].Background = new SolidColorBrush(index switch
            {
                0 when ReferenceEquals(section, WheelPanel) => WpfColor.FromRgb(24, 39, 56),
                1 when ReferenceEquals(section, AppearancePanel) => WpfColor.FromRgb(24, 39, 56),
                2 when ReferenceEquals(section, ShortcutPanel) => WpfColor.FromRgb(24, 39, 56),
                3 when ReferenceEquals(section, GeneralPanel) => WpfColor.FromRgb(24, 39, 56),
                _ => Colors.Transparent
            });
        }

        if (ReferenceEquals(section, AppearancePanel))
        {
            UpdatePreview();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var darkMode = 1;
        var cornerPreference = 2;
        var backdrop = 2;
        DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref cornerPreference, sizeof(int));
        DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public sealed record WheelTargetOption(string Title, WheelItem? Group)
    {
        public override string ToString() => Title;
    }

    public sealed record LocalizedValueOption<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    public sealed record LocalizedIconOption(string Name, string Label)
    {
        public override string ToString() => Label;
    }
}

