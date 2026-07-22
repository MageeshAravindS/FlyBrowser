using System.Windows;
using System.Windows.Input;
using FocusLock.Security;

namespace FocusLock.App.Views;

public partial class ExitDialog : Window
{
    private readonly ExitAuthorizationService _authService;

    public bool IsAuthenticated { get; private set; }

    public ExitDialog(ExitAuthorizationService authService)
    {
        InitializeComponent();
        _authService = authService;
        Loaded += (s, e) =>
        {
            Activate();
            Focus();
            TxtPassword.Focus();
        };
    }

    private void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        Authenticate();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        IsAuthenticated = false;
        DialogResult = false;
        Close();
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Authenticate();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Authenticate()
    {
        string input = TxtPassword.Password;
        if (_authService.VerifyPassword(input))
        {
            IsAuthenticated = true;
            DialogResult = true;
            Close();
        }
        else
        {
            TxtError.Visibility = Visibility.Visible;
            TxtPassword.SelectAll();
            TxtPassword.Focus();
        }
    }
}
