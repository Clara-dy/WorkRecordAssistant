using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 快捷按钮 ViewModel。
/// </summary>
public partial class QuickButtonViewModel : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    public int SortOrder { get; set; }
}
