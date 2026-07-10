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
    private readonly string _settingsPath;
    private readonly string _dataDirectory;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        _dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkRecordAssistant");

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
