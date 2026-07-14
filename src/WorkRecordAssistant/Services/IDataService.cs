namespace WorkRecordAssistant.Services;

/// <summary>
/// 数据持久化抽象，便于后续扩展云同步等能力。
/// </summary>
public interface IDataService
{
    Task InitializeAsync();

    Task<IReadOnlyList<Models.WorkRecord>> GetRecordsAsync(string date);

    Task<IReadOnlyList<Models.WorkRecord>> GetRecordsForDisplayAsync(string viewDate);

    Task SetRecordStarredAsync(int id, bool isStarred);

    Task<Models.WorkRecord> AddRecordAsync(string date, string content);

    Task CompleteRecordAsync(int id, string completionDate);

    Task UncompleteRecordAsync(int id);

    Task UpdateRecordAsync(Models.WorkRecord record);

    Task UpdateRecordContentAsync(int id, string content);

    Task UpdateRecordVersionAsync(int id, string? versionNumber, string? versionInfo);

    Task ReorderRecordsAsync(IReadOnlyList<int> orderedIds);

    Task DeleteRecordAsync(int id);

    Task<IReadOnlyList<Models.LongTermTask>> GetLongTermTasksAsync();

    Task<Models.LongTermTask> AddLongTermTaskAsync(string content);

    Task UpdateLongTermTaskContentAsync(int id, string content);

    Task ReorderLongTermTasksAsync(IReadOnlyList<int> orderedIds);

    Task DeleteLongTermTaskAsync(int id);

    Task CompleteLongTermTaskAsync(int id);

    Task UncompleteLongTermTaskAsync(int id);

    Task<IReadOnlyList<Models.LongTermTask>> GetArchivedLongTermTasksAsync();

    Task<IReadOnlyList<Models.WorkRecord>> GetCompletedWorkRecordsAsync();

    Task<IReadOnlyDictionary<int, IReadOnlyList<Models.TaskSubItem>>> GetSubTasksAsync(
        Models.TaskParentType parentType, IEnumerable<int> parentIds);

    Task<Models.TaskSubItem> AddSubTaskAsync(Models.TaskParentType parentType, int parentId, string content);

    Task UpdateSubTaskContentAsync(int id, string content);

    Task UpdateSubTaskVersionAsync(int id, string? versionNumber, string? versionInfo);

    Task CompleteSubTaskAsync(int id);

    Task UncompleteSubTaskAsync(int id);

    Task DeleteSubTaskAsync(int id);

    Task DeleteSubTasksForParentAsync(Models.TaskParentType parentType, int parentId);

    Task<IReadOnlyList<Models.QuickButton>> GetQuickButtonsAsync();

    Task SaveQuickButtonsAsync(IEnumerable<Models.QuickButton> buttons);

    Task<IReadOnlyList<Models.SubTaskTemplate>> GetSubTaskTemplatesAsync();

    Task SaveSubTaskTemplatesAsync(IEnumerable<Models.SubTaskTemplate> templates);

    Task ExportAllAsync(string filePath);

    Task ImportAllAsync(string filePath);

    Task BackupAsync(string filePath);
}
