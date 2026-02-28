using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace AudioVisualizer;

public partial class ColorPickerDialog : Window
{
    public Color SelectedColor => Picker.SelectedColor;

    /// <summary>True when the window was closed because the user clicked the eyedropper button.
    /// ShowDialog() returns null in this case; the caller should sample a pixel and reopen.</summary>
    public bool EyedropperMode { get; private set; }

    public ColorPickerDialog(Color initialColor)
    {
        InitializeComponent();
        Picker.SelectedColor = initialColor;
    }

    private void BtnEyedropper_Click(object sender, RoutedEventArgs e)
    {
        EyedropperMode = true;
        Close();   // DialogResult stays null; ShowDialog() will return null to caller
    }

    private void BtnUebernehmen_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;

    private void BtnAbbrechen_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private void Dialog_Closing(object sender, CancelEventArgs e)
    {
        // X-button: treat as cancel — but not when closing for eyedropper mode
        if (!EyedropperMode && DialogResult == null)
            DialogResult = false;
    }
}
