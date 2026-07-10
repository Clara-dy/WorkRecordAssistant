using System.IO;

namespace WorkRecordAssistant.Services;

/// <summary>
/// 开始菜单快捷方式管理，便于 Windows 搜索启动应用。
/// </summary>
public static class StartMenuShortcutService
{
    private const string ShortcutFileName = "工作记录助手.lnk";
    private const string Description = "Windows 悬浮工作记录助手";

    public static void EnsureShortcut(string executablePath)
    {
        if (!CanCreateShortcut(executablePath))
            return;

        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            ShortcutFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = executablePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
        shortcut.Description = Description;
        shortcut.Save();
    }

    private static bool CanCreateShortcut(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return false;

        return string.Equals(
            Path.GetFileName(executablePath),
            "WorkRecordAssistant.exe",
            StringComparison.OrdinalIgnoreCase);
    }
}
