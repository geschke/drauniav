using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Drauniav;

public partial class VideoOptionsDialog : Window
{
    private const int MinResolution = 64;
    private const int MaxResolution = 8192;

    private readonly int _imageWidth;
    private readonly int _imageHeight;
    private bool _updatingControls;
    private bool _isInitializing = true;
    private int _aspectNumerator = 1;
    private int _aspectDenominator = 1;

    public VideoOutputSettings SelectedOptions { get; private set; }

    public VideoOptionsDialog(int imageWidth, int imageHeight, VideoOutputSettings current)
    {
        InitializeComponent();

        _imageWidth = Math.Max(MinResolution, imageWidth);
        _imageHeight = Math.Max(MinResolution, imageHeight);

        SelectedOptions = current.Clone();
        if (SelectedOptions.UseImageResolution || SelectedOptions.Width <= 0 || SelectedOptions.Height <= 0)
            SelectedOptions.ResetToImage(_imageWidth, _imageHeight);

        int sliderMax = Math.Max(MaxResolution, Math.Max(_imageWidth, _imageHeight) * 2);
        SldWidth.Maximum = sliderMax;
        SldHeight.Maximum = sliderMax;

        SelectedOptions.Width = Math.Clamp(SelectedOptions.Width, MinResolution, sliderMax);
        SelectedOptions.Height = Math.Clamp(SelectedOptions.Height, MinResolution, sliderMax);
        if (SelectedOptions.KeepAspectRatio
            && SelectedOptions.LockedAspectNumerator > 0
            && SelectedOptions.LockedAspectDenominator > 0)
        {
            _aspectNumerator = SelectedOptions.LockedAspectNumerator;
            _aspectDenominator = SelectedOptions.LockedAspectDenominator;
        }
        else
        {
            SetLockedAspectFromResolution(SelectedOptions.Width, SelectedOptions.Height);
        }

        _updatingControls = true;
        ChkKeepAspectRatio.IsChecked = SelectedOptions.KeepAspectRatio;
        _updatingControls = false;
        SetResolutionControls(SelectedOptions.Width, SelectedOptions.Height);
        _isInitializing = false;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        CommitAllInputs();
        int width = (int)Math.Round(SldWidth.Value);
        int height = (int)Math.Round(SldHeight.Value);
        width = NormalizeEvenDimension(width);
        height = NormalizeEvenDimension(height);

        SelectedOptions.Width = width;
        SelectedOptions.Height = height;
        SelectedOptions.KeepAspectRatio = ChkKeepAspectRatio.IsChecked == true;
        SelectedOptions.UseImageResolution = width == _imageWidth && height == _imageHeight;
        SelectedOptions.LockedAspectNumerator = _aspectNumerator;
        SelectedOptions.LockedAspectDenominator = _aspectDenominator;
        DialogResult = true;
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        SetLockedAspectFromResolution(_imageWidth, _imageHeight);
        ChkKeepAspectRatio.IsChecked = true;
        SetResolutionControls(_imageWidth, _imageHeight);
    }

