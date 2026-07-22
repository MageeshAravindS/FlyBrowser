using System.Windows;
using System.Windows.Input;

namespace FocusLock.App.Views;

public partial class WarningDialog : Window
{
    public WarningDialog(int currentCount, int maxCount)
    {
        InitializeComponent();
        SetCounts(currentCount, maxCount);

        Loaded += (s, e) =>
        {
            Activate();
            Focus();
            BtnDismiss.Focus();
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.Space)
            {
                CloseDialog();
                e.Handled = true;
            }
        };
    }

    public void SetCounts(int currentCount, int maxCount)
    {
        RunCount.Text = currentCount.ToString();
        RunMax.Text = maxCount.ToString();
    }

    private void BtnDismiss_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
    }

    private void CloseDialog()
    {
        DialogResult = true;
        Close();
    }
}
