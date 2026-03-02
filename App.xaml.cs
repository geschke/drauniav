using System.Windows;
using System.IO;
using System.Windows.Media.Imaging;
using System.Linq;

namespace Drauniav;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LocalizationManager.Initialize(this);

        bool simulateMissingFfmpeg = Array.Exists(
            e.Args,
            arg => string.Equals(arg, "--simulate-missing-ffmpeg", StringComparison.OrdinalIgnoreCase));

        bool ffmpegAvailable = IsToolAvailable("ffmpeg.exe");
        bool ffprobeAvailable = IsToolAvailable("ffprobe.exe");
        bool showMissingFfmpegDialog = simulateMissingFfmpeg || !ffmpegAvailable || !ffprobeAvailable;

        var mainWindow = new MainWindow();
        TryApplyWindowIcon(mainWindow);
        MainWindow = mainWindow;
        if (showMissingFfmpegDialog)
        {
            void ShowDialogAfterRender(object? _, EventArgs __)
            {
                mainWindow.ContentRendered -= ShowDialogAfterRender;
                var dialog = new FfmpegMissingDialog { Owner = mainWindow };
                dialog.ShowDialog();
            }

            mainWindow.ContentRendered += ShowDialogAfterRender;
        }

        mainWindow.Show();
    }

    private static void TryApplyWindowIcon(Window window)
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Drauniav.ico");
        if (!File.Exists(iconPath))
            return;

        using var stream = File.OpenRead(iconPath);
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        BitmapFrame? bestFrame = decoder.Frames
            .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
            .FirstOrDefault();

        if (bestFrame is null)
            return;

        bestFrame.Freeze();
        window.Icon = bestFrame;
    }

    private static bool IsToolAvailable(string exeName)
    {
        string localPath = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(localPath))
            return true;

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(directory, exeName);
                if (File.Exists(candidate))
                    return true;
            }
            catch
            {
                // Ignore invalid path entries and continue search.
            }
        }

        return false;
    }
}
