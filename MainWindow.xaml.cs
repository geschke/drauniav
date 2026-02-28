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

namespace AudioVisualizer;

public partial class MainWindow : Window
{
    // ── Color state ───────────────────────────────────────────────────────────
    private string _spectrumColor = "0xFFFFFF";   // FFmpeg 0xRRGGBB hex string

    // ── Drag state ────────────────────────────────────────────────────────────
    private const double MinOverlayWidthPreview = 40.0;
    private const double MinOverlayHeightPreview = 20.0;
    private const double SnapThresholdPreview = 10.0;
    private const double HandleSizePreview = 6.0;
    private const double MoveEdgeExclusionPreview = 5.0;

    private bool _overlayInitialized;
    private bool _isDragging;
    private bool _snapCenterX;
    private bool _snapCenterY;

    // Stored in real image pixels for direct FFmpeg usage.
    private double _overlayX;
    private double _overlayY;
    private double _overlayWidth;
    private double _overlayHeight;

    private Point _dragStartMouse;
    private Rect _dragStartRectPreview;
    private OverlayDragMode _dragMode;

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

    // ── Browse handlers ──────────────────────────────────────────────────────

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select background image",
            Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
        };
        if (dlg.ShowDialog() == true)
        {
            TxtImage.Text = dlg.FileName;
            var bitmap = new BitmapImage(new Uri(dlg.FileName));
            ImgPreview.Source = bitmap;
            _overlayInitialized = false;
            _snapCenterX = false;
            _snapCenterY = false;
            UpdateOverlay();
            UpdateImageInfo(dlg.FileName, bitmap);
        }
    }

    private void BrowseAudio_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select audio file",
            Filter = "MP3 files (*.mp3)|*.mp3|All audio files (*.mp3;*.wav;*.aac)|*.mp3;*.wav;*.aac"
        };
        if (dlg.ShowDialog() == true)
            TxtAudio.Text = dlg.FileName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save output video",
            Filter = "MP4 video (*.mp4)|*.mp4",
            DefaultExt = ".mp4"
        };
        if (dlg.ShowDialog() == true)
            TxtOutput.Text = dlg.FileName;
    }

    private void UpdateImageInfo(string imagePath, BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        string aspect = BuildAspectRatioLabel(width, height);
        double sizeInMb = new FileInfo(imagePath).Length / (1024d * 1024d);
        string format = GetImageFormatLabel(imagePath);

        TxtImageInfo.Text =
            $"Image: {width} \u00D7 {height} px   Aspect: {aspect}   Size: {sizeInMb.ToString("0.0", CultureInfo.InvariantCulture)} MB   Format: {format}";
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
            _ => "Unknown"
        };
    }

    // ── Generate ─────────────────────────────────────────────────────────────

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
                ShowError("Could not detect image resolution via ffprobe.");
                return;
            }

            // Build filter options from UI
            string color    = GetColor();
            string drawMode = GetDrawMode();
            string overlay  = BuildOverlayFilter(width, height, color, drawMode);

            string args = BuildFfmpegArgs(imagePath, audioPath, outputPath, width, height, overlay);

            string? error = await RunProcessAsync(ffmpeg, args);
            if (error != null)
            {
                ShowError($"FFmpeg error:\n\n{error}");
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
                MessageBox.Show("Done! Video saved to:\n" + outputPath,
                                "Audio Visualizer", MessageBoxButton.OK, MessageBoxImage.Information);
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

    // ── Validation ────────────────────────────────────────────────────────────

    private bool ValidateInputs(out string image, out string audio, out string output)
    {
        image  = TxtImage.Text.Trim();
        audio  = TxtAudio.Text.Trim();
        output = TxtOutput.Text.Trim();

        if (string.IsNullOrEmpty(image) || !File.Exists(image))
        { MessageBox.Show("Please select a valid background image.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

        if (string.IsNullOrEmpty(audio) || !File.Exists(audio))
        { MessageBox.Show("Please select a valid audio file.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

        if (string.IsNullOrEmpty(output))
        { MessageBox.Show("Please choose an output file path.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

        return true;
    }

    // ── Tool resolution ───────────────────────────────────────────────────────

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

    // ── ffprobe – image resolution ────────────────────────────────────────────

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

    // ── Filter / argument builders ────────────────────────────────────────────

    private string GetColor() => _spectrumColor;

    private string GetDrawMode() => CboStyle.SelectedIndex == 1 ? "p2p" : "line";

    private string BuildOverlayFilter(int imgWidth, int imgHeight, string color, string drawMode)
    {
        EnsureOverlayStateForImageSize(imgWidth, imgHeight);

        int waveW = Math.Clamp((int)Math.Round(_overlayWidth), 1, imgWidth);
        int waveH = Math.Clamp((int)Math.Round(_overlayHeight), 1, imgHeight);
        int posX = Math.Clamp((int)Math.Round(_overlayX), 0, imgWidth - waveW);
        int posY = Math.Clamp((int)Math.Round(_overlayY), 0, imgHeight - waveH);

        // showwaves filter: scale audio to overlay rectangle size and draw waveform
        string showwaves =
            $"showwaves=s={waveW}x{waveH}:mode={drawMode}:colors={color}:rate=25";

        // Overlay waveform on static image at selected rectangle position
        string filter =
            $"[1:a]{showwaves}[wave];" +
            $"[0:v][wave]overlay={posX}:{posY}[v]";

        return filter;
    }

    private static string BuildFfmpegArgs(
        string image, string audio, string output,
        int width, int height,
        string filterComplex)
    {
        // -loop 1        : loop the still image
        // -i image       : input 0 – background
        // -i audio       : input 1 – audio (also drives showwaves)
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

    // ── Process helpers ────────────────────────────────────────────────────────

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
        double scaleX = imageRect.Width / bmp.PixelWidth;
        double scaleY = imageRect.Height / bmp.PixelHeight;

        return new Rect(
            imageRect.Left + (_overlayX * scaleX),
            imageRect.Top + (_overlayY * scaleY),
            _overlayWidth * scaleX,
            _overlayHeight * scaleY);
    }

    private void UpdateOverlayFromPreviewRect(Rect previewRect, Rect imageRect, BitmapSource bmp)
    {
        double scaleX = bmp.PixelWidth / imageRect.Width;
        double scaleY = bmp.PixelHeight / imageRect.Height;

        _overlayX = (previewRect.Left - imageRect.Left) * scaleX;
        _overlayY = (previewRect.Top - imageRect.Top) * scaleY;
        _overlayWidth = previewRect.Width * scaleX;
        _overlayHeight = previewRect.Height * scaleY;

        EnsureOverlayStateForImageSize(bmp.PixelWidth, bmp.PixelHeight);
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
        OverlayTopLine.Visibility = Visibility.Collapsed;
        OverlayBottomLine.Visibility = Visibility.Collapsed;
        OverlayLeftLine.Visibility = Visibility.Collapsed;
        OverlayRightLine.Visibility = Visibility.Collapsed;

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
        OverlayBar.Fill = new SolidColorBrush(Color.FromArgb(115, tint.R, tint.G, tint.B));
        OverlayBar.Width = overlayRect.Width;
        OverlayBar.Height = overlayRect.Height;
        Canvas.SetLeft(OverlayBar, overlayRect.Left);
        Canvas.SetTop(OverlayBar, overlayRect.Top);
        OverlayBar.Visibility = Visibility.Visible;

        // Bright border lines
        var lineBrush = new SolidColorBrush(Color.FromArgb(220, tint.R, tint.G, tint.B));

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

        SetHandlePosition(HandleTopLeft, overlayRect.Left, overlayRect.Top);
        SetHandlePosition(HandleTop, midX, overlayRect.Top);
        SetHandlePosition(HandleTopRight, overlayRect.Right, overlayRect.Top);
        SetHandlePosition(HandleRight, overlayRect.Right, midY);
        SetHandlePosition(HandleBottomRight, overlayRect.Right, overlayRect.Bottom);
        SetHandlePosition(HandleBottom, midX, overlayRect.Bottom);
        SetHandlePosition(HandleBottomLeft, overlayRect.Left, overlayRect.Bottom);
        SetHandlePosition(HandleLeft, overlayRect.Left, midY);

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

    // -- Color picker ──────────────────────────────────────────────────────────

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

        MouseButtonEventHandler? handler = null;
        handler = (_, args) =>
        {
            PreviewMouseDown -= handler;
            Mouse.OverrideCursor = null;
            args.Handled = true;   // prevent drag / other child handlers
            tcs.SetResult(SampleColorAtPoint(args.GetPosition(ImgPreview)));
        };
        PreviewMouseDown += handler;

        return tcs.Task;
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

        // Map preview coords → [0,1] within the image content rect, then to real px
        double relX = Math.Clamp((posInPreview.X - img.Left) / img.Width,  0, 1);
        double relY = Math.Clamp((posInPreview.Y - img.Top)  / img.Height, 0, 1);
        int px = Math.Clamp((int)(relX * bmp.PixelWidth),  0, bmp.PixelWidth  - 1);
        int py = Math.Clamp((int)(relY * bmp.PixelHeight), 0, bmp.PixelHeight - 1);

        // Normalise to Bgra32 so the byte layout is always B G R A
        var src = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        byte[] pixel = new byte[4];
        src.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);   // BGRA → RGB
    }

    private static Color ParseSpectrumColor(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && hex.Length == 8
            && int.TryParse(hex[2..], System.Globalization.NumberStyles.HexNumber, null, out int val))
            return Color.FromRgb((byte)(val >> 16), (byte)((val >> 8) & 0xFF), (byte)(val & 0xFF));
        return Color.FromRgb(255, 255, 255);
    }

    private void ShowError(string message) =>
        MessageBox.Show(message, "Audio Visualizer – Error", MessageBoxButton.OK, MessageBoxImage.Error);
}


