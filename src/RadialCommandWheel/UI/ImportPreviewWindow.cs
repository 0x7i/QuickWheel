using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RadialCommandWheel.Core;
using RadialCommandWheel.Services;

namespace RadialCommandWheel.UI;

public sealed class ImportPreviewWindow : Window
{
    public ImportPreviewWindow(Window owner, ConfigurationTransferInspection inspection)
    {
        Owner = owner;
        Title = LocalizationService.Get("ImportPreviewTitle");
        Width = 560;
        MinHeight = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(9, 13, 20));
        Foreground = new SolidColorBrush(Color.FromRgb(245, 248, 252));
        FlowDirection = LocalizationService.CurrentLanguage == AppLanguage.Arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        var languageName = inspection.Configuration?.Settings.Language == AppLanguage.Arabic
            ? LocalizationService.Get("Arabic")
            : LocalizationService.Get("English");
        var summary = LocalizationService.Format("ImportSummaryFormat",
            inspection.ItemCount, inspection.GroupCount, inspection.ThemeCount, languageName,
            inspection.Configuration is null
                ? ""
                : LocalizationService.ShortcutDisplay(inspection.Configuration.Shortcut));

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("ImportPreviewHeading"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(new TextBlock
        {
            Text = summary,
            Foreground = new SolidColorBrush(Color.FromRgb(182, 197, 213)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        if (inspection.MissingTargetCount > 0)
        {
            var warningPanel = new StackPanel();
            warningPanel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Format("MissingTargetsFormat", inspection.MissingTargetCount),
                Foreground = new SolidColorBrush(Color.FromRgb(246, 200, 95)),
                TextWrapping = TextWrapping.Wrap
            });
            warningPanel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("MissingTargetsLabel"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 9, 0, 5)
            });
            foreach (var target in inspection.MissingTargets.Take(8))
            {
                warningPanel.Children.Add(new TextBlock
                {
                    Text = target,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 11,
                    FlowDirection = FlowDirection.LeftToRight,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 34, 20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(116, 86, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 14),
                Child = warningPanel
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("ImportBackupCreated"),
            Foreground = new SolidColorBrush(Color.FromRgb(118, 190, 154)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button
        {
            Content = LocalizationService.Get("ImportCancel"),
            IsCancel = true,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var confirm = new Button
        {
            Content = LocalizationService.Get("ImportConfirm"),
            IsDefault = true,
            MinWidth = 190,
            Background = new SolidColorBrush(Color.FromRgb(25, 48, 69)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 86, 110))
        };
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        content.Children.Add(buttons);
        Content = content;
    }
}
