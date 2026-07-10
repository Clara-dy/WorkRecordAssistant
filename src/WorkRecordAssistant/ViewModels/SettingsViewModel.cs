using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WorkRecordAssistant.Models;
using WorkRecordAssistant.Services;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 设置窗口 ViewModel。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDataService _dataService;

    public SettingsViewModel(ISettingsService settingsService, IDataService dataService)
    {
        _settingsService = settingsService;
        _dataService = dataService;
        LoadFromSettings();
    }

    public ObservableCollection<QuickButtonViewModel> QuickButtons { get; } = [];

    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private ThemeMode _themeMode = ThemeMode.Auto;
    [ObservableProperty] private int _autoHideDelayMs = 500;
    [ObservableProperty] private int _animationDurationMs = 250;
    [ObservableProperty] private int _snapDistancePx = 20;
    [ObservableProperty] private int _hiddenStripWidthPx = 12;
    [ObservableProperty] private RecordSortOrder _defaultSortOrder = RecordSortOrder.NewestFirst;
    [ObservableProperty] private bool _showRecordTime;
    [ObservableProperty] private string _copyTemplate = string.Empty;
    [ObservableProperty] private string _copyItemTemplate = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public Array ThemeModes => Enum.GetValues(typeof(ThemeMode));
    public Array SortOrders => Enum.GetValues(typeof(RecordSortOrder));

    public async Task LoadQuickButtonsAsync()
    {
        var buttons = await _dataService.GetQuickButtonsAsync();
        QuickButtons.Clear();
        foreach (var button in buttons.OrderBy(b => b.SortOrder))
        {
            QuickButtons.Add(new QuickButtonViewModel
            {
                Id = button.Id,
                Name = button.Name,
                Url = button.Url,
                IsVisible = button.IsVisible,
                SortOrder = button.SortOrder
            });
        }
    }

    [RelayCommand]
    private void AddQuickButton()
    {
        QuickButtons.Add(new QuickButtonViewModel
        {
            Name = "新按钮",
            Url = "https://",
            IsVisible = true
        });
    }

    [RelayCommand]
    private void RemoveQuickButton(QuickButtonViewModel? button)
    {
        if (button is not null)
            QuickButtons.Remove(button);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _settingsService.Current;
        settings.AutoStart = AutoStart;
        settings.ThemeMode = ThemeMode;
        settings.AutoHideDelayMs = AutoHideDelayMs;
        settings.AnimationDurationMs = AnimationDurationMs;
        settings.SnapDistancePx = SnapDistancePx;
        settings.HiddenStripWidthPx = HiddenStripWidthPx;
        settings.DefaultSortOrder = DefaultSortOrder;
        settings.ShowRecordTime = ShowRecordTime;
        settings.CopyTemplate = CopyTemplate;
        settings.CopyItemTemplate = CopyItemTemplate;

        await _settingsService.SaveAsync();

        var buttons = QuickButtons.Select((b, i) => new QuickButton
        {
            Id = b.Id,
            Name = b.Name,
            Url = b.Url,
            IsVisible = b.IsVisible,
            SortOrder = i
        });
        await _dataService.SaveQuickButtonsAsync(buttons);

        var exePath = Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrEmpty(exePath))
        {
            StartupService.SetAutoStart(AutoStart, exePath);
            if (AutoStart)
                StartMenuShortcutService.EnsureShortcut(exePath);
        }

        ThemeService.ApplyTheme(ThemeMode);
        StatusMessage = "设置已保存";
    }

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = $"workrecords-export-{DateTime.Now:yyyyMMdd}.json"
        };

        if (dialog.ShowDialog() != true) return;
        await _dataService.ExportAllAsync(dialog.FileName);
        StatusMessage = "导出成功";
    }

    [RelayCommand]
    private async Task ImportDataAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON 文件|*.json"
        };

        if (dialog.ShowDialog() != true) return;
        await _dataService.ImportAllAsync(dialog.FileName);
        await LoadQuickButtonsAsync();
        StatusMessage = "导入成功";
    }

    [RelayCommand]
    private async Task BackupDataAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 备份|*.json",
            FileName = $"workrecords-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != true) return;
        await _dataService.BackupAsync(dialog.FileName);
        StatusMessage = "备份成功";
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;
        AutoStart = s.AutoStart;
        ThemeMode = s.ThemeMode;
        AutoHideDelayMs = s.AutoHideDelayMs;
        AnimationDurationMs = s.AnimationDurationMs;
        SnapDistancePx = s.SnapDistancePx;
        HiddenStripWidthPx = s.HiddenStripWidthPx;
        DefaultSortOrder = s.DefaultSortOrder;
        ShowRecordTime = s.ShowRecordTime;
        CopyTemplate = s.CopyTemplate;
        CopyItemTemplate = s.CopyItemTemplate;
    }
}
