namespace WorkRecordAssistant.Services;

/// <summary>
/// 应用设置读写抽象。
/// </summary>
public interface ISettingsService
{
    Models.AppSettings Current { get; }

    Task LoadAsync();

    Task SaveAsync();

    string GetDataDirectory();
}
