using System.Globalization;
using System.Windows.Data;
using RadialCommandWheel.Core;
using RadialCommandWheel.Services;

namespace RadialCommandWheel.UI;

public sealed class ItemCaptionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WheelItem item ? LocalizationService.ItemCaption(item) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class ItemTypeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WheelItem item
            ? LocalizationService.TypeName(item.Type)
            : value is WheelItemType type ? LocalizationService.TypeName(type) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
