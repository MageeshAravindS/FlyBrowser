using System;
using System.Windows;
using System.Windows.Controls;

namespace FocusLock.App.Views;

public partial class WarningOverlay : UserControl
{
    public event EventHandler? Dismissed;

    public WarningOverlay()
    {
        InitializeComponent();
    }

    public void SetCounts(int currentCount, int maxThreshold)
    {
        RunCount.Text = currentCount.ToString();
        RunMax.Text = maxThreshold.ToString();
    }

    private void BtnDismiss_Click(object sender, RoutedEventArgs e)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
