using System.Windows;
using WorkRecordAssistant.ViewModels;

namespace WorkRecordAssistant.Views;

public partial class LongTermArchiveDialog : Window
{
    private readonly LongTermArchiveViewModel _viewModel;

    public LongTermArchiveDialog(LongTermArchiveViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += LongTermArchiveDialog_Loaded;
    }

    private async void LongTermArchiveDialog_Loaded(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
