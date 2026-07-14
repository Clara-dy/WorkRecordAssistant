using System.IO;
using System.Text.Json;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Services;

/// <summary>
/// JSON 文件持久化应用设置。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    /// <summary>D 盘首选数据目录（安装与数据同根）。</summary>
    public const string PreferredDataDirectory = @"D:\WorkRecordAssistant\Data";

    private readonly string _settingsPath;
    private readonly string _dataDirectory;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        _dataDirectory = ResolveDataDirectory();
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
    }

    public string GetDataDirectory() => _dataDirectory;

    public async Task LoadAsync()
    {
        Directory.CreateDirectory(_dataDirectory);

        if (!File.Exists(_settingsPath))
        {
            Current = new AppSettings();
            await SaveAsync();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            WindowGeometryHelper.SanitizeSettings(Current);
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }

    private static string ResolveDataDirectory()
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkRecordAssistant");

        if (!IsDriveReady(@"D:\"))
            return legacy;

        Directory.CreateDirectory(PreferredDataDirectory);
        MigrateLegacyDataIfNeeded(legacy, PreferredDataDirectory);
        return PreferredDataDirectory;
    }

    private static bool IsDriveReady(string root)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root) ?? root);
            return drive.IsReady;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 首次启用 D 盘目录时，从 %LocalAppData%\WorkRecordAssistant 拷贝已有库与设置。
    /// </summary>
    private static void MigrateLegacyDataIfNeeded(string legacyDir, string targetDir)
    {
        if (!Directory.Exists(legacyDir)) return;

        var targetDb = Path.Combine(targetDir, "workrecords.db");
        var legacyDb = Path.Combine(legacyDir, "workrecords.db");

        // 目标已有数据库则视为已迁移，避免覆盖较新数据。
        if (File.Exists(targetDb)) return;

        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(legacyDir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(targetDir, name);
            if (!File.Exists(dest))
                File.Copy(file, dest, overwrite: false);
        }

        // 若只有设置没有库，也迁；若 lib 不存在则仅迁已有文件即可
        _ = legacyDb;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
