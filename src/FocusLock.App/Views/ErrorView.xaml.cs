using System;
using System.Windows;
using System.Windows.Controls;

namespace FocusLock.App.Views;

public partial class ErrorView : UserControl
{
    public event EventHandler? ExitRequested;

    public ErrorView()
    {
        InitializeComponent();
    }

    public void SetError(string details)
    {
        TxtErrorDetails.Text = details;
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
