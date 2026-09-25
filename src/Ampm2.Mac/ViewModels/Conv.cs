using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ampm2.Mac.ViewModels;

public static class Conv
{
    public static readonly IValueConverter ConnectedBrush =
        new FuncValueConverter<bool, IBrush>(c => c ? StatusBrushes.Online : StatusBrushes.Launching);

    public static readonly IValueConverter ToastBrush =
        new FuncValueConverter<Toast?, IBrush>(t => t?.Kind switch
        {
            ToastKind.Success => StatusBrushes.Online,
            ToastKind.Error => StatusBrushes.Errored,
            _ => Resource("Accent", Brushes.SlateBlue),
        });

    public static readonly IValueConverter LogBrush =
        new FuncValueConverter<LogLine?, IBrush>(l => l?.Kind switch
        {
            LogKind.Err => Resource("LogErr", Brushes.IndianRed),
            LogKind.Marker => Resource("Accent", Brushes.SlateBlue),
            _ => Resource("Text", Brushes.Gray),
        });

    private static IBrush Resource(string key, IBrush fallback)
    {
        var app = Application.Current;
        if (app != null && app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is IBrush b) return b;
        return fallback;
    }
}
