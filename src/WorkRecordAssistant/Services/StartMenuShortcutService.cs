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
        var targetPath = ResolveShortcutTarget(executablePath);
        if (!CanCreateShortcut(targetPath))
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
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        shortcut.Description = Description;
        shortcut.Save();
    }

    private static string ResolveShortcutTarget(string executablePath)
    {
        var preferredInstallExe = @"D:\WorkRecordAssistant\App\WorkRecordAssistant.exe";
        if (File.Exists(preferredInstallExe))
            return preferredInstallExe;

        var legacyInstallExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "WorkRecordAssistant",
            "WorkRecordAssistant.exe");

        if (File.Exists(legacyInstallExe))
            return legacyInstallExe;

        return executablePath;
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
