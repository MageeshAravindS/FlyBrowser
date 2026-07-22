using System.Windows.Controls;

namespace FocusLock.App.Views;

public partial class LoadingView : UserControl
{
    public LoadingView()
    {
        InitializeComponent();
    }

    public void UpdateStatus(string statusText, string? appName = null)
    {
        TxtStatus.Text = statusText;
        if (!string.IsNullOrWhiteSpace(appName))
        {
            TxtAppName.Text = appName;
        }
    }
}
