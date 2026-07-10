namespace WorkRecordAssistant.Models;

/// <summary>
/// 记录排序方式。
/// </summary>
public enum RecordSortOrder
{
    NewestFirst,
    OldestFirst
}

/// <summary>
/// 主题模式。
/// </summary>
public enum ThemeMode
{
    Auto,
    Light,
    Dark
}

/// <summary>
/// 窗口吸附边。
/// </summary>
public enum SnapEdge
{
    None,
    Left,
    Right
}

/// <summary>
/// 应用全局设置。
/// </summary>
public class AppSettings
{
    public bool AutoStart { get; set; }

    public ThemeMode ThemeMode { get; set; } = ThemeMode.Auto;

    public int AutoHideDelayMs { get; set; } = 500;

    public int AnimationDurationMs { get; set; } = 250;

    public int SnapDistancePx { get; set; } = 20;

    public int HiddenStripWidthPx { get; set; } = 20;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 320;

    public double WindowHeight { get; set; } = 520;

    public SnapEdge SnapEdge { get; set; } = SnapEdge.None;

    public RecordSortOrder DefaultSortOrder { get; set; } = RecordSortOrder.NewestFirst;

    public bool ShowRecordTime { get; set; }

    public string CopyTemplate { get; set; } =
        "{date}\n\n{items}";

    public string CopyItemTemplate { get; set; } =
        "{index}.\n{content}";
}
