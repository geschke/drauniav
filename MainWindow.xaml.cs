using System.Diagnostics;
using System.IO;
using System.Text;
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
    private double _offsetY;           // bar offset from image center in real image pixels (0 = centered)
    private bool   _isDragging;
    private double _dragStartMouseY;
    private double _dragStartOffsetY;

    public MainWindow()
    {
        InitializeComponent();
        ImgPreview.SizeChanged         += (_, _) => UpdateOverlay();
        OverlayBar.MouseLeftButtonDown += OverlayBar_MouseDown;
        OverlayBar.MouseLeftButtonUp   += OverlayBar_MouseUp;
        OverlayBar.MouseMove           += OverlayBar_MouseMove;
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
            ImgPreview.Source = new BitmapImage(new Uri(dlg.FileName));
            UpdateOverlay();
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

            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            MessageBox.Show("Done! Video saved to:\n" + outputPath,
                            "Audio Visualizer", MessageBoxButton.OK, MessageBoxImage.Information);
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
        // Wave height = 20% of image height, minimum 80px
        int waveH = Math.Max(80, imgHeight / 5);

        int    offsetPx = (int)Math.Round(_offsetY);
        string yExpr   = offsetPx == 0
            ? $"({imgHeight}-{waveH})/2"
            : $"({imgHeight}-{waveH})/2+{offsetPx}";

        // showwaves filter: scale audio to image width, draw waveform
        string showwaves =
            $"showwaves=s={imgWidth}x{waveH}:mode={drawMode}:colors={color}:rate=25";

        // Overlay waveform on static image
        // [0:v] is the image (looped), [1:a] feeds showwaves → [wave] → overlay → [v]
        string filter =
            $"[1:a]{showwaves}[wave];" +
            $"[0:v][wave]overlay=0:{yExpr}[v]";

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

    // ── Spectrum position overlay ──────────────────────────────────────────────

    /// <summary>Redraws the semi-transparent bar showing where the spectrum will appear.</summary>
    private void UpdateOverlay()
    {
        Rect img = GetImageContentRect();
        if (img == Rect.Empty)
        {
            OverlayBar.Visibility = OverlayTopLine.Visibility = OverlayBottomLine.Visibility = Visibility.Collapsed;
            CenterGuideLine.Visibility = CenterLabel.Visibility = Visibility.Collapsed;
            return;
        }

        // Mirror the FFmpeg wave height: 20 % of image height
        var    bmpSrc      = (BitmapSource)ImgPreview.Source;
        double previewScale = img.Height / bmpSrc.PixelHeight;
        double barH = Math.Max(6, img.Height * 0.20);
        double barY = Math.Clamp(
            img.Top + (img.Height - barH) / 2.0 + _offsetY * previewScale,
            img.Top, img.Bottom - barH);

        Color tint = ParseSpectrumColor(_spectrumColor);

        // Semi-transparent fill
        OverlayBar.Fill   = new SolidColorBrush(Color.FromArgb(115, tint.R, tint.G, tint.B));
        OverlayBar.Width  = img.Width;
        OverlayBar.Height = barH;
        Canvas.SetLeft(OverlayBar, img.Left);
        Canvas.SetTop(OverlayBar,  barY);
        OverlayBar.Visibility = Visibility.Visible;

        // Bright border lines (top and bottom)
        var lineBrush = new SolidColorBrush(Color.FromArgb(220, tint.R, tint.G, tint.B));

        OverlayTopLine.Fill  = lineBrush;
        OverlayTopLine.Width = img.Width;
        Canvas.SetLeft(OverlayTopLine, img.Left);
        Canvas.SetTop(OverlayTopLine,  barY);
        OverlayTopLine.Visibility = Visibility.Visible;

        OverlayBottomLine.Fill  = lineBrush;
        OverlayBottomLine.Width = img.Width;
        Canvas.SetLeft(OverlayBottomLine, img.Left);
        Canvas.SetTop(OverlayBottomLine,  barY + barH - 1);
        OverlayBottomLine.Visibility = Visibility.Visible;

        // Center snap guide – visible only when snapped
        if (_offsetY == 0.0)
        {
            double guideY = img.Top + img.Height / 2.0;
            CenterGuideLine.Fill  = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            CenterGuideLine.Width = img.Width;
            Canvas.SetLeft(CenterGuideLine, img.Left);
            Canvas.SetTop(CenterGuideLine,  guideY);
            CenterGuideLine.Visibility = Visibility.Visible;

            Canvas.SetLeft(CenterLabel, img.Left + 4);
            Canvas.SetTop(CenterLabel,  guideY + 2);
            CenterLabel.Visibility = Visibility.Visible;
        }
        else
        {
            CenterGuideLine.Visibility = CenterLabel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Returns the pixel rect of the rendered image content inside ImgPreview (Stretch="Uniform" letterboxes).</summary>
    private Rect GetImageContentRect()
    {
        if (ImgPreview.Source is not BitmapSource bmp) return Rect.Empty;
        double panW = ImgPreview.ActualWidth;
        double panH = ImgPreview.ActualHeight;
        if (panW == 0 || panH == 0) return Rect.Empty;

        double scale = Math.Min(panW / bmp.PixelWidth, panH / bmp.PixelHeight);
        double rendW = bmp.PixelWidth  * scale;
        double rendH = bmp.PixelHeight * scale;
        return new Rect((panW - rendW) / 2.0, (panH - rendH) / 2.0, rendW, rendH);
    }

    // ── Overlay bar drag handlers ─────────────────────────────────────────────

    private void OverlayBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging       = true;
        _dragStartMouseY  = e.GetPosition(OverlayCanvas).Y;
        _dragStartOffsetY = _offsetY;
        OverlayBar.CaptureMouse();
    }

    private void OverlayBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        Rect img = GetImageContentRect();
        if (img == Rect.Empty || ImgPreview.Source is not BitmapSource bmpSrc) return;

        double previewScale = img.Height / bmpSrc.PixelHeight;
        double newOffsetY   = _dragStartOffsetY + (e.GetPosition(OverlayCanvas).Y - _dragStartMouseY) / previewScale;

        // Snap to center when within 10 preview pixels of it
        if (Math.Abs(newOffsetY * previewScale) < 10.0)
            newOffsetY = 0.0;

        // Clamp: bar must stay fully inside the image
        double maxOffset = (bmpSrc.PixelHeight - Math.Max(80, bmpSrc.PixelHeight / 5)) / 2.0;
        _offsetY = Math.Clamp(newOffsetY, -maxOffset, maxOffset);

        UpdateOverlay();
    }

    private void OverlayBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        OverlayBar.ReleaseMouseCapture();
    }

    // ── Color picker ──────────────────────────────────────────────────────────

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
