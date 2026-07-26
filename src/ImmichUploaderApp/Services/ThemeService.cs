using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ImmichUploaderApp.Services;

public readonly struct Palette
{
    public required Color Background { get; init; }
    public required Color Border { get; init; }
    public required Color Text { get; init; }
    public required Color TextMuted { get; init; }
    public required Color Divider { get; init; }
    public required Color Accent { get; init; }
    public required Color Track { get; init; }
    public required Color ThumbPlaceholder { get; init; }
    public required Color ControlBackground { get; init; }
    public required Color ControlBorder { get; init; }
    public required bool IsDark { get; init; }

    public static Palette Dark => new()
    {
        Background = Color.FromArgb(32, 32, 32),
        Border = Color.FromArgb(64, 64, 64),
        Text = Color.FromArgb(245, 245, 245),
        TextMuted = Color.FromArgb(155, 155, 155),
        Divider = Color.FromArgb(55, 55, 55),
        Accent = Color.FromArgb(0, 191, 179),
        Track = Color.FromArgb(55, 55, 55),
        ThumbPlaceholder = Color.FromArgb(48, 48, 48),
        ControlBackground = Color.FromArgb(45, 45, 45),
        ControlBorder = Color.FromArgb(80, 80, 80),
        IsDark = true,
    };

    public static Palette Light => new()
    {
        Background = Color.FromArgb(250, 250, 250),
        Border = Color.FromArgb(222, 222, 222),
        Text = Color.FromArgb(28, 28, 28),
        TextMuted = Color.FromArgb(110, 110, 110),
        Divider = Color.FromArgb(228, 228, 228),
        Accent = Color.FromArgb(0, 150, 140),
        Track = Color.FromArgb(228, 228, 228),
        ThumbPlaceholder = Color.FromArgb(238, 238, 238),
        ControlBackground = Color.White,
        ControlBorder = Color.FromArgb(200, 200, 200),
        IsDark = false,
    };
}

public static class ThemeService
{
    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// themeSetting is AppConfig.Theme: "Light", "Dark", or anything else (treated as "System").
    public static Palette Resolve(string themeSetting) => themeSetting switch
    {
        "Light" => Palette.Light,
        "Dark" => Palette.Dark,
        _ => IsSystemDarkMode() ? Palette.Dark : Palette.Light,
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    /// Colors the window's native title bar to match; silently no-ops on Windows versions that
    /// don't support it (pre-Win11 20H1) since this is a cosmetic touch, not a functional one.
    public static void ApplyTitleBarTheme(Form form, bool dark)
    {
        try
        {
            const int dwmwaUseImmersiveDarkMode = 20;
            var useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, dwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
        }
        catch
        {
            // Best-effort cosmetic touch only.
        }
    }
}
