using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
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

        LoadAboutLogo();
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

    private void LoadAboutLogo()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Drauniav-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "Drauniav-logo.jpg"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "Drauniav-logo.jpeg")
        };

        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(candidate, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            AboutLogoImage.Source = bitmap;
            AboutLogoImage.Visibility = Visibility.Visible;
            AboutHeaderIcon.Visibility = Visibility.Collapsed;
            return;
        }
    }
}
