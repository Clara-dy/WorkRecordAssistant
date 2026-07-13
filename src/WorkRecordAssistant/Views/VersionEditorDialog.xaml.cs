using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkRecordAssistant.Helpers;

namespace WorkRecordAssistant.Views;

public partial class VersionEditorDialog : Window
{
    private enum ActiveField
    {
        VersionNumber,
        VersionInfo
    }

    private ActiveField _activeField = ActiveField.VersionNumber;

    public VersionEditorDialog(string? versionNumber, string? versionInfo)
    {
        InitializeComponent();
        VersionNumberBox.Text = versionNumber ?? string.Empty;
        VersionInfoBox.Text = versionInfo ?? string.Empty;
    }

    public string? VersionNumber =>
        string.IsNullOrWhiteSpace(VersionNumberBox.Text) ? null : VersionNumberBox.Text.Trim();

    public string? VersionInfo =>
        string.IsNullOrWhiteSpace(VersionInfoBox.Text) ? null : VersionInfoBox.Text.Trim();

    private TextBox ActiveBox => _activeField == ActiveField.VersionNumber ? VersionNumberBox : VersionInfoBox;

    private void VersionNumberBox_GotFocus(object sender, RoutedEventArgs e) =>
        _activeField = ActiveField.VersionNumber;

    private void VersionInfoBox_GotFocus(object sender, RoutedEventArgs e) =>
        _activeField = ActiveField.VersionInfo;

    private void Digit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        AppendToActive(button.Content?.ToString() ?? string.Empty);
    }

    private void Dot_Click(object sender, RoutedEventArgs e)
    {
        if (_activeField == ActiveField.VersionNumber) return;
        AppendToActive(".");
    }

    private void AppendToActive(string text)
    {
        var box = ActiveBox;
        var start = box.SelectionStart;
        var length = box.SelectionLength;
        var current = box.Text;
        box.Text = current.Remove(start, length).Insert(start, text);
        box.SelectionStart = start + text.Length;
        box.SelectionLength = 0;
        box.Focus();
    }

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        var box = ActiveBox;
        if (box.SelectionLength > 0)
        {
            var start = box.SelectionStart;
            box.Text = box.Text.Remove(start, box.SelectionLength);
            box.SelectionStart = start;
            box.SelectionLength = 0;
            return;
        }

        if (box.SelectionStart <= 0 || box.Text.Length == 0) return;

        var index = box.SelectionStart - 1;
        box.Text = box.Text.Remove(index, 1);
        box.SelectionStart = index;
        box.Focus();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ActiveBox.Text = string.Empty;
        ActiveBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            TryConfirm();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel_Click(sender, e);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => TryConfirm();

    private void TryConfirm()
    {
        if (!VersionDisplayHelper.IsValidVersionNumber(VersionNumber))
        {
            MessageBox.Show(this, "版本号只能包含数字，不能有小数点。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _activeField = ActiveField.VersionNumber;
            VersionNumberBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }
}
