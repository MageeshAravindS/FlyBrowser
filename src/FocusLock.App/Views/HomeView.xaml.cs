using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FocusLock.App.Views;

public partial class HomeView : UserControl
{
    public event EventHandler<string>? CodeSubmitted;

    public HomeView()
    {
        InitializeComponent();
        Loaded += (s, e) => FocusAccessCodeInput();
    }

    public void SetAppName(string appName)
    {
        TxtAppName.Text = string.IsNullOrWhiteSpace(appName) ? "FlyLock Browser" : appName;
    }

    public void FocusAccessCodeInput()
    {
        Dispatcher.InvokeAsync(() =>
        {
            TxtAccessCode.Focus();
            Keyboard.Focus(TxtAccessCode);
        });
    }

    public void ShowTerminationNotice(string message)
    {
        TxtCodeError.Text = message;
        TxtCodeError.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        TxtCodeError.Visibility = Visibility.Visible;
        TxtAccessCode.Clear();
        FocusAccessCodeInput();
    }

    private void BtnStartExam_Click(object sender, RoutedEventArgs e)
    {
        SubmitCode();
    }

    private void TxtAccessCode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitCode();
            e.Handled = true;
        }
    }

    private void SubmitCode()
    {
        string code = TxtAccessCode.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            TxtCodeError.Text = "Please enter a valid assessment code to continue.";
            TxtCodeError.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TxtCodeError.Visibility = Visibility.Visible;
            FocusAccessCodeInput();
            return;
        }

        TxtCodeError.Visibility = Visibility.Collapsed;
        CodeSubmitted?.Invoke(this, code);
    }
}
