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
        Background = Color.FromArgb(13, 19, 31),
        Border = Color.FromArgb(43, 55, 76),
        Text = Color.FromArgb(234, 240, 250),
        TextMuted = Color.FromArgb(148, 164, 189),
        Divider = Color.FromArgb(35, 47, 66),
        Accent = Color.FromArgb(139, 167, 255),
        Track = Color.FromArgb(35, 47, 66),
        ThumbPlaceholder = Color.FromArgb(31, 43, 63),
        ControlBackground = Color.FromArgb(22, 32, 49),
        ControlBorder = Color.FromArgb(53, 69, 94),
        IsDark = true,
    };

    public static Palette Light => new()
    {
        Background = Color.FromArgb(244, 247, 252),
        Border = Color.FromArgb(217, 225, 239),
        Text = Color.FromArgb(30, 43, 65),
        TextMuted = Color.FromArgb(100, 116, 141),
        Divider = Color.FromArgb(225, 232, 244),
        Accent = Color.FromArgb(62, 94, 210),
        Track = Color.FromArgb(225, 232, 244),
        ThumbPlaceholder = Color.FromArgb(235, 240, 249),
        ControlBackground = Color.White,
        ControlBorder = Color.FromArgb(194, 207, 227),
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
