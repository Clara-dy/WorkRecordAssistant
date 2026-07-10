namespace WorkRecordAssistant.Models;

/// <summary>
/// 顶部快捷按钮实体。
/// </summary>
public class QuickButton
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsVisible { get; set; } = true;
}
