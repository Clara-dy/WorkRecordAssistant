using System.Windows;

namespace WorkRecordAssistant.Views;

public partial class CalendarPickerDialog : Window
{
    public DateTime? SelectedDate { get; private set; }

    public CalendarPickerDialog(DateTime initialDate)
    {
        InitializeComponent();
        CalendarControl.SelectedDate = initialDate;
        SelectedDate = initialDate;
        CalendarControl.SelectedDatesChanged += (_, _) =>
        {
            SelectedDate = CalendarControl.SelectedDate;
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = CalendarControl.SelectedDate;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
