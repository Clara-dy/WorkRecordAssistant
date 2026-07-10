using System.Windows;
using WorkRecordAssistant.ViewModels;

namespace WorkRecordAssistant.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadQuickButtonsAsync();
    }

    private async void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SaveCommand.CanExecute(null))
            await _viewModel.SaveCommand.ExecuteAsync(null);
        DialogResult = true;
        Close();
    }
}
