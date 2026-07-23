using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace FocusLock.App.Views;

public partial class LoginPromptView : UserControl
{
    public event EventHandler? OpenBrowserRequested;
    public event EventHandler? CheckLoginRequested;

    public LoginPromptView()
    {
        InitializeComponent();
    }

    public void SetStatusText(string status)
    {
        TxtStatus.Text = status;
    }

    private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        OpenBrowserRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnCheckLogin_Click(object sender, RoutedEventArgs e)
    {
        CheckLoginRequested?.Invoke(this, EventArgs.Empty);
    }
}
