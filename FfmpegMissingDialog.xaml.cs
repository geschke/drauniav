using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Drauniav;

public partial class FfmpegMissingDialog : Window
{
    private string _homepageUrl = string.Empty;

    public FfmpegMissingDialog()
    {
        InitializeComponent();

        _homepageUrl = Loc.Get("FfmpegMissingHomepageUrl");
        TxtFfmpegHomepageValue.Text = _homepageUrl;
        if (Uri.TryCreate(_homepageUrl, UriKind.Absolute, out Uri? homepageUri))
            LnkFfmpegHomepage.NavigateUri = homepageUri;
    }

    private void LnkFfmpegHomepage_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is null)
            return;

        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
