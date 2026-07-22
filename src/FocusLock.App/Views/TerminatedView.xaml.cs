using System.Windows.Controls;

namespace FocusLock.App.Views;

public partial class TerminatedView : UserControl
{
    public TerminatedView()
    {
        InitializeComponent();
    }

    public void SetDetails(string sessionId, string reason, string exitShortcut = "Ctrl+Alt+Shift+Q")
    {
        TxtSessionId.Text = sessionId;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            TxtReason.Text = reason;
        }
        TxtProctorHint.Text = $"Proctors: Press {exitShortcut} to exit";
    }
}
