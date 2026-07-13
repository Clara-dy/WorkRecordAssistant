using System.Windows;

namespace WorkRecordAssistant.Services;

/// <summary>
/// 剪贴板与 URL 打开。
/// </summary>
public static class ClipboardService
{
    public static bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 忽略无法打开 URL
        }
    }
}
