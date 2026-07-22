using System;
using System.Windows;
using System.Windows.Controls;

namespace FocusLock.App.Views;

public partial class CompletionView : UserControl
{
    public event EventHandler? ExitRequested;

    public CompletionView()
    {
        InitializeComponent();
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
