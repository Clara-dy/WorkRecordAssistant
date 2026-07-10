using System.Diagnostics;
using System.Windows;

namespace WorkRecordAssistant.Services;

/// <summary>
/// 剪贴板与外部链接辅助服务。
/// </summary>
public static class ClipboardService
{
    public static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板被占用时忽略
        }
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // 无效 URL
        }
    }
}