    private void ChkKeepAspectRatio_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingControls || _isInitializing)
            return;

        if (ChkKeepAspectRatio.IsChecked == true)
        {
            int width = ParseInt(TxtWidth.Text, (int)Math.Round(SldWidth.Value), MinResolution, (int)SldWidth.Maximum);
            int height = ParseInt(TxtHeight.Text, (int)Math.Round(SldHeight.Value), MinResolution, (int)SldHeight.Maximum);
            SetLockedAspectFromResolution(width, height);
            ApplyWidthChange(width);
        }
    }

    private void SldWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingControls || _isInitializing)
            return;

        ApplyWidthChange((int)Math.Round(e.NewValue));
    }

    private void SldHeight_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingControls || _isInitializing)
            return;

        ApplyHeightChange((int)Math.Round(e.NewValue));
    }

    private void TxtWidth_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Keep typing flow stable; values are committed on Enter or focus loss.
    }

    private void TxtHeight_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Keep typing flow stable; values are committed on Enter or focus loss.
    }

    private void TxtWidth_LostFocus(object sender, RoutedEventArgs e) => CommitWidthInput();

    private void TxtHeight_LostFocus(object sender, RoutedEventArgs e) => CommitHeightInput();

    private void TxtWidth_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitWidthInput();
        e.Handled = true;
    }

    private void TxtHeight_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitHeightInput();
        e.Handled = true;
    }

    private void TxtNumeric_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigitsOnly(e.Text);
    }

    private void TxtNumeric_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        string text = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!IsDigitsOnly(text))
            e.CancelCommand();
    }

    private void ApplyWidthChange(int width)
    {
        if (TxtHeight == null || TxtWidth == null || SldWidth == null || SldHeight == null)
            return;

        int maxWidth = (int)SldWidth.Maximum;
        int maxHeight = (int)SldHeight.Maximum;
        if (ChkKeepAspectRatio.IsChecked == true)
        {
            width = Math.Clamp(width, MinResolution, maxWidth);
            int height = (int)Math.Round(width * (_aspectDenominator / (double)_aspectNumerator));

            if (height < MinResolution)
            {
                height = MinResolution;
                width = (int)Math.Round(height * (_aspectNumerator / (double)_aspectDenominator));
            }
            else if (height > maxHeight)
            {
                height = maxHeight;
                width = (int)Math.Round(height * (_aspectNumerator / (double)_aspectDenominator));
            }

            width = Math.Clamp(width, MinResolution, maxWidth);
            height = Math.Clamp(height, MinResolution, maxHeight);
            SetResolutionControls(width, height);
            return;
        }

        width = Math.Clamp(width, MinResolution, maxWidth);
        int resolvedHeight = ParseInt(TxtHeight.Text, (int)Math.Round(SldHeight.Value), MinResolution, maxHeight);
        SetResolutionControls(width, resolvedHeight);
    }

    private void ApplyHeightChange(int height)
    {
        if (TxtHeight == null || TxtWidth == null || SldWidth == null || SldHeight == null)
            return;

        int maxWidth = (int)SldWidth.Maximum;
        int maxHeight = (int)SldHeight.Maximum;
        if (ChkKeepAspectRatio.IsChecked == true)
        {
            height = Math.Clamp(height, MinResolution, maxHeight);
            int width = (int)Math.Round(height * (_aspectNumerator / (double)_aspectDenominator));

            if (width < MinResolution)
            {
                width = MinResolution;
                height = (int)Math.Round(width * (_aspectDenominator / (double)_aspectNumerator));
            }
            else if (width > maxWidth)
            {
                width = maxWidth;
                height = (int)Math.Round(width * (_aspectDenominator / (double)_aspectNumerator));
            }

            width = Math.Clamp(width, MinResolution, maxWidth);
            height = Math.Clamp(height, MinResolution, maxHeight);
            SetResolutionControls(width, height);
            return;
        }

        height = Math.Clamp(height, MinResolution, maxHeight);
        int resolvedWidth = ParseInt(TxtWidth.Text, (int)Math.Round(SldWidth.Value), MinResolution, maxWidth);
        SetResolutionControls(resolvedWidth, height);
    }

    private void SetResolutionControls(int width, int height)
    {
        _updatingControls = true;

        width = Math.Clamp(width, MinResolution, (int)SldWidth.Maximum);
        height = Math.Clamp(height, MinResolution, (int)SldHeight.Maximum);

        TxtWidth.Text = width.ToString(CultureInfo.InvariantCulture);
        TxtHeight.Text = height.ToString(CultureInfo.InvariantCulture);
        SldWidth.Value = width;
        SldHeight.Value = height;

        TxtCurrentResolution.Text = $"{width} x {height} px";
        TxtCurrentAspect.Text = ChkKeepAspectRatio?.IsChecked == true
            ? $"{_aspectNumerator}:{_aspectDenominator}"
            : BuildAspectRatioLabel(width, height);

        _updatingControls = false;
    }

    private void CommitWidthInput()
    {
        if (_updatingControls || TxtWidth == null || SldWidth == null)
            return;

        int value = ParseInt(TxtWidth.Text, (int)Math.Round(SldWidth.Value), MinResolution, (int)SldWidth.Maximum);
        ApplyWidthChange(value);
    }

    private void CommitHeightInput()
    {
        if (_updatingControls || TxtHeight == null || SldHeight == null)
            return;

        int value = ParseInt(TxtHeight.Text, (int)Math.Round(SldHeight.Value), MinResolution, (int)SldHeight.Maximum);
        ApplyHeightChange(value);
    }

    private void CommitAllInputs()
    {
        if (TxtWidth?.IsKeyboardFocusWithin == true)
        {
            CommitWidthInput();
            return;
        }

        if (TxtHeight?.IsKeyboardFocusWithin == true)
        {
            CommitHeightInput();
            return;
        }

        int width = ParseInt(TxtWidth?.Text, (int)Math.Round(SldWidth.Value), MinResolution, (int)SldWidth.Maximum);
        int height = ParseInt(TxtHeight?.Text, (int)Math.Round(SldHeight.Value), MinResolution, (int)SldHeight.Maximum);
        if (ChkKeepAspectRatio.IsChecked == true)
            ApplyWidthChange(width);
        else
            SetResolutionControls(width, height);
    }

    private static int ParseInt(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            parsed = fallback;
        return Math.Clamp(parsed, min, max);
    }

    private static bool IsDigitsOnly(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (char c in text)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return true;
    }

    private static string BuildAspectRatioLabel(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return "-";

        int gcd = GreatestCommonDivisor(width, height);
        return $"{width / gcd}:{height / gcd}";
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            int tmp = a % b;
            a = b;
            b = tmp;
        }
        return a == 0 ? 1 : a;
    }

    private static int NormalizeEvenDimension(int value)
    {
        value = Math.Max(MinResolution, value);
        return (value % 2 == 0) ? value : value - 1;
    }

    private void SetLockedAspectFromResolution(int width, int height)
    {
        int safeWidth = Math.Max(1, width);
        int safeHeight = Math.Max(1, height);
        int gcd = GreatestCommonDivisor(safeWidth, safeHeight);
        _aspectNumerator = Math.Max(1, safeWidth / gcd);
        _aspectDenominator = Math.Max(1, safeHeight / gcd);
    }

}
