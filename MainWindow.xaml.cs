using System.Diagnostics;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Drauniav;

public partial class MainWindow : Window
{
    // â”€â”€ Color state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private string _spectrumColor = "0xFFFFFF";   // FFmpeg 0xRRGGBB hex string
    private VisualizerSettings _visualizerSettings = VisualizerSettings.CreateDefault();
    private VideoOutputSettings _videoOutputSettings = VideoOutputSettings.CreateDefault();
    private BitmapSource? _sourceImageBitmap;

    // â”€â”€ Drag state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const double MinOverlayWidthPreview = 40.0;
    private const double MinOverlayHeightPreview = 20.0;
    private const double SnapThresholdPreview = 10.0;
    private const double HandleSizePreview = 6.0;
    private const double MoveEdgeExclusionPreview = 5.0;
    private const int EyedropperSampleSize = 15;
    private const double EyedropperLensSize = 120.0;
    private const double EyedropperLensOffset = 20.0;
    private const int MinFineRotationDeg = -30;
    private const int MaxFineRotationDeg = 30;
    private const int FineRotationStepDeg = 1;

    private bool _overlayInitialized;
    private bool _isDragging;
    private bool _snapCenterX;
    private bool _snapCenterY;
    private int _baseRotationDeg;
    private int _fineRotationDeg;

    // Stored in output video pixels for direct FFmpeg usage.
    private double _overlayX;
    private double _overlayY;
    private double _overlayWidth;
    private double _overlayHeight;

    private Point _dragStartMouse;
    private Rect _dragStartRectPreview;
    private OverlayDragMode _dragMode;
    private System.Windows.Controls.Primitives.Popup? _eyedropperMagnifierPopup;
    private Image? _eyedropperMagnifierImage;

    private enum OverlayDragMode
    {
        None,
        Move,
        ResizeLeft,
        ResizeTopLeft,
        ResizeTop,
        ResizeTopRight,
        ResizeRight,
        ResizeBottomRight,
        ResizeBottom,
        ResizeBottomLeft
    }
    private static readonly HashSet<string> CommonAspectRatios = new(StringComparer.Ordinal)
    {
        "16:9", "4:3", "9:16", "1:1", "3:4", "4:5", "5:4", "3:2", "2:3", "21:9", "9:21"
    };

    public MainWindow()
    {
        InitializeComponent();
        LocalizationManager.LanguageChanged += LocalizationManager_LanguageChanged;
        UpdateLanguageMenuChecks();
        UpdateVisualizerSummary();
        UpdateVideoSummary();
        UpdateCommandPreview();
        UpdateRotationAngleDisplay();
        ImgPreview.SizeChanged         += (_, _) => UpdateOverlay();
        OverlayBar.MouseLeftButtonDown += OverlayBar_MouseDown;
        OverlayBar.MouseLeftButtonUp   += OverlayBar_MouseUp;
        OverlayBar.MouseMove           += OverlayBar_MouseMove;

        AttachHandle(HandleTopLeft, OverlayDragMode.ResizeTopLeft);
        AttachHandle(HandleTop, OverlayDragMode.ResizeTop);
        AttachHandle(HandleTopRight, OverlayDragMode.ResizeTopRight);
        AttachHandle(HandleRight, OverlayDragMode.ResizeRight);
        AttachHandle(HandleBottomRight, OverlayDragMode.ResizeBottomRight);
        AttachHandle(HandleBottom, OverlayDragMode.ResizeBottom);
        AttachHandle(HandleBottomLeft, OverlayDragMode.ResizeBottomLeft);
        AttachHandle(HandleLeft, OverlayDragMode.ResizeLeft);
    }

    protected override void OnClosed(EventArgs e)
    {
        LocalizationManager.LanguageChanged -= LocalizationManager_LanguageChanged;
        base.OnClosed(e);
    }

    private void MenuFileExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuLanguageSystem_Click(object sender, RoutedEventArgs e) =>
        LocalizationManager.SetPreferredLanguage(AppLanguage.System);

    private void MenuLanguageGerman_Click(object sender, RoutedEventArgs e) =>
        LocalizationManager.SetPreferredLanguage(AppLanguage.German);

    private void MenuLanguageEnglish_Click(object sender, RoutedEventArgs e) =>
        LocalizationManager.SetPreferredLanguage(AppLanguage.English);

    private void MenuHelpAbout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AboutDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void LocalizationManager_LanguageChanged(object? sender, EventArgs e)
    {
        UpdateLanguageMenuChecks();
        RefreshLocalizedRuntimeTexts();
    }

    private void UpdateLanguageMenuChecks()
    {
        if (MiLanguageSystem == null || MiLanguageGerman == null || MiLanguageEnglish == null)
            return;

        MiLanguageSystem.IsChecked = LocalizationManager.SelectedLanguage == AppLanguage.System;
        MiLanguageGerman.IsChecked = LocalizationManager.SelectedLanguage == AppLanguage.German;
        MiLanguageEnglish.IsChecked = LocalizationManager.SelectedLanguage == AppLanguage.English;
    }

    private void RefreshLocalizedRuntimeTexts()
    {
        UpdateVisualizerSummary();
        UpdateVideoSummary();

        if (_sourceImageBitmap != null && File.Exists(TxtImage.Text))
            UpdateImageInfo(TxtImage.Text, _sourceImageBitmap);

        UpdateCommandPreview();
    }

