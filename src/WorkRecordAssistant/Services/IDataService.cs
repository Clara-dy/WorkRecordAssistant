namespace WorkRecordAssistant.Services;

/// <summary>
/// 数据持久化抽象，便于后续扩展云同步等能力。
/// </summary>
public interface IDataService
{
    Task InitializeAsync();

    Task<IReadOnlyList<Models.WorkRecord>> GetRecordsAsync(string date);

    Task<Models.WorkRecord> AddRecordAsync(string date, string content);

    Task UpdateRecordAsync(Models.WorkRecord record);

    Task UpdateRecordContentAsync(int id, string content);

    Task ReorderRecordsAsync(string date, IReadOnlyList<int> orderedIds);

    Task DeleteRecordAsync(int id);

    Task<IReadOnlyList<Models.LongTermTask>> GetLongTermTasksAsync();

    Task<Models.LongTermTask> AddLongTermTaskAsync(string content);

    Task UpdateLongTermTaskContentAsync(int id, string content);

    Task ReorderLongTermTasksAsync(IReadOnlyList<int> orderedIds);

    Task DeleteLongTermTaskAsync(int id);

    Task CompleteLongTermTaskAsync(int id);

    Task<IReadOnlyList<Models.LongTermTask>> GetArchivedLongTermTasksAsync();

    Task<IReadOnlyList<Models.QuickButton>> GetQuickButtonsAsync();

    Task SaveQuickButtonsAsync(IEnumerable<Models.QuickButton> buttons);

    Task ExportAllAsync(string filePath);

    Task ImportAllAsync(string filePath);

    Task BackupAsync(string filePath);
}
