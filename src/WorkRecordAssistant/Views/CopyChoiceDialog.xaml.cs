using System.Windows;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Views;

public partial class CopyChoiceDialog : Window
{
    public CopyChoiceDialog()
    {
        InitializeComponent();
    }

    public CopyTaskScope SelectedScope { get; private set; }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        SelectedScope = CopyTaskScope.All;
        DialogResult = true;
        Close();
    }

    private void CopyCompleted_Click(object sender, RoutedEventArgs e)
    {
        SelectedScope = CopyTaskScope.CompletedOnly;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
