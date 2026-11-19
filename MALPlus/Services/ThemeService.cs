using System;
using MALPlus.Controls;
using MALPlus.XShared.Utils;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace MALPlus.Services;

public static class ThemeService
{
    // Dark Theme (Standard Deep Navy)
    public static readonly Color DarkBackground = Color.FromArgb("#051522");
    public static readonly Color DarkSheet = Color.FromArgb("#0A1F2E");
    public static readonly Color DarkBorder = Color.FromArgb("#1E3A52");
    public static readonly Color DarkChipBg = Color.FromArgb("#141D2B");
    public static readonly Color DarkChipText = Color.FromArgb("#8FA8C0");
    public static readonly Color DarkSunkenBg = Color.FromArgb("#061522");
    public static readonly Color DarkSunkenBorder = Color.FromArgb("#162E44");

    // AMOLED Profile 1: Charcoal (Black background + subtle elevated #0D0D0D sheets)
    public static readonly Color CharcoalBackground = Color.FromArgb("#000000");
    public static readonly Color CharcoalSheet = Color.FromArgb("#0D0D0D");
    public static readonly Color CharcoalBorder = Color.FromArgb("#262626");
    public static readonly Color CharcoalChipBg = Color.FromArgb("#161616");
    public static readonly Color CharcoalChipText = Color.FromArgb("#8FA8C0");
    public static readonly Color CharcoalSunkenBg = Color.FromArgb("#050505");
    public static readonly Color CharcoalSunkenBorder = Color.FromArgb("#1F1F1F");

    // AMOLED Profile 2: Real Dark (100% Pure Black everywhere, border-delineated)
    public static readonly Color RealDarkBackground = Color.FromArgb("#000000");
    public static readonly Color RealDarkSheet = Color.FromArgb("#000000");
    public static readonly Color RealDarkBorder = Color.FromArgb("#282828");
    public static readonly Color RealDarkChipBg = Color.FromArgb("#121212");
    public static readonly Color RealDarkChipText = Color.FromArgb("#8FA8C0");
    public static readonly Color RealDarkSunkenBg = Color.FromArgb("#050505");
    public static readonly Color RealDarkSunkenBorder = Color.FromArgb("#222222");

    // Legacy Amoled aliases
    public static Color AmoledBackground => RealDarkBackground;
    public static Color AmoledSheet => IsRealDark ? RealDarkSheet : CharcoalSheet;
    public static Color AmoledBorder => IsRealDark ? RealDarkBorder : CharcoalBorder;
    public static Color AmoledChipBg => IsRealDark ? RealDarkChipBg : CharcoalChipBg;
    public static Color AmoledChipText => RealDarkChipText;

    public static bool IsAmoled => Settings.AppTheme != 0;
    public static bool IsRealDark => Settings.AppTheme == 2;

    public static Color CurrentBackground => !IsAmoled ? DarkBackground : (IsRealDark ? RealDarkBackground : CharcoalBackground);
    public static Color CurrentSheet => !IsAmoled ? DarkSheet : (IsRealDark ? RealDarkSheet : CharcoalSheet);
    public static Color CurrentBorder => !IsAmoled ? DarkBorder : (IsRealDark ? RealDarkBorder : CharcoalBorder);
    public static Color CurrentChipBg => !IsAmoled ? DarkChipBg : (IsRealDark ? RealDarkChipBg : CharcoalChipBg);
    public static Color CurrentChipText => !IsAmoled ? DarkChipText : (IsRealDark ? RealDarkChipText : CharcoalChipText);
    public static Color CurrentSunkenBg => !IsAmoled ? DarkSunkenBg : (IsRealDark ? RealDarkSunkenBg : CharcoalSunkenBg);
    public static Color CurrentSunkenBorder => !IsAmoled ? DarkSunkenBorder : (IsRealDark ? RealDarkSunkenBorder : CharcoalSunkenBorder);

    public static event Action ThemeChanged;

    public static void ApplyTheme()
    {
        var bg = CurrentBackground;
        var sheet = CurrentSheet;
        var border = CurrentBorder;
        var chipBg = CurrentChipBg;
        var chipText = CurrentChipText;

        if (Application.Current?.Resources != null)
        {
            Application.Current.Resources["EmBackground"] = bg;
            Application.Current.Resources["EmSheet"] = sheet;
            Application.Current.Resources["EmBorder"] = border;
            Application.Current.Resources["EmBorderBrush"] = new SolidColorBrush(border);
            Application.Current.Resources["EmBackgroundBrush"] = new SolidColorBrush(bg);
            Application.Current.Resources["EmSheetBrush"] = new SolidColorBrush(sheet);
            Application.Current.Resources["TabChipInactiveBg"] = chipBg;
            Application.Current.Resources["TabChipInactiveText"] = chipText;
        }

        // Notify Shell pager / host bar
        if (Shell.Current is AppShell shell)
        {
            shell.RefreshTheme();
        }

        // Update BottomNavBar instances
        BottomNavBar.ApplyThemeToAll();

        // Update Android window bars
#if ANDROID
        MainActivity.SetWindowThemeColors(IsAmoled);
#endif

        ThemeChanged?.Invoke();
    }

    public static void ApplyTheme(bool isAmoled)
    {
        Settings.DarkThemeAmoled = isAmoled;
        ApplyTheme();
    }
}
