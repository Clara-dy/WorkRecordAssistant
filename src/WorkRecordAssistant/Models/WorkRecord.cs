namespace WorkRecordAssistant.Models;

/// <summary>
/// 单条工作记录实体。
/// </summary>
public class WorkRecord
{
    public int Id { get; set; }

    /// <summary>日期键，格式 yyyy-MM-dd。</summary>
    public string Date { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int SortOrder { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public bool IsStarred { get; set; }
}
