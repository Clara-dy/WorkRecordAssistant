using System.Windows;
using System.Windows.Media;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Services;

/// <summary>
/// 主题切换服务，支持浅色/深色/跟随系统。
/// </summary>
public static class ThemeService
{
    public static void ApplyTheme(ThemeMode mode)
    {
        var app = Application.Current;
        if (app is null) return;

        var effective = mode == ThemeMode.Auto ? GetSystemTheme() : mode;
        var dictUri = effective == ThemeMode.Dark
            ? new Uri("Resources/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Resources/LightTheme.xaml", UriKind.Relative);

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = dictUri });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Resources/Styles.xaml", UriKind.Relative) });
    }

    private static ThemeMode GetSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i && i == 0)
                return ThemeMode.Dark;
        }
        catch
        {
            // ignore
        }

        return ThemeMode.Light;
    }
}
