using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Views;

public partial class SubTaskTemplateManageDialog : Window
{
    private readonly ObservableCollection<SubTaskTemplate> _items = [];

    public SubTaskTemplateManageDialog(IEnumerable<SubTaskTemplate> existing)
    {
        InitializeComponent();
        foreach (var item in existing)
        {
            _items.Add(new SubTaskTemplate
            {
                Id = item.Id,
                Content = item.Content,
                SortOrder = item.SortOrder
            });
        }

        TemplateList.ItemsSource = _items;
        Loaded += (_, _) => NewTemplateBox.Focus();
    }

    public IReadOnlyList<SubTaskTemplate> ResultTemplates { get; private set; } = [];

    private void AddButton_Click(object sender, RoutedEventArgs e) => TryAddTemplate();

    private void NewTemplateBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryAddTemplate();
        }
    }

    private void TryAddTemplate()
    {
        var text = NewTemplateBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        _items.Add(new SubTaskTemplate
        {
            Content = text,
            SortOrder = _items.Count
        });
        NewTemplateBox.Clear();
        NewTemplateBox.Focus();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SubTaskTemplate item })
            _items.Remove(item);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultTemplates = _items
            .Where(t => !string.IsNullOrWhiteSpace(t.Content))
            .Select((t, i) => new SubTaskTemplate
            {
                Content = t.Content.Trim(),
                SortOrder = i
            })
            .ToList();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