    // â”€â”€ Browse handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.Get("DialogSelectBackgroundTitle"),
            Filter = Loc.Get("DialogFilterImage")
        };
        if (dlg.ShowDialog() == true)
        {
            TxtImage.Text = dlg.FileName;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            _sourceImageBitmap = bitmap;

            if (_videoOutputSettings.UseImageResolution)
                _videoOutputSettings.ResetToImage(bitmap.PixelWidth, bitmap.PixelHeight);

            ApplyPreviewAspect();
            _overlayInitialized = false;
            _snapCenterX = false;
            _snapCenterY = false;
            UpdateOverlay();
            UpdateImageInfo(dlg.FileName, bitmap);
            UpdateVideoSummary();
            UpdateCommandPreview();
        }
    }

    private void BrowseAudio_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.Get("DialogSelectAudioTitle"),
            Filter = Loc.Get("DialogFilterAudio")
        };
        if (dlg.ShowDialog() == true)
        {
            TxtAudio.Text = dlg.FileName;
            UpdateCommandPreview();
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = Loc.Get("DialogSaveOutputTitle"),
            Filter = Loc.Get("DialogFilterOutput"),
            DefaultExt = ".mp4"
        };
        if (dlg.ShowDialog() == true)
        {
            TxtOutput.Text = dlg.FileName;
            UpdateCommandPreview();
        }
    }

    private void UpdateImageInfo(string imagePath, BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        string aspect = GetVideoAspectSummaryLabel(width, height);
        double sizeInMb = new FileInfo(imagePath).Length / (1024d * 1024d);
        string format = GetImageFormatLabel(imagePath);

        TxtImageInfo.Text = string.Format(
            CultureInfo.CurrentCulture,
            Loc.Get("ImageInfoTemplate"),
            width,
            height,
            aspect,
            sizeInMb.ToString("0.0", CultureInfo.CurrentCulture),
            format);
        TxtImageInfo.Visibility = Visibility.Visible;
    }

    private static string BuildAspectRatioLabel(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return "-";

        string rawRatio = $"{width}:{height}";
        int gcd = GreatestCommonDivisor(width, height);
        string simplifiedRatio = $"{width / gcd}:{height / gcd}";

        if (CommonAspectRatios.Contains(simplifiedRatio))
            return simplifiedRatio;

        return rawRatio == simplifiedRatio ? simplifiedRatio : $"{rawRatio} -> {simplifiedRatio}";
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            int temp = a % b;
            a = b;
            b = temp;
        }
        return a == 0 ? 1 : a;
    }

    private static string GetImageFormatLabel(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".jpg" => "JPG",
            ".jpeg" => "JPG",
            ".png" => "PNG",
            _ => Loc.Get("ImageFormatUnknown")
        };
    }

    private void BtnVisualizerSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new VisualizerSettingsDialog(_visualizerSettings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _visualizerSettings = dlg.SelectedSettings.Clone();
            UpdateVisualizerSummary();
            UpdateCommandPreview();
        }
    }

    private void BtnVideoOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceImageBitmap == null)
        {
            MessageBox.Show(
                Loc.Get("ValidationBackground"),
                Loc.Get("ValidationTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var settingsForDialog = _videoOutputSettings.Clone();
        if (settingsForDialog.UseImageResolution)
            settingsForDialog.ResetToImage(_sourceImageBitmap.PixelWidth, _sourceImageBitmap.PixelHeight);

        var dlg = new VideoOptionsDialog(_sourceImageBitmap.PixelWidth, _sourceImageBitmap.PixelHeight, settingsForDialog)
        {
            Owner = this
        };

        if (dlg.ShowDialog() != true)
            return;

        TryGetEffectiveOutputResolution(out int oldWidth, out int oldHeight);
        _videoOutputSettings = dlg.SelectedOptions.Clone();
        ApplyPreviewAspect();
        TryGetEffectiveOutputResolution(out int newWidth, out int newHeight);
        ScaleOverlayToNewResolution(oldWidth, oldHeight, newWidth, newHeight);
        UpdateOverlay();
        UpdateVideoSummary();
        UpdateCommandPreview();
    }

    private void BtnRotateLeft_Click(object sender, RoutedEventArgs e)
    {
        _baseRotationDeg = NormalizeBaseRotation(_baseRotationDeg - 90);
        SwapOverlayDimensionsForQuarterTurn();
        ApplyRotationChange();
    }

    private void BtnRotateRight_Click(object sender, RoutedEventArgs e)
    {
        _baseRotationDeg = NormalizeBaseRotation(_baseRotationDeg + 90);
        SwapOverlayDimensionsForQuarterTurn();
        ApplyRotationChange();
    }

    private void BtnFineRotateMinus_Click(object sender, RoutedEventArgs e)
    {
        _fineRotationDeg = Math.Clamp(_fineRotationDeg - FineRotationStepDeg, MinFineRotationDeg, MaxFineRotationDeg);
        ApplyRotationChange();
    }

    private void BtnFineRotatePlus_Click(object sender, RoutedEventArgs e)
    {
        _fineRotationDeg = Math.Clamp(_fineRotationDeg + FineRotationStepDeg, MinFineRotationDeg, MaxFineRotationDeg);
        ApplyRotationChange();
    }

    private void BtnRotationReset_Click(object sender, RoutedEventArgs e)
    {
        _baseRotationDeg = 0;
        _fineRotationDeg = 0;
        ApplyRotationChange();
    }

    private void UpdateVisualizerSummary()
    {
        string alphaText = $"{Math.Round(_visualizerSettings.Alpha * 100):0}%";
        TxtVisualizerSummary.Text = string.Format(
            CultureInfo.CurrentCulture,
            Loc.Get("VisualizerSummaryTemplate"),
            _visualizerSettings.PresetName,
            _visualizerSettings.FilterType,
            _visualizerSettings.Mode,
            _visualizerSettings.Rate,
            alphaText);
    }

    private void UpdateVideoSummary()
    {
        if (TxtVideoSummary == null)
            return;

        if (!TryGetEffectiveOutputResolution(out int width, out int height))
        {
            TxtVideoSummary.Text = Loc.Get("VideoSummaryPlaceholder");
            return;
        }

        string aspect = GetVideoAspectSummaryLabel(width, height);
        string sourceMode = _videoOutputSettings.UseImageResolution
            ? Loc.Get("VideoSummarySourceImage")
            : Loc.Get("VideoSummarySourceCustom");

        TxtVideoSummary.Text = string.Format(
            CultureInfo.CurrentCulture,
            Loc.Get("VideoSummaryTemplate"),
            width,
            height,
            aspect,
            sourceMode);
    }

    private bool TryGetEffectiveOutputResolution(out int width, out int height)
    {
        width = 0;
        height = 0;

        if (_sourceImageBitmap == null)
            return false;

        if (_videoOutputSettings.UseImageResolution
            || _videoOutputSettings.Width <= 0
            || _videoOutputSettings.Height <= 0)
        {
            width = _sourceImageBitmap.PixelWidth;
            height = _sourceImageBitmap.PixelHeight;
            NormalizeEncodingResolution(ref width, ref height);
            return true;
        }

        width = _videoOutputSettings.Width;
        height = _videoOutputSettings.Height;
        NormalizeEncodingResolution(ref width, ref height);
        return true;
    }

    private static void NormalizeEncodingResolution(ref int width, ref int height)
    {
        width = NormalizeEvenDimension(width);
        height = NormalizeEvenDimension(height);
    }

    private static int NormalizeEvenDimension(int value)
    {
        value = Math.Max(2, value);
        return (value % 2 == 0) ? value : value - 1;
    }

    private string GetVideoAspectSummaryLabel(int width, int height)
    {
        if (_videoOutputSettings.KeepAspectRatio
            && _videoOutputSettings.LockedAspectNumerator > 0
            && _videoOutputSettings.LockedAspectDenominator > 0)
        {
            return $"{_videoOutputSettings.LockedAspectNumerator}:{_videoOutputSettings.LockedAspectDenominator}";
        }

        return BuildAspectRatioLabel(width, height);
    }

    private void ApplyRotationChange()
    {
        UpdateRotationAngleDisplay();
        UpdateOverlay();
        UpdateCommandPreview();
    }

    private void UpdateRotationAngleDisplay()
    {
        if (TxtVisualizerRotationAngle == null)
            return;

        TxtVisualizerRotationAngle.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0}°",
            GetEffectiveRotationDeg());
    }

    private int GetEffectiveRotationDeg() => _baseRotationDeg + _fineRotationDeg;

    private static int NormalizeBaseRotation(int value)
    {
        int normalized = value % 360;
        if (normalized < 0)
            normalized += 360;
        return normalized;
    }

    private void SwapOverlayDimensionsForQuarterTurn()
    {
        if (!_overlayInitialized)
            return;

        int imageWidth = 0;
        int imageHeight = 0;
        if (!TryGetEffectiveOutputResolution(out imageWidth, out imageHeight))
        {
            if (_sourceImageBitmap == null)
                return;

            imageWidth = _sourceImageBitmap.PixelWidth;
            imageHeight = _sourceImageBitmap.PixelHeight;
        }

        double centerX = _overlayX + (_overlayWidth / 2.0);
        double centerY = _overlayY + (_overlayHeight / 2.0);
        double swappedWidth = Math.Clamp(_overlayHeight, 1.0, imageWidth);
        double swappedHeight = Math.Clamp(_overlayWidth, 1.0, imageHeight);

        _overlayWidth = swappedWidth;
        _overlayHeight = swappedHeight;
        _overlayX = Math.Clamp(centerX - (swappedWidth / 2.0), 0.0, imageWidth - swappedWidth);
        _overlayY = Math.Clamp(centerY - (swappedHeight / 2.0), 0.0, imageHeight - swappedHeight);
    }

    private void ApplyPreviewAspect()
    {
        if (_sourceImageBitmap == null)
        {
            ImgPreview.Source = null;
            return;
        }

        if (!TryGetEffectiveOutputResolution(out int targetWidth, out int targetHeight))
        {
            ImgPreview.Source = _sourceImageBitmap;
            return;
        }

        double sourceAspect = (double)_sourceImageBitmap.PixelWidth / _sourceImageBitmap.PixelHeight;
        double targetAspect = (double)targetWidth / targetHeight;

        if (Math.Abs(sourceAspect - targetAspect) < 0.0001)
        {
            ImgPreview.Source = _sourceImageBitmap;
            return;
        }

        var transform = new ScaleTransform(targetAspect / sourceAspect, 1.0);
        var transformed = new TransformedBitmap(_sourceImageBitmap, transform);
        transformed.Freeze();
        ImgPreview.Source = transformed;
    }

    private void ScaleOverlayToNewResolution(int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        if (!_overlayInitialized || oldWidth <= 0 || oldHeight <= 0 || newWidth <= 0 || newHeight <= 0)
            return;

        double scaleX = (double)newWidth / oldWidth;
        double scaleY = (double)newHeight / oldHeight;
        _overlayX *= scaleX;
        _overlayY *= scaleY;
        _overlayWidth *= scaleX;
        _overlayHeight *= scaleY;
    }

    // â”€â”€ Generate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputs(out string imagePath, out string audioPath, out string outputPath))
            return;

        BtnGenerate.IsEnabled = false;
        Progress.IsIndeterminate = true;

        try
        {
            string ffmpeg  = FindTool("ffmpeg.exe");
            string ffprobe = FindTool("ffprobe.exe");

            // Detect image resolution
            var (width, height) = await GetImageResolutionAsync(ffprobe, imagePath);
            if (width == 0 || height == 0)
            {
                ShowError(Loc.Get("ErrorDetectResolution"));
                return;
            }

            int outputWidth;
            int outputHeight;
            if (_videoOutputSettings.UseImageResolution)
            {
                outputWidth = width;
                outputHeight = height;
            }
            else if (!TryGetEffectiveOutputResolution(out outputWidth, out outputHeight))
            {
                outputWidth = width;
                outputHeight = height;
            }

            // Build filter options from UI
            string color   = GetColor();
            string overlay = BuildOverlayFilter(outputWidth, outputHeight, color, _visualizerSettings);

            string args = BuildFfmpegArgs(imagePath, audioPath, outputPath, outputWidth, outputHeight, overlay);
            TxtCommandPreview.Text = $"{ffmpeg} {args}";

            string? error = await RunProcessAsync(ffmpeg, args);
            if (error != null)
            {
                TxtCommandPreview.Text = $"{ffmpeg} {args}{Environment.NewLine}{Environment.NewLine}FFmpeg error:{Environment.NewLine}{error}";
                ShowError(string.Format(CultureInfo.CurrentCulture, Loc.Get("ErrorFfmpegTemplate"), Environment.NewLine, error));
                return;
            }

            if (ChkPlayAfterExport.IsChecked == true)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputPath,
                    UseShellExecute = true
                });
            }
            
            Progress.IsIndeterminate = false;
            Progress.Value = 100;

            if (ChkPlayAfterExport.IsChecked != true)
            {
                MessageBox.Show(
                    string.Format(CultureInfo.CurrentCulture, Loc.Get("GenerateSuccessMessage"), Environment.NewLine, outputPath),
                    Loc.Get("GenerateSuccessTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            BtnGenerate.IsEnabled = true;
            Progress.IsIndeterminate = false;
        }
    }

    // â”€â”€ Validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private bool ValidateInputs(out string image, out string audio, out string output)
    {
        image  = TxtImage.Text.Trim();
        audio  = TxtAudio.Text.Trim();
        output = TxtOutput.Text.Trim();

        if (string.IsNullOrEmpty(image) || !File.Exists(image))
        { MessageBox.Show(Loc.Get("ValidationBackground"), Loc.Get("ValidationTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

        if (string.IsNullOrEmpty(audio) || !File.Exists(audio))
        { MessageBox.Show(Loc.Get("ValidationAudio"), Loc.Get("ValidationTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

        if (string.IsNullOrEmpty(output))
        { MessageBox.Show(Loc.Get("ValidationOutput"), Loc.Get("ValidationTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

        return true;
    }

    // â”€â”€ Tool resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns the path to an FFmpeg tool, preferring the exe directory then PATH.</summary>
    private static string FindTool(string exeName)
    {
        string exeDir  = AppContext.BaseDirectory;
        string local   = Path.Combine(exeDir, exeName);
        if (File.Exists(local))
            return local;

        // Fall back to PATH
        return exeName;
    }

    // â”€â”€ ffprobe â€“ image resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task<(int width, int height)> GetImageResolutionAsync(string ffprobe, string imagePath)
    {
        string args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{imagePath}\"";

        string? output = await RunProcessCaptureStdoutAsync(ffprobe, args);
        if (output == null)
            return (0, 0);

        string[] parts = output.Trim().Split(',');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out int w)
            && int.TryParse(parts[1], out int h))
            return (w, h);

        return (0, 0);
    }

    // â”€â”€ Filter / argument builders â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private string GetColor() => _spectrumColor;

    private void UpdateCommandPreview()
    {
        if (TxtCommandPreview == null)
            return;

        if (_sourceImageBitmap == null || _sourceImageBitmap.PixelWidth <= 0 || _sourceImageBitmap.PixelHeight <= 0)
        {
            TxtCommandPreview.Text = Loc.Get("PlaceholderCommandPreview");
            return;
        }

        string ffmpeg = FindTool("ffmpeg.exe");
        string imagePath = string.IsNullOrWhiteSpace(TxtImage.Text) ? "<image-file>" : TxtImage.Text.Trim();
        string audioPath = string.IsNullOrWhiteSpace(TxtAudio.Text) ? "<audio-file>" : TxtAudio.Text.Trim();
        string outputPath = string.IsNullOrWhiteSpace(TxtOutput.Text) ? "<output-file>" : TxtOutput.Text.Trim();

        if (!TryGetEffectiveOutputResolution(out int outputWidth, out int outputHeight))
        {
            TxtCommandPreview.Text = Loc.Get("PlaceholderCommandPreview");
            return;
        }

        string color = GetColor();
        string overlay = BuildOverlayFilter(outputWidth, outputHeight, color, _visualizerSettings);
        string args = BuildFfmpegArgs(imagePath, audioPath, outputPath, outputWidth, outputHeight, overlay);

        TxtCommandPreview.Text = $"{ffmpeg} {args}";
    }

    private string BuildOverlayFilter(int imgWidth, int imgHeight, string color, VisualizerSettings settings)
    {
        EnsureOverlayStateForImageSize(imgWidth, imgHeight);

        int boxW = Math.Clamp((int)Math.Round(_overlayWidth), 1, imgWidth);
        int boxH = Math.Clamp((int)Math.Round(_overlayHeight), 1, imgHeight);
        int posX = Math.Clamp((int)Math.Round(_overlayX), 0, imgWidth - boxW);
        int posY = Math.Clamp((int)Math.Round(_overlayY), 0, imgHeight - boxH);
        int baseRotationDeg = NormalizeBaseRotation(_baseRotationDeg);
        int fineRotationDeg = Math.Clamp(_fineRotationDeg, MinFineRotationDeg, MaxFineRotationDeg);
        bool hasRotation = baseRotationDeg != 0 || fineRotationDeg != 0;
        double effectiveRotationDeg = baseRotationDeg + fineRotationDeg;

        double renderW = boxW;
        double renderH = boxH;
        if (hasRotation)
            ComputeRenderSizeFromVisualSize(boxW, boxH, effectiveRotationDeg, out renderW, out renderH);

        int waveW = Math.Clamp((int)Math.Round(renderW), 1, 16384);
        int waveH = Math.Clamp((int)Math.Round(renderH), 1, 16384);

        string filterType = settings.FilterType == "showwaves" ? "showwaves" : "showfreqs";
        string channelMode = GetSafeChoice(settings.ChannelMode, "mono", "mono", "stereo_split", "stereo_mirror");
        string mode = GetSafeMode(filterType, settings.Mode);
        string ascale = GetSafeChoice(settings.AScale, "sqrt", "lin", "sqrt", "cbrt", "log");
        string fscale = GetSafeChoice(settings.FScale, "log", "lin", "log");

        int rate = Math.Clamp(settings.Rate, 1, 120);
        int winSize = Math.Clamp(settings.WinSize, 32, 65536);
        int thickness = Math.Clamp(settings.LineThickness, 1, 8);
        double volumeDb = Math.Clamp(settings.VolumeDb, -96.0, 40.0);
        double alpha = Math.Clamp(settings.Alpha, 0.05, 1.0);
        double keySimilarity = Math.Clamp(settings.ColorKeySimilarity, 0.0, 1.0);
        double keyBlend = Math.Clamp(settings.ColorKeyBlend, 0.0, 1.0);

        string volumeText = volumeDb.ToString("0.###", CultureInfo.InvariantCulture);
        string alphaText = alpha.ToString("0.###", CultureInfo.InvariantCulture);
        string keySimilarityText = keySimilarity.ToString("0.###", CultureInfo.InvariantCulture);
        string keyBlendText = keyBlend.ToString("0.###", CultureInfo.InvariantCulture);

        var filterParts = new List<string>();
        filterParts.Add($"[0:v]scale={imgWidth}:{imgHeight}:flags=lanczos,format=rgba[bg]");

        string currentLabel = "viz0";
        if (channelMode == "mono")
        {
            if (filterType == "showwaves")
            {
                filterParts.Add(
                    $"[1:a]volume={volumeText}dB,showwaves=s={waveW}x{waveH}:mode={mode}:rate={rate}:colors={color},format=rgba[{currentLabel}]");
            }
            else
            {
                double showfreqVolumeDb = volumeDb;
                if (settings.AutoHeadroom && settings.SmoothSpectrum)
                    showfreqVolumeDb += MapSmoothnessToHeadroomDb(settings.Smoothness);
                showfreqVolumeDb = Math.Clamp(showfreqVolumeDb, -96.0, 40.0);
                string showfreqVolumeText = showfreqVolumeDb.ToString("0.###", CultureInfo.InvariantCulture);

                string averagingPart = string.Empty;
                if (settings.SmoothSpectrum)
                {
                    int averaging = MapSmoothnessToAveraging(settings.Smoothness);
                    averagingPart = $":averaging={averaging}";
                }

                string minAmpPart = string.Empty;
                if (settings.UseMinAmplitude)
                {
                    double minAmp = MapMinAmplitude(settings.MinAmplitude);
                    string minAmpText = minAmp.ToString("G17", CultureInfo.InvariantCulture);
                    minAmpPart = $":minamp={minAmpText}";
                }

                filterParts.Add(
                    $"[1:a]volume={showfreqVolumeText}dB,showfreqs=s={waveW}x{waveH}:mode={mode}:ascale={ascale}:win_size={winSize}:rate={rate}:fscale={fscale}{averagingPart}{minAmpPart}:colors={color},format=rgba[{currentLabel}]");
            }
        }
        else
        {
            int halfWidth = Math.Max(1, waveW / 2);
            int remainderWidth = Math.Max(1, waveW - halfWidth);
            string leftLabel = "vizChLeft";
            string rightLabel = "vizChRight";
            string mergedLabel = "vizStereo";

            if (filterType == "showwaves")
            {
                filterParts.Add($"[1:a]aformat=channel_layouts=stereo,channelsplit=channel_layout=stereo[left][right]");
                filterParts.Add($"[left]volume={volumeText}dB,showwaves=s={halfWidth}x{waveH}:mode={mode}:rate={rate}:colors={color},format=rgba[{leftLabel}]");
                filterParts.Add($"[right]volume={volumeText}dB,showwaves=s={remainderWidth}x{waveH}:mode={mode}:rate={rate}:colors={color},format=rgba[{rightLabel}]");
            }
            else
            {
                double showfreqVolumeDb = volumeDb;
                if (settings.AutoHeadroom && settings.SmoothSpectrum)
                    showfreqVolumeDb += MapSmoothnessToHeadroomDb(settings.Smoothness);
                showfreqVolumeDb = Math.Clamp(showfreqVolumeDb, -96.0, 40.0);
                string showfreqVolumeText = showfreqVolumeDb.ToString("0.###", CultureInfo.InvariantCulture);

                string averagingPart = string.Empty;
                if (settings.SmoothSpectrum)
                {
                    int averaging = MapSmoothnessToAveraging(settings.Smoothness);
                    averagingPart = $":averaging={averaging}";
                }

                string minAmpPart = string.Empty;
                if (settings.UseMinAmplitude)
                {
                    double minAmp = MapMinAmplitude(settings.MinAmplitude);
                    string minAmpText = minAmp.ToString("G17", CultureInfo.InvariantCulture);
                    minAmpPart = $":minamp={minAmpText}";
                }

                filterParts.Add($"[1:a]aformat=channel_layouts=stereo,channelsplit=channel_layout=stereo[left][right]");
                filterParts.Add($"[left]volume={showfreqVolumeText}dB,showfreqs=s={halfWidth}x{waveH}:mode={mode}:ascale={ascale}:win_size={winSize}:rate={rate}:fscale={fscale}{averagingPart}{minAmpPart}:colors={color},format=rgba[{leftLabel}]");
                filterParts.Add($"[right]volume={showfreqVolumeText}dB,showfreqs=s={remainderWidth}x{waveH}:mode={mode}:ascale={ascale}:win_size={winSize}:rate={rate}:fscale={fscale}{averagingPart}{minAmpPart}:colors={color},format=rgba[{rightLabel}]");
            }

            if (channelMode == "stereo_mirror")
            {
                string mirroredLeftLabel = "vizChLeftMirror";
                filterParts.Add($"[{leftLabel}]hflip[{mirroredLeftLabel}]");
                leftLabel = mirroredLeftLabel;
            }

            filterParts.Add($"[{leftLabel}][{rightLabel}]hstack=inputs=2[{mergedLabel}]");
            currentLabel = mergedLabel;
        }

        if (settings.UseColorKey)
        {
            string nextLabel = "vizKey";
            filterParts.Add(
                $"[{currentLabel}]colorkey=0x000000:{keySimilarityText}:{keyBlendText}[{nextLabel}]");
            currentLabel = nextLabel;
        }

        for (int i = 1; i < thickness; i++)
        {
            string nextLabel = $"vizDil{i}";
            filterParts.Add($"[{currentLabel}]dilation[{nextLabel}]");
            currentLabel = nextLabel;
        }

        if (settings.UseColorKey)
        {
            Color tint = ParseSpectrumColor(color);
            string nextLabel = "vizTint";
            filterParts.Add($"[{currentLabel}]lutrgb=r={tint.R}:g={tint.G}:b={tint.B}[{nextLabel}]");
            currentLabel = nextLabel;
        }

        if (alpha < 0.999)
        {
            string nextLabel = "vizAlpha";
            filterParts.Add($"[{currentLabel}]colorchannelmixer=aa={alphaText}[{nextLabel}]");
            currentLabel = nextLabel;
        }

        if (baseRotationDeg != 0)
        {
            string nextLabel = "vizRotateBase";
            string baseRotationFilter = baseRotationDeg switch
            {
                90 => "transpose=clock",
                180 => "hflip,vflip",
                270 => "transpose=cclock",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(baseRotationFilter))
            {
                filterParts.Add($"[{currentLabel}]{baseRotationFilter}[{nextLabel}]");
                currentLabel = nextLabel;
            }
        }

        if (fineRotationDeg != 0)
        {
            double radians = fineRotationDeg * Math.PI / 180.0;
            string radiansText = radians.ToString("0.###########", CultureInfo.InvariantCulture);
            string nextLabel = "vizRotateFine";
            filterParts.Add($"[{currentLabel}]rotate={radiansText}:ow=rotw(iw):oh=roth(ih):c=none[{nextLabel}]");
            currentLabel = nextLabel;
        }

        if (hasRotation)
        {
            double centerX = posX + (boxW / 2.0);
            double centerY = posY + (boxH / 2.0);
            string centerXText = centerX.ToString("0.###", CultureInfo.InvariantCulture);
            string centerYText = centerY.ToString("0.###", CultureInfo.InvariantCulture);
            filterParts.Add($"[bg][{currentLabel}]overlay={centerXText}-overlay_w/2:{centerYText}-overlay_h/2:format=auto[v]");
        }
        else
        {
            filterParts.Add($"[bg][{currentLabel}]overlay={posX}:{posY}:format=auto[v]");
        }
        return string.Join(";", filterParts);
    }

    private static string GetSafeMode(string filterType, string mode)
    {
        if (filterType == "showwaves")
            return GetSafeChoice(mode, "line", "line", "p2p", "point");

        return GetSafeChoice(mode, "line", "line", "bar", "dot");
    }

    private static void ComputeRenderSizeFromVisualSize(double visualWidth, double visualHeight, double angleDeg, out double renderWidth, out double renderHeight)
    {
        double width = Math.Max(1.0, visualWidth);
        double height = Math.Max(1.0, visualHeight);
        double radians = angleDeg * Math.PI / 180.0;
        double c = Math.Abs(Math.Cos(radians));
        double s = Math.Abs(Math.Sin(radians));
        double det = (c * c) - (s * s);

        if (Math.Abs(det) < 0.0001)
        {
            renderWidth = width;
            renderHeight = height;
            return;
        }

        renderWidth = ((c * width) - (s * height)) / det;
        renderHeight = ((-s * width) + (c * height)) / det;

        renderWidth = Math.Max(1.0, Math.Abs(renderWidth));
        renderHeight = Math.Max(1.0, Math.Abs(renderHeight));
    }

    private static int MapSmoothnessToAveraging(int smoothness)
    {
        int clamped = Math.Clamp(smoothness, 0, 100);
        if (clamped <= 24)
            return 1;
        if (clamped <= 49)
            return 2;
        if (clamped <= 74)
            return 3;
        return 4;
    }

    private static double MapSmoothnessToHeadroomDb(int smoothness)
    {
        int clamped = Math.Clamp(smoothness, 0, 100);
        if (clamped <= 24)
            return 0.0;
        if (clamped <= 49)
            return -2.0;
        if (clamped <= 74)
            return -4.0;
        return -6.0;
    }

    private static double MapMinAmplitude(int minAmplitude)
    {
        int clamped = Math.Clamp(minAmplitude, 0, 100);
        const double minValue = 1e-9;
        const double maxValue = 8e-7;
        double t = clamped / 100.0;
        return minValue * Math.Pow(maxValue / minValue, t);
    }

    private static string GetSafeChoice(string value, string fallback, params string[] allowed)
    {
        foreach (string candidate in allowed)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return fallback;
    }

    private static string BuildFfmpegArgs(
        string image, string audio, string output,
        int width, int height,
        string filterComplex)
    {
        // -loop 1        : loop the still image
        // -i image       : input 0 â€“ background
        // -i audio       : input 1 â€“ audio (also drives showwaves)
        // -filter_complex: combined filter graph
        // -map "[v]" not needed when filter ends with overlay (output is [0:v] after overlay)
        // -map 0:a       : passthrough audio
        // -shortest      : stop when audio ends
        // -c:v libx264   : encode video
        // -c:a aac       : encode audio
        // -pix_fmt yuv420p : broad compatibility
        // -y             : overwrite output

        return $"-loop 1 -i \"{image}\" -i \"{audio}\" " +
               $"-filter_complex \"{filterComplex}\" " +
               $"-map \"[v]\" -map 1:a " +
               $"-c:v libx264 -c:a aac -pix_fmt yuv420p -shortest -y \"{output}\"";
    }

    // â”€â”€ Process helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Runs a process and returns stderr on non-zero exit, null on success.</summary>
    private static Task<string?> RunProcessAsync(string exe, string args)
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute        = false,
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
            var stderr = new StringBuilder();
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            return proc.ExitCode == 0 ? null : stderr.ToString();
        });
    }

    /// <summary>Runs a process and returns stdout, or null on error.</summary>
    private static Task<string?> RunProcessCaptureStdoutAsync(string exe, string args)
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                string stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return proc.ExitCode == 0 ? stdout : null;
            }
            catch
            {
                return null;
            }
        });
    }

    // -- Spectrum position overlay -------------------------------------------------

    private void AttachHandle(System.Windows.Shapes.Rectangle handle, OverlayDragMode mode)
    {
        handle.Tag = mode;
        handle.MouseLeftButtonDown += OverlayHandle_MouseDown;
        handle.MouseMove += OverlayHandle_MouseMove;
        handle.MouseLeftButtonUp += OverlayHandle_MouseUp;
    }

    private void OverlayHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Shapes.Rectangle handle || handle.Tag is not OverlayDragMode mode)
            return;

        BeginOverlayDrag(e.GetPosition(OverlayCanvas), mode);
        if (_isDragging)
        {
            handle.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OverlayHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        UpdateOverlayDrag(e.GetPosition(OverlayCanvas));
        e.Handled = true;
    }

    private void OverlayHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        _dragMode = OverlayDragMode.None;

        if (sender is UIElement element)
            element.ReleaseMouseCapture();

        e.Handled = true;
    }

    private void OverlayBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Rect imageRect = GetImageContentRect();
        if (imageRect == Rect.Empty || ImgPreview.Source is not BitmapSource bmp)
            return;

        EnsureOverlayStateForBitmap(bmp);
        Rect overlayRect = GetOverlayPreviewRect(imageRect, bmp);
        Point mousePos = e.GetPosition(OverlayCanvas);
        if (!IsMoveArea(mousePos, overlayRect))
            return;

        BeginOverlayDrag(mousePos, OverlayDragMode.Move);
        if (_isDragging)
        {
            OverlayBar.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OverlayBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            UpdateOverlayDrag(e.GetPosition(OverlayCanvas));
            e.Handled = true;
            return;
        }

        Rect imageRect = GetImageContentRect();
        if (imageRect == Rect.Empty || ImgPreview.Source is not BitmapSource bmp)
            return;

        EnsureOverlayStateForBitmap(bmp);
        Rect overlayRect = GetOverlayPreviewRect(imageRect, bmp);
        OverlayBar.Cursor = IsMoveArea(e.GetPosition(OverlayCanvas), overlayRect) ? Cursors.SizeAll : Cursors.Arrow;
    }

    private void OverlayBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        _dragMode = OverlayDragMode.None;
        OverlayBar.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void BeginOverlayDrag(Point mousePos, OverlayDragMode mode)
    {
        Rect imageRect = GetImageContentRect();
        if (imageRect == Rect.Empty || ImgPreview.Source is not BitmapSource bmp)
            return;

        EnsureOverlayStateForBitmap(bmp);

        _dragStartMouse = mousePos;
        _dragStartRectPreview = GetOverlayPreviewRect(imageRect, bmp);
        _dragMode = mode;
        _isDragging = true;
    }

    private void UpdateOverlayDrag(Point currentMousePos)
    {
        if (!_isDragging || _dragMode == OverlayDragMode.None)
            return;

        Rect imageRect = GetImageContentRect();
        if (imageRect == Rect.Empty || ImgPreview.Source is not BitmapSource bmp)
            return;

        Vector delta = currentMousePos - _dragStartMouse;
        Rect previewRect;

        if (_dragMode == OverlayDragMode.Move)
        {
            double maxLeft = Math.Max(imageRect.Left, imageRect.Right - _dragStartRectPreview.Width);
            double maxTop = Math.Max(imageRect.Top, imageRect.Bottom - _dragStartRectPreview.Height);

            double left = Math.Clamp(_dragStartRectPreview.Left + delta.X, imageRect.Left, maxLeft);
            double top = Math.Clamp(_dragStartRectPreview.Top + delta.Y, imageRect.Top, maxTop);
            previewRect = new Rect(left, top, _dragStartRectPreview.Width, _dragStartRectPreview.Height);
        }
        else
        {
            previewRect = ResizePreviewRect(_dragStartRectPreview, delta.X, delta.Y, _dragMode, imageRect);
        }

        ApplyCenterSnap(ref previewRect, imageRect);
        UpdateOverlayFromPreviewRect(previewRect, imageRect, bmp);
        UpdateOverlay();
        UpdateCommandPreview();
    }

    private static bool IsMoveArea(Point point, Rect rect)
    {
        if (!rect.Contains(point))
            return false;

        return point.X > rect.Left + MoveEdgeExclusionPreview
            && point.X < rect.Right - MoveEdgeExclusionPreview
            && point.Y > rect.Top + MoveEdgeExclusionPreview
            && point.Y < rect.Bottom - MoveEdgeExclusionPreview;
    }

    private static Rect ResizePreviewRect(Rect startRect, double deltaX, double deltaY, OverlayDragMode mode, Rect imageRect)
    {
        bool resizeLeft = mode is OverlayDragMode.ResizeLeft or OverlayDragMode.ResizeTopLeft or OverlayDragMode.ResizeBottomLeft;
        bool resizeRight = mode is OverlayDragMode.ResizeRight or OverlayDragMode.ResizeTopRight or OverlayDragMode.ResizeBottomRight;
        bool resizeTop = mode is OverlayDragMode.ResizeTop or OverlayDragMode.ResizeTopLeft or OverlayDragMode.ResizeTopRight;
        bool resizeBottom = mode is OverlayDragMode.ResizeBottom or OverlayDragMode.ResizeBottomLeft or OverlayDragMode.ResizeBottomRight;

        double minW = Math.Min(MinOverlayWidthPreview, imageRect.Width);
        double minH = Math.Min(MinOverlayHeightPreview, imageRect.Height);

        double left = startRect.Left;
        double right = startRect.Right;
        double top = startRect.Top;
        double bottom = startRect.Bottom;

        if (resizeLeft) left += deltaX;
        if (resizeRight) right += deltaX;
        if (resizeTop) top += deltaY;
        if (resizeBottom) bottom += deltaY;

        if (right - left < minW)
        {
            if (resizeLeft && !resizeRight)
                left = right - minW;
            else if (resizeRight && !resizeLeft)
                right = left + minW;
        }

        if (bottom - top < minH)
        {
            if (resizeTop && !resizeBottom)
                top = bottom - minH;
            else if (resizeBottom && !resizeTop)
                bottom = top + minH;
        }

        if (resizeLeft && !resizeRight)
            left = Math.Clamp(left, imageRect.Left, right - minW);
        if (resizeRight && !resizeLeft)
            right = Math.Clamp(right, left + minW, imageRect.Right);

        if (resizeTop && !resizeBottom)
            top = Math.Clamp(top, imageRect.Top, bottom - minH);
        if (resizeBottom && !resizeTop)
            bottom = Math.Clamp(bottom, top + minH, imageRect.Bottom);

        double width = Math.Clamp(right - left, minW, imageRect.Width);
        double height = Math.Clamp(bottom - top, minH, imageRect.Height);

        left = Math.Clamp(left, imageRect.Left, imageRect.Right - width);
        top = Math.Clamp(top, imageRect.Top, imageRect.Bottom - height);

        return new Rect(left, top, width, height);
    }

    private void ApplyCenterSnap(ref Rect rect, Rect imageRect)
    {
        double imageCenterX = imageRect.Left + imageRect.Width / 2.0;
        double imageCenterY = imageRect.Top + imageRect.Height / 2.0;

        double rectCenterX = rect.Left + rect.Width / 2.0;
        double rectCenterY = rect.Top + rect.Height / 2.0;

        _snapCenterX = Math.Abs(rectCenterX - imageCenterX) <= SnapThresholdPreview;
        _snapCenterY = Math.Abs(rectCenterY - imageCenterY) <= SnapThresholdPreview;

        if (_snapCenterX)
        {
            double maxLeft = Math.Max(imageRect.Left, imageRect.Right - rect.Width);
            rect.X = Math.Clamp(imageCenterX - rect.Width / 2.0, imageRect.Left, maxLeft);
        }

        if (_snapCenterY)
        {
            double maxTop = Math.Max(imageRect.Top, imageRect.Bottom - rect.Height);
            rect.Y = Math.Clamp(imageCenterY - rect.Height / 2.0, imageRect.Top, maxTop);
        }
    }

    private void EnsureOverlayStateForBitmap(BitmapSource bmp)
    {
        if (TryGetEffectiveOutputResolution(out int width, out int height))
            EnsureOverlayStateForImageSize(width, height);
        else
            EnsureOverlayStateForImageSize(bmp.PixelWidth, bmp.PixelHeight);
    }

    private void EnsureOverlayStateForImageSize(int imageWidth, int imageHeight)
    {
        if (!_overlayInitialized)
        {
            _overlayX = 0;
            _overlayWidth = imageWidth;
            _overlayHeight = Math.Max(80.0, imageHeight / 5.0);
            _overlayY = (imageHeight - _overlayHeight) / 2.0;
            _overlayInitialized = true;
        }

        _overlayWidth = Math.Clamp(_overlayWidth, 1.0, imageWidth);
        _overlayHeight = Math.Clamp(_overlayHeight, 1.0, imageHeight);
        _overlayX = Math.Clamp(_overlayX, 0.0, imageWidth - _overlayWidth);
        _overlayY = Math.Clamp(_overlayY, 0.0, imageHeight - _overlayHeight);
    }

    private Rect GetOverlayPreviewRect(Rect imageRect, BitmapSource bmp)
    {
        int basisWidth = bmp.PixelWidth;
        int basisHeight = bmp.PixelHeight;
        if (TryGetEffectiveOutputResolution(out int width, out int height))
        {
            basisWidth = width;
            basisHeight = height;
        }

        double scaleX = imageRect.Width / basisWidth;
        double scaleY = imageRect.Height / basisHeight;

        return new Rect(
            imageRect.Left + (_overlayX * scaleX),
            imageRect.Top + (_overlayY * scaleY),
            _overlayWidth * scaleX,
            _overlayHeight * scaleY);
    }

    private void UpdateOverlayFromPreviewRect(Rect previewRect, Rect imageRect, BitmapSource bmp)
    {
        int basisWidth = bmp.PixelWidth;
        int basisHeight = bmp.PixelHeight;
        if (TryGetEffectiveOutputResolution(out int width, out int height))
        {
            basisWidth = width;
            basisHeight = height;
        }

        double scaleX = basisWidth / imageRect.Width;
        double scaleY = basisHeight / imageRect.Height;

        _overlayX = (previewRect.Left - imageRect.Left) * scaleX;
        _overlayY = (previewRect.Top - imageRect.Top) * scaleY;
        _overlayWidth = previewRect.Width * scaleX;
        _overlayHeight = previewRect.Height * scaleY;

        EnsureOverlayStateForImageSize(basisWidth, basisHeight);
    }

    private static void SetHandlePosition(System.Windows.Shapes.Rectangle handle, double centerX, double centerY)
    {
        Canvas.SetLeft(handle, centerX - (HandleSizePreview / 2.0));
        Canvas.SetTop(handle, centerY - (HandleSizePreview / 2.0));
        handle.Visibility = Visibility.Visible;
    }

    private void HideOverlayElements()
    {
        OverlayBar.Visibility = Visibility.Collapsed;
        OverlayRotatedBar.Visibility = Visibility.Collapsed;
        OverlayTopLine.Visibility = Visibility.Collapsed;
        OverlayBottomLine.Visibility = Visibility.Collapsed;
        OverlayLeftLine.Visibility = Visibility.Collapsed;
        OverlayRightLine.Visibility = Visibility.Collapsed;
        OverlayTopEdgeMarker.Visibility = Visibility.Collapsed;
        OverlayDirectionStem.Visibility = Visibility.Collapsed;
        OverlayDirectionHeadLeft.Visibility = Visibility.Collapsed;
        OverlayDirectionHeadRight.Visibility = Visibility.Collapsed;

        CenterGuideLine.Visibility = Visibility.Collapsed;
        CenterGuideLineVertical.Visibility = Visibility.Collapsed;
        CenterLabel.Visibility = Visibility.Collapsed;

        HandleTopLeft.Visibility = Visibility.Collapsed;
        HandleTop.Visibility = Visibility.Collapsed;
        HandleTopRight.Visibility = Visibility.Collapsed;
        HandleRight.Visibility = Visibility.Collapsed;
        HandleBottomRight.Visibility = Visibility.Collapsed;
        HandleBottom.Visibility = Visibility.Collapsed;
        HandleBottomLeft.Visibility = Visibility.Collapsed;
        HandleLeft.Visibility = Visibility.Collapsed;
    }

    /// <summary>Redraws the semi-transparent overlay rectangle showing where the spectrum will appear.</summary>
    private void UpdateOverlay()
    {
        Rect imageRect = GetImageContentRect();
        if (imageRect == Rect.Empty || ImgPreview.Source is not BitmapSource bmp)
        {
            _snapCenterX = false;
            _snapCenterY = false;
            HideOverlayElements();
            return;
        }

        EnsureOverlayStateForBitmap(bmp);
        Rect overlayRect = GetOverlayPreviewRect(imageRect, bmp);

        Color tint = ParseSpectrumColor(_spectrumColor);

        // Semi-transparent fill
        double effectiveRotation = GetEffectiveRotationDeg();
        bool hasRotation = Math.Abs(effectiveRotation) > 0.001;

        OverlayBar.Fill = hasRotation
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(115, tint.R, tint.G, tint.B));
        OverlayBar.RenderTransform = Transform.Identity;
        OverlayBar.RenderTransformOrigin = new Point(0.5, 0.5);
        OverlayBar.Width = overlayRect.Width;
        OverlayBar.Height = overlayRect.Height;
        Canvas.SetLeft(OverlayBar, overlayRect.Left);
        Canvas.SetTop(OverlayBar, overlayRect.Top);
        OverlayBar.Visibility = Visibility.Visible;

        // Bright border lines
        var lineBrush = new SolidColorBrush(Color.FromArgb(220, tint.R, tint.G, tint.B));
        double centerX = overlayRect.Left + (overlayRect.Width / 2.0);
        double centerY = overlayRect.Top + (overlayRect.Height / 2.0);
        double renderPreviewW = overlayRect.Width;
        double renderPreviewH = overlayRect.Height;

        if (hasRotation)
        {
            ComputeRenderSizeFromVisualSize(overlayRect.Width, overlayRect.Height, effectiveRotation, out renderPreviewW, out renderPreviewH);
            OverlayRotatedBar.Fill = new SolidColorBrush(Color.FromArgb(115, tint.R, tint.G, tint.B));
            OverlayRotatedBar.Stroke = lineBrush;
            OverlayRotatedBar.StrokeThickness = 1;
            OverlayRotatedBar.Width = renderPreviewW;
            OverlayRotatedBar.Height = renderPreviewH;
            Canvas.SetLeft(OverlayRotatedBar, centerX - (renderPreviewW / 2.0));
            Canvas.SetTop(OverlayRotatedBar, centerY - (renderPreviewH / 2.0));
            OverlayRotatedBar.RenderTransformOrigin = new Point(0.5, 0.5);
            OverlayRotatedBar.RenderTransform = new RotateTransform(effectiveRotation);
            OverlayRotatedBar.Visibility = Visibility.Visible;
        }
        else
        {
            OverlayRotatedBar.Visibility = Visibility.Collapsed;
            OverlayRotatedBar.RenderTransform = Transform.Identity;
        }

        double topEdgeY = centerY - (renderPreviewH / 2.0);
        OverlayTopEdgeMarker.Stroke = lineBrush;
        OverlayTopEdgeMarker.StrokeThickness = 3.0;
        OverlayTopEdgeMarker.X1 = centerX - (renderPreviewW / 2.0);
        OverlayTopEdgeMarker.Y1 = topEdgeY;
        OverlayTopEdgeMarker.X2 = centerX + (renderPreviewW / 2.0);
        OverlayTopEdgeMarker.Y2 = topEdgeY;
        OverlayTopEdgeMarker.RenderTransform = new RotateTransform(effectiveRotation, centerX, centerY);
        OverlayTopEdgeMarker.Visibility = Visibility.Visible;

        const double arrowOutside = 8.0;
        const double arrowHeadSize = 4.0;
        double arrowTipY = centerY - (renderPreviewH / 2.0) - arrowOutside;

        OverlayDirectionStem.Stroke = lineBrush;
        OverlayDirectionStem.StrokeThickness = 2.2;
        OverlayDirectionStem.X1 = centerX;
        OverlayDirectionStem.Y1 = centerY;
        OverlayDirectionStem.X2 = centerX;
        OverlayDirectionStem.Y2 = arrowTipY;
        OverlayDirectionStem.RenderTransform = new RotateTransform(effectiveRotation, centerX, centerY);
        OverlayDirectionStem.Visibility = Visibility.Visible;

        OverlayDirectionHeadLeft.Stroke = lineBrush;
        OverlayDirectionHeadLeft.StrokeThickness = 2.2;
        OverlayDirectionHeadLeft.X1 = centerX;
        OverlayDirectionHeadLeft.Y1 = arrowTipY;
        OverlayDirectionHeadLeft.X2 = centerX - arrowHeadSize;
        OverlayDirectionHeadLeft.Y2 = arrowTipY + arrowHeadSize;
        OverlayDirectionHeadLeft.RenderTransform = new RotateTransform(effectiveRotation, centerX, centerY);
        OverlayDirectionHeadLeft.Visibility = Visibility.Visible;

        OverlayDirectionHeadRight.Stroke = lineBrush;
        OverlayDirectionHeadRight.StrokeThickness = 2.2;
        OverlayDirectionHeadRight.X1 = centerX;
        OverlayDirectionHeadRight.Y1 = arrowTipY;
        OverlayDirectionHeadRight.X2 = centerX + arrowHeadSize;
        OverlayDirectionHeadRight.Y2 = arrowTipY + arrowHeadSize;
        OverlayDirectionHeadRight.RenderTransform = new RotateTransform(effectiveRotation, centerX, centerY);
        OverlayDirectionHeadRight.Visibility = Visibility.Visible;

        OverlayTopLine.Fill = lineBrush;
        OverlayTopLine.Width = overlayRect.Width;
        Canvas.SetLeft(OverlayTopLine, overlayRect.Left);
        Canvas.SetTop(OverlayTopLine, overlayRect.Top);
        OverlayTopLine.Visibility = Visibility.Visible;

        OverlayBottomLine.Fill = lineBrush;
        OverlayBottomLine.Width = overlayRect.Width;
        Canvas.SetLeft(OverlayBottomLine, overlayRect.Left);
        Canvas.SetTop(OverlayBottomLine, overlayRect.Bottom - 1);
        OverlayBottomLine.Visibility = Visibility.Visible;

        OverlayLeftLine.Fill = lineBrush;
        OverlayLeftLine.Height = overlayRect.Height;
        Canvas.SetLeft(OverlayLeftLine, overlayRect.Left);
        Canvas.SetTop(OverlayLeftLine, overlayRect.Top);
        OverlayLeftLine.Visibility = Visibility.Visible;

        OverlayRightLine.Fill = lineBrush;
        OverlayRightLine.Height = overlayRect.Height;
        Canvas.SetLeft(OverlayRightLine, overlayRect.Right - 1);
        Canvas.SetTop(OverlayRightLine, overlayRect.Top);
        OverlayRightLine.Visibility = Visibility.Visible;

        // Resize handles
        double midX = overlayRect.Left + overlayRect.Width / 2.0;
        double midY = overlayRect.Top + overlayRect.Height / 2.0;

        Point handleTopLeft = new(overlayRect.Left, overlayRect.Top);
        Point handleTop = new(midX, overlayRect.Top);
        Point handleTopRight = new(overlayRect.Right, overlayRect.Top);
        Point handleRight = new(overlayRect.Right, midY);
        Point handleBottomRight = new(overlayRect.Right, overlayRect.Bottom);
        Point handleBottom = new(midX, overlayRect.Bottom);
        Point handleBottomLeft = new(overlayRect.Left, overlayRect.Bottom);
        Point handleLeft = new(overlayRect.Left, midY);

        SetHandlePosition(HandleTopLeft, handleTopLeft.X, handleTopLeft.Y);
        SetHandlePosition(HandleTop, handleTop.X, handleTop.Y);
        SetHandlePosition(HandleTopRight, handleTopRight.X, handleTopRight.Y);
        SetHandlePosition(HandleRight, handleRight.X, handleRight.Y);
        SetHandlePosition(HandleBottomRight, handleBottomRight.X, handleBottomRight.Y);
        SetHandlePosition(HandleBottom, handleBottom.X, handleBottom.Y);
        SetHandlePosition(HandleBottomLeft, handleBottomLeft.X, handleBottomLeft.Y);
        SetHandlePosition(HandleLeft, handleLeft.X, handleLeft.Y);

        HandleTopLeft.Cursor = Cursors.SizeNWSE;
        HandleTop.Cursor = Cursors.SizeNS;
        HandleTopRight.Cursor = Cursors.SizeNESW;
        HandleRight.Cursor = Cursors.SizeWE;
        HandleBottomRight.Cursor = Cursors.SizeNWSE;
        HandleBottom.Cursor = Cursors.SizeNS;
        HandleBottomLeft.Cursor = Cursors.SizeNESW;
        HandleLeft.Cursor = Cursors.SizeWE;

        // Snap guides
        if (_snapCenterY)
        {
            CenterGuideLine.Fill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            CenterGuideLine.Width = imageRect.Width;
            Canvas.SetLeft(CenterGuideLine, imageRect.Left);
            Canvas.SetTop(CenterGuideLine, imageRect.Top + (imageRect.Height / 2.0));
            CenterGuideLine.Visibility = Visibility.Visible;
        }
        else
        {
            CenterGuideLine.Visibility = Visibility.Collapsed;
        }

        if (_snapCenterX)
        {
            CenterGuideLineVertical.Fill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            CenterGuideLineVertical.Height = imageRect.Height;
            Canvas.SetLeft(CenterGuideLineVertical, imageRect.Left + (imageRect.Width / 2.0));
            Canvas.SetTop(CenterGuideLineVertical, imageRect.Top);
            CenterGuideLineVertical.Visibility = Visibility.Visible;
        }
        else
        {
            CenterGuideLineVertical.Visibility = Visibility.Collapsed;
        }

        CenterLabel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Returns the pixel rect of the rendered image content inside ImgPreview (Stretch="Uniform" letterboxes).</summary>
    private Rect GetImageContentRect()
    {
        if (ImgPreview.Source is not BitmapSource bmp) return Rect.Empty;
        double panW = ImgPreview.ActualWidth;
        double panH = ImgPreview.ActualHeight;
        if (panW == 0 || panH == 0) return Rect.Empty;

        double scale = Math.Min(panW / bmp.PixelWidth, panH / bmp.PixelHeight);
        double rendW = bmp.PixelWidth * scale;
        double rendH = bmp.PixelHeight * scale;

        // Compute in OverlayCanvas coordinates (not ImgPreview-local), so letterboxing offsets are correct.
        Point previewTopLeft = ImgPreview.TranslatePoint(new Point(0, 0), OverlayCanvas);
        return new Rect(
            previewTopLeft.X + (panW - rendW) / 2.0,
            previewTopLeft.Y + (panH - rendH) / 2.0,
            rendW,
            rendH);
    }

    // -- Color picker â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async void BtnPickColor_Click(object sender, RoutedEventArgs e)
    {
        Color currentColor = ParseSpectrumColor(_spectrumColor);

        while (true)
        {
            var dlg = new ColorPickerDialog(currentColor) { Owner = this };
            dlg.ShowDialog();   // blocks until dialog closes (returns null for eyedropper)

            if (dlg.EyedropperMode)
            {
                // Dialog closed itself so it can be gc'd; sample a pixel, then reopen
                currentColor = await WaitForEyedropperAsync();
            }
            else if (dlg.DialogResult == true)
            {
                var c = dlg.SelectedColor;
                _spectrumColor = $"0x{c.R:X2}{c.G:X2}{c.B:X2}";
                RctColor.Fill  = new SolidColorBrush(c);
                UpdateOverlay();
                UpdateCommandPreview();
                break;
            }
            else
            {
                break;  // cancelled or X-button
            }
        }
    }

    /// <summary>
    /// Sets the cursor to crosshair, registers a one-shot PreviewMouseDown handler,
    /// and returns a Task that completes with the sampled Color when the user clicks.
    /// </summary>
    private Task<Color> WaitForEyedropperAsync()
    {
        var tcs = new TaskCompletionSource<Color>();
        Mouse.OverrideCursor = Cursors.Cross;
        EnsureEyedropperMagnifierPopup();

        MouseButtonEventHandler? mouseDownHandler = null;
        MouseEventHandler? mouseMoveHandler = null;

        void Cleanup()
        {
            if (mouseDownHandler != null) PreviewMouseDown -= mouseDownHandler;
            if (mouseMoveHandler != null) PreviewMouseMove -= mouseMoveHandler;
            Mouse.OverrideCursor = null;
            HideEyedropperMagnifier();
        }

        mouseMoveHandler = (_, args) =>
        {
            UpdateEyedropperMagnifier(args.GetPosition(ImgPreview), args.GetPosition(this));
        };

        mouseDownHandler = (_, args) =>
        {
            Cleanup();
            args.Handled = true;   // prevent drag / other child handlers
            tcs.TrySetResult(SampleColorAtPoint(args.GetPosition(ImgPreview)));
        };

        PreviewMouseMove += mouseMoveHandler;
        PreviewMouseDown += mouseDownHandler;

        UpdateEyedropperMagnifier(Mouse.GetPosition(ImgPreview), Mouse.GetPosition(this));

        return tcs.Task;
    }

    private void EnsureEyedropperMagnifierPopup()
    {
        if (_eyedropperMagnifierPopup != null)
            return;

        var image = new Image
        {
            Width = EyedropperLensSize,
            Height = EyedropperLensSize,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };

        var layer = new Grid
        {
            Width = EyedropperLensSize,
            Height = EyedropperLensSize,
            ClipToBounds = true,
            IsHitTestVisible = false
        };
        layer.Children.Add(image);
        layer.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = EyedropperLensSize / 2.0,
            X2 = EyedropperLensSize / 2.0,
            Y1 = 0,
            Y2 = EyedropperLensSize,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Opacity = 0.85,
            IsHitTestVisible = false
        });
        layer.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 0,
            X2 = EyedropperLensSize,
            Y1 = EyedropperLensSize / 2.0,
            Y2 = EyedropperLensSize / 2.0,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Opacity = 0.85,
            IsHitTestVisible = false
        });

        var border = new Border
        {
            Child = layer,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false
        };

        _eyedropperMagnifierPopup = new System.Windows.Controls.Primitives.Popup
        {
            AllowsTransparency = true,
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = border
        };
        _eyedropperMagnifierImage = image;
    }

    private void UpdateEyedropperMagnifier(Point posInPreview, Point posInWindow)
    {
        if (_eyedropperMagnifierPopup == null || _eyedropperMagnifierImage == null)
            return;

        if (ImgPreview.Source is not BitmapSource bmp)
        {
            HideEyedropperMagnifier();
            return;
        }

        Rect imageRect = GetImageContentRect();
        if (imageRect == Rect.Empty)
        {
            HideEyedropperMagnifier();
            return;
        }

        Point posInCanvas = ImgPreview.TranslatePoint(posInPreview, OverlayCanvas);
        if (!imageRect.Contains(posInCanvas))
        {
            HideEyedropperMagnifier();
            return;
        }

        double relX = Math.Clamp((posInCanvas.X - imageRect.Left) / imageRect.Width, 0, 1);
        double relY = Math.Clamp((posInCanvas.Y - imageRect.Top) / imageRect.Height, 0, 1);
        int px = Math.Clamp((int)(relX * bmp.PixelWidth), 0, bmp.PixelWidth - 1);
        int py = Math.Clamp((int)(relY * bmp.PixelHeight), 0, bmp.PixelHeight - 1);

        int radius = EyedropperSampleSize / 2;
        int x0 = Math.Max(0, px - radius);
        int y0 = Math.Max(0, py - radius);
        int x1 = Math.Min(bmp.PixelWidth - 1, px + radius);
        int y1 = Math.Min(bmp.PixelHeight - 1, py + radius);
        int sampleW = Math.Max(1, x1 - x0 + 1);
        int sampleH = Math.Max(1, y1 - y0 + 1);

        BitmapSource src = bmp.Format == PixelFormats.Bgra32
            ? bmp
            : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);

        _eyedropperMagnifierImage.Source = new CroppedBitmap(src, new Int32Rect(x0, y0, sampleW, sampleH));

        double popupX = posInWindow.X + EyedropperLensOffset;
        double popupY = posInWindow.Y + EyedropperLensOffset;

        if (popupX + EyedropperLensSize + 8 > ActualWidth)
            popupX = posInWindow.X - EyedropperLensSize - EyedropperLensOffset;
        if (popupY + EyedropperLensSize + 8 > ActualHeight)
            popupY = posInWindow.Y - EyedropperLensSize - EyedropperLensOffset;

        popupX = Math.Clamp(popupX, 0, Math.Max(0, ActualWidth - EyedropperLensSize));
        popupY = Math.Clamp(popupY, 0, Math.Max(0, ActualHeight - EyedropperLensSize));

        _eyedropperMagnifierPopup.HorizontalOffset = popupX;
        _eyedropperMagnifierPopup.VerticalOffset = popupY;
        _eyedropperMagnifierPopup.IsOpen = true;
    }

    private void HideEyedropperMagnifier()
    {
        if (_eyedropperMagnifierPopup != null)
            _eyedropperMagnifierPopup.IsOpen = false;

        if (_eyedropperMagnifierImage != null)
            _eyedropperMagnifierImage.Source = null;
    }

    /// <summary>
    /// Translates a point in ImgPreview layout coordinates to a real image pixel,
    /// converts the bitmap to Bgra32, reads the pixel, and returns the Color.
    /// </summary>
    private Color SampleColorAtPoint(Point posInPreview)
    {
        if (ImgPreview.Source is not BitmapSource bmp) return Colors.White;
        Rect img = GetImageContentRect();
        if (img == Rect.Empty) return Colors.White;

        Point posInCanvas = ImgPreview.TranslatePoint(posInPreview, OverlayCanvas);

        // Map canvas coords -> [0,1] within the image content rect, then to real px
        double relX = Math.Clamp((posInCanvas.X - img.Left) / img.Width, 0, 1);
        double relY = Math.Clamp((posInCanvas.Y - img.Top) / img.Height, 0, 1);
        int px = Math.Clamp((int)(relX * bmp.PixelWidth), 0, bmp.PixelWidth - 1);
        int py = Math.Clamp((int)(relY * bmp.PixelHeight), 0, bmp.PixelHeight - 1);

        // Normalize to Bgra32 so the byte layout is always B G R A
        var src = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        byte[] pixel = new byte[4];
        src.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);   // BGRA -> RGB
    }

    private static Color ParseSpectrumColor(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && hex.Length == 8
            && int.TryParse(hex[2..], System.Globalization.NumberStyles.HexNumber, null, out int val))
            return Color.FromRgb((byte)(val >> 16), (byte)((val >> 8) & 0xFF), (byte)(val & 0xFF));
        return Color.FromRgb(255, 255, 255);
    }

    private void ShowError(string message) =>
        MessageBox.Show(message, Loc.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
}






