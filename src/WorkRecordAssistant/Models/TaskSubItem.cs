namespace WorkRecordAssistant.Models;

/// <summary>
/// 任务子项，可挂载于短期或长期任务。
/// </summary>
public class TaskSubItem
{
    public int Id { get; set; }

    public TaskParentType ParentType { get; set; }

    public int ParentId { get; set; }

    public string Content { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int SortOrder { get; set; }

    /// <summary>纯数字版本号，不含小数点。</summary>
    public string? VersionNumber { get; set; }

    /// <summary>版本信息说明。</summary>
    public string? VersionInfo { get; set; }
}

/// <summary>
/// 子任务所属父任务类型。
/// </summary>
public enum TaskParentType
{
    WorkRecord,
    LongTermTask
}

public static class TaskParentTypeExtensions
{
    public static string ToDbValue(this TaskParentType type) => type switch
    {
        TaskParentType.WorkRecord => "WorkRecord",
        TaskParentType.LongTermTask => "LongTermTask",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static TaskParentType FromDbValue(string value) => value switch
    {
        "WorkRecord" => TaskParentType.WorkRecord,
        "LongTermTask" => TaskParentType.LongTermTask,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value)
    };
}
