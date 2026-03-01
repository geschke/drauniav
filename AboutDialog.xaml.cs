using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Drauniav;

public partial class AboutDialog : Window
{
    private string _githubUrl = string.Empty;

    public AboutDialog()
    {
        InitializeComponent();

        string version = typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        TxtVersionValue.Text = string.Format(Loc.Get("AboutVersionValueTemplate"), version);
        TxtAuthorValue.Text = Loc.Get("AboutAuthorValue");
        TxtEmailValue.Text = Loc.Get("AboutEmailValue");
        TxtLicenseValue.Text = Loc.Get("AboutLicenseValue");

        _githubUrl = Loc.Get("AboutGithubValue");
        TxtGithubValue.Text = _githubUrl;
        if (Uri.TryCreate(_githubUrl, UriKind.Absolute, out Uri? githubUri))
            LnkGithub.NavigateUri = githubUri;
    }

    private void LnkGithub_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri is not null)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
