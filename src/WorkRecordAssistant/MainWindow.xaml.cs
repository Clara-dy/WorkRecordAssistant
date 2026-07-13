using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using WorkRecordAssistant.Behaviors;
using WorkRecordAssistant.Controls;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.Models;
using WorkRecordAssistant.Services;
using WorkRecordAssistant.ViewModels;
using WorkRecordAssistant.Views;

namespace WorkRecordAssistant;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private readonly WindowDragHelper _dragHelper;
    private FloatingWindowBehavior? _floatingBehavior;
    private TrayIconHelper? _trayIcon;
    private bool _isExiting;

    public MainWindow(MainViewModel viewModel, ISettingsService settingsService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        _dragHelper = new WindowDragHelper(this, () => _settingsService.Current.SnapDistancePx);
        DataContext = viewModel;

        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/app-icon.png", UriKind.Absolute));

        _trayIcon = new TrayIconHelper(this, ExitApplication, OnRestoredFromTray);
        _trayIcon.EnsureTrayIconVisible();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SizeChanged += MainWindow_SizeChanged;
    }

    private void OnRestoredFromTray()
    {
        _dragHelper.ClampWindowToWorkArea();
        _floatingBehavior?.EnsureOnScreen();
    }

    private void SetCollapsedChrome(bool collapsed)
    {
        CollapsedStripOverlay.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        MainContent.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_floatingBehavior?.IsCollapsedAtEdge == true && ActualWidth < 40)
            SetCollapsedChrome(true);
    }

    public void PrepareStartupInTray()
    {
        ShowInTaskbar = false;
        ShowActivated = false;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreWindowGeometry();

        _floatingBehavior = new FloatingWindowBehavior(
            this,
            () => _settingsService.Current,
            () => _viewModel.IsEditing,
            SetCollapsedChrome);

        if (_settingsService.Current.SnapEdge != SnapEdge.None)
            _floatingBehavior.RestoreSnapState(_settingsService.Current.SnapEdge);

        await _viewModel.InitializeAsync();
        _trayIcon?.HideToTray();
    }

    private void RestoreWindowGeometry()
    {
        WindowGeometryHelper.SanitizeSettings(_settingsService.Current);
        WindowGeometryHelper.ApplyToWindow(this, _settingsService.Current);
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            StopWindowDrag();
            await SaveWindowStateAsync();
            _trayIcon?.HideToTray();
            return;
        }

        StopWindowDrag();
        await SaveWindowStateAsync();
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private async Task SaveWindowStateAsync()
    {
        if (_floatingBehavior is not null)
        {
            var (left, top, width, height, snapEdge) = _floatingBehavior.GetPersistedState();
            await _viewModel.SaveWindowStateAsync(left, top, width, height, snapEdge);
        }
        else
        {
            await _viewModel.SaveWindowStateAsync(Left, Top, Width, Height, SnapEdge.None);
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        System.Windows.Application.Current.Shutdown();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        if (_floatingBehavior?.IsCollapsedAtEdge == true)
        {
            _floatingBehavior.TryExpandIfCollapsed(animated: false);
            e.Handled = true;
            return;
        }

        StartWindowDrag(e);
    }

    private void CollapsedStripOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _floatingBehavior?.TryExpandIfCollapsed(animated: false);
        e.Handled = true;
    }

    private void CollapsedStripOverlay_MouseEnter(object sender, MouseEventArgs e)
    {
        _floatingBehavior?.TryExpandIfCollapsed(animated: true);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_dragHelper.IsDragging) return;

        if (_floatingBehavior?.IsCollapsedAtEdge == true)
        {
            _floatingBehavior.TryExpandIfCollapsed(animated: false);
            e.Handled = true;
            return;
        }

        if (_floatingBehavior?.IsSnappedAtEdge != true) return;
        if (IsInteractiveElement(e.OriginalSource as DependencyObject)) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (!IsDescendantOf(source, TitleBarGrid)) return;

        StartWindowDrag(e);
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        while (child is not null)
        {
            if (child == ancestor) return true;
            child = VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child);
        }

        return false;
    }

    private void StartWindowDrag(MouseButtonEventArgs e)
    {
        if (_dragHelper.IsDragging) return;

        _floatingBehavior?.BeginDrag();
        _dragHelper.Begin(e);
        CompositionTarget.Rendering += OnDragRendering;
        e.Handled = true;
    }

    private void OnDragRendering(object? sender, EventArgs e)
    {
        if (!_dragHelper.IsDragging) return;

        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            FinishWindowDrag();
            return;
        }

        _dragHelper.Move();
    }

    private void FinishWindowDrag()
    {
        if (!_dragHelper.IsDragging) return;

        CompositionTarget.Rendering -= OnDragRendering;
        _dragHelper.End(out var didMove);
        _floatingBehavior?.EndDrag(didMove);
    }

    private void StopWindowDrag()
    {
        if (!_dragHelper.IsDragging) return;
        FinishWindowDrag();
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button or TextBox or ListBox or ComboBox or Calendar)
                return true;
            if (source is Controls.RecordListView or Controls.RecordListItem)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        FinishWindowDrag();
    }

    private void InputBox_GotFocus(object sender, RoutedEventArgs e) =>
        _viewModel.IsInputFocused = true;

    private void InputBox_LostFocus(object sender, RoutedEventArgs e) =>
        _viewModel.IsInputFocused = false;

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.IsInputPanelVisible = false;
            _viewModel.InputText = string.Empty;
            e.Handled = true;
            return;
        }

        if (IsSubmitEnter(e))
        {
            e.Handled = true;
            InputBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (_viewModel.AddRecordCommand.CanExecute(null))
                await _viewModel.AddRecordCommand.ExecuteAsync(null);
        }
    }

    private static bool IsSubmitEnter(KeyEventArgs e) =>
        (e.Key == Key.Enter || e.Key == Key.Return)
        && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private void InputPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UIElement { IsVisible: true })
            InputBox.Focus();
    }

    private void DateButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CalendarPickerDialog(_viewModel.SelectedDate) { Owner = this };

        if (dialog.ShowDialog() == true && dialog.SelectedDate.HasValue)
            _viewModel.SelectedDate = dialog.SelectedDate.Value;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = App.Services.GetRequiredService<SettingsWindow>();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();

        _viewModel.ShowRecordTime = _settingsService.Current.ShowRecordTime;
        _ = _viewModel.LoadQuickButtonsAsync();
        _ = _viewModel.LoadTasksAsync();
    }

    private async void Minimize_Click(object sender, RoutedEventArgs e)
    {
        StopWindowDrag();
        await SaveWindowStateAsync();
        _trayIcon?.HideToTray();
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        StopWindowDrag();
        await SaveWindowStateAsync();
        _trayIcon?.HideToTray();
    }

    private async void RecordList_DeleteRequested(object? sender, IRecordListItemViewModel e) =>
        await _viewModel.DeleteRecordItemAsync(e);

    private async void RecordList_CompleteRequested(object? sender, IRecordListItemViewModel e) =>
        await _viewModel.CompleteRecordItemAsync(e);

    private void RecordList_OpenArchiveRequested(object? sender, EventArgs e)
    {
        var dialog = App.Services.GetRequiredService<LongTermArchiveDialog>();
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void RecordList_RecordEditingChanged(object? sender, bool editing) =>
        _viewModel.IsRecordEditing = editing;
}
