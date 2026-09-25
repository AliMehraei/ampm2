using System;
using System.Windows;
using System.Windows.Interop;
using Ampm2.Sys;
using Microsoft.Win32;

namespace Ampm2.Ui;

public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;
    public static event Action? Changed;
    private static string _mode = "System";

    public static bool SystemPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v ? v == 0 : true;
        }
        catch { return true; }
    }

    public static void Apply(string mode)
    {
        _mode = mode;
        bool dark = mode switch { "Dark" => true, "Light" => false, _ => SystemPrefersDark() };
        IsDark = dark;
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var palette = new ResourceDictionary { Source = uri };
        // [0] is always the palette, [1] the control styles
        if (dicts.Count > 0) dicts[0] = palette; else dicts.Add(palette);
        foreach (Window w in Application.Current.Windows) ApplyChrome(w);
        Changed?.Invoke();
    }

    public static void HookSystemChanges()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _mode == "System")
                Application.Current.Dispatcher.BeginInvoke(() => Apply("System"));
        };
    }

    /// <summary>Dark/light native title bar, colored to blend with the window body (Windows 11).</summary>
    public static void ApplyChrome(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        int caption = IsDark ? 0x0015100E : 0x00F9F5F4;   // COLORREF 0x00BBGGRR: matches C.Bg
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_CAPTION_COLOR, ref caption, 4);
        int text = IsDark ? 0x00F0EAE7 : 0x00211814;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_TEXT_COLOR, ref text, 4);
        int border = IsDark ? 0x00372B26 : 0x00ECE4E1;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, 4);
    }
}
