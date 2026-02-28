using System.Windows;

namespace AudioVisualizer;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        string version = typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        TxtVersionValue.Text = string.Format(Loc.Get("AboutVersionValueTemplate"), version);
        TxtAuthorValue.Text = Loc.Get("AboutAuthorValue");
        TxtHomepageValue.Text = Loc.Get("AboutHomepageValue");
        TxtGithubValue.Text = Loc.Get("AboutGithubValue");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
