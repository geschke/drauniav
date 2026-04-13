using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Drauniav;

public partial class VisualizerSettingsDialog : Window
{
    private bool _suppressEvents;

    public VisualizerSettings SelectedSettings { get; private set; }

    public VisualizerSettingsDialog(VisualizerSettings current)
    {
        InitializeComponent();
        SelectedSettings = current.Clone();

        foreach (string preset in VisualizerSettings.PresetNames)
            CboPreset.Items.Add(preset);
        CboPreset.Items.Add("Custom");

        LoadSettingsIntoUi(SelectedSettings);
    }

    private void CboPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;

        if (CboPreset.SelectedItem is not string presetName || presetName == "Custom")
            return;

        LoadSettingsIntoUi(VisualizerSettings.CreatePreset(presetName));
    }

    private void CboType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;

        string type = GetComboValue(CboType, "showfreqs");
        string selectedMode = GetComboValue(CboMode, "line");
        PopulateModeItems(type, selectedMode);
        UpdateShowfreqFieldsEnabled(type == "showfreqs");
    }

    private void ChkSmoothSpectrum_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;

        UpdateShowfreqFieldsEnabled(GetComboValue(CboType, "showfreqs") == "showfreqs");
    }

    private void SldSmoothness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSmoothnessValue == null)
            return;

        TxtSmoothnessValue.Text = $"{(int)Math.Round(SldSmoothness.Value)}";
    }

    private void ChkUseMinAmplitude_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;

        UpdateShowfreqFieldsEnabled(GetComboValue(CboType, "showfreqs") == "showfreqs");
    }

    private void SldMinAmplitude_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMinAmplitudeValue == null)
            return;

        TxtMinAmplitudeValue.Text = $"{(int)Math.Round(SldMinAmplitude.Value)}";
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        SelectedSettings = ReadSettingsFromUi();
        SelectedSettings.PresetName = VisualizerSettings.DetectPresetName(SelectedSettings);
        DialogResult = true;
    }

    private void LoadSettingsIntoUi(VisualizerSettings settings)
    {
        _suppressEvents = true;

        CboPreset.SelectedItem = settings.PresetName;
        if (CboPreset.SelectedItem == null)
            CboPreset.SelectedItem = "Custom";

        SetComboValue(CboType, settings.FilterType);
        SetComboValue(CboChannelMode, settings.ChannelMode);
        PopulateModeItems(settings.FilterType, settings.Mode);
        SetComboValue(CboMode, settings.Mode);
        SetComboValue(CboAScale, settings.AScale);
        SetComboValue(CboFScale, settings.FScale);

        TxtLineThickness.Text = settings.LineThickness.ToString(CultureInfo.InvariantCulture);
        TxtAlpha.Text = VisualizerSettings.FormatDouble(settings.Alpha);
        TxtRate.Text = settings.Rate.ToString(CultureInfo.InvariantCulture);
        TxtVolumeDb.Text = VisualizerSettings.FormatDouble(settings.VolumeDb);
        TxtWinSize.Text = settings.WinSize.ToString(CultureInfo.InvariantCulture);
        TxtColorKeySimilarity.Text = VisualizerSettings.FormatDouble(settings.ColorKeySimilarity);
        TxtColorKeyBlend.Text = VisualizerSettings.FormatDouble(settings.ColorKeyBlend);
        ChkUseColorKey.IsChecked = settings.UseColorKey;
        ChkSmoothSpectrum.IsChecked = settings.SmoothSpectrum;
        SldSmoothness.Value = Math.Clamp(settings.Smoothness, 0, 100);
        TxtSmoothnessValue.Text = $"{(int)Math.Round(SldSmoothness.Value)}";
        ChkAutoHeadroom.IsChecked = settings.AutoHeadroom;
        ChkUseMinAmplitude.IsChecked = settings.UseMinAmplitude;
        SldMinAmplitude.Value = Math.Clamp(settings.MinAmplitude, 0, 100);
        TxtMinAmplitudeValue.Text = $"{(int)Math.Round(SldMinAmplitude.Value)}";
        ChkMirrorHorizontally.IsChecked = settings.MirrorHorizontally;

        UpdateShowfreqFieldsEnabled(settings.FilterType == "showfreqs");
        _suppressEvents = false;
    }

    private VisualizerSettings ReadSettingsFromUi()
    {
        string type = GetComboValue(CboType, "showfreqs");
        string modeFallback = type == "showwaves" ? "line" : "line";

        return new VisualizerSettings
        {
            PresetName = GetComboValue(CboPreset, "Custom"),
            FilterType = type,
            ChannelMode = GetComboValue(CboChannelMode, "mono"),
            Mode = GetComboValue(CboMode, modeFallback),
            LineThickness = ParseInt(TxtLineThickness.Text, 2, 1, 8),
            Alpha = ParseDouble(TxtAlpha.Text, 0.95, 0.05, 1.0),
            Rate = ParseInt(TxtRate.Text, 24, 1, 120),
            VolumeDb = ParseDouble(TxtVolumeDb.Text, 18.0, -96.0, 40.0),
            AScale = GetComboValue(CboAScale, "sqrt"),
            WinSize = ParseInt(TxtWinSize.Text, 4096, 32, 65536),
            FScale = GetComboValue(CboFScale, "log"),
            UseColorKey = ChkUseColorKey.IsChecked == true,
            ColorKeySimilarity = ParseDouble(TxtColorKeySimilarity.Text, 0.10, 0.0, 1.0),
            ColorKeyBlend = ParseDouble(TxtColorKeyBlend.Text, 0.0, 0.0, 1.0),
            SmoothSpectrum = type == "showfreqs" && ChkSmoothSpectrum.IsChecked == true,
            Smoothness = type == "showfreqs" ? (int)Math.Round(SldSmoothness.Value) : 0,
            AutoHeadroom = type == "showfreqs" && ChkAutoHeadroom.IsChecked == true,
            UseMinAmplitude = type == "showfreqs" && ChkUseMinAmplitude.IsChecked == true,
            MinAmplitude = type == "showfreqs" ? (int)Math.Round(SldMinAmplitude.Value) : 0,
            MirrorHorizontally = ChkMirrorHorizontally.IsChecked == true
        };
    }

    private static string GetComboValue(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is string s)
            return s;

        if (combo.SelectedItem is ComboBoxItem item)
        {
            if (item.Tag is string tag)
                return tag;

            if (item.Content is string content)
                return content;
        }

        return fallback;
    }

    private static void SetComboValue(ComboBox combo, string value)
    {
        foreach (var item in combo.Items)
        {
            string compare = item switch
            {
                string s => s,
                ComboBoxItem cbi when cbi.Tag is string tag => tag,
                ComboBoxItem cbi when cbi.Content is string content => content,
                _ => string.Empty
            };

            if (string.Equals(compare, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void PopulateModeItems(string filterType, string selectedMode)
    {
        CboMode.Items.Clear();
        if (filterType == "showwaves")
        {
            CboMode.Items.Add(new ComboBoxItem { Content = "line" });
            CboMode.Items.Add(new ComboBoxItem { Content = "p2p" });
            CboMode.Items.Add(new ComboBoxItem { Content = "point" });
        }
        else
        {
            CboMode.Items.Add(new ComboBoxItem { Content = "line" });
            CboMode.Items.Add(new ComboBoxItem { Content = "bar" });
            CboMode.Items.Add(new ComboBoxItem { Content = "dot" });
        }
        SetComboValue(CboMode, selectedMode);
    }

    private void UpdateShowfreqFieldsEnabled(bool enabled)
    {
        CboAScale.IsEnabled = enabled;
        CboFScale.IsEnabled = enabled;
        TxtWinSize.IsEnabled = enabled;
        AdvancedOptionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        bool smoothEnabled = enabled && ChkSmoothSpectrum.IsChecked == true;
        SldSmoothness.IsEnabled = smoothEnabled;
        LblSmoothness.IsEnabled = smoothEnabled;
        TxtSmoothnessValue.Opacity = smoothEnabled ? 1.0 : 0.5;
        bool minAmpEnabled = enabled && ChkUseMinAmplitude.IsChecked == true;
        SldMinAmplitude.IsEnabled = minAmpEnabled;
        LblMinAmplitude.IsEnabled = minAmpEnabled;
        TxtMinAmplitudeValue.Opacity = minAmpEnabled ? 1.0 : 0.5;
    }

    private static int ParseInt(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            parsed = fallback;
        return Math.Clamp(parsed, min, max);
    }

    private static double ParseDouble(string? value, double fallback, double min, double max)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed)
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            parsed = fallback;
        return Math.Clamp(parsed, min, max);
    }
}
