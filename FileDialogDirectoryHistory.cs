using System.IO;
using System.Text.Json;

namespace Drauniav;

public static class FileDialogDirectoryHistory
{
    private const string DirectoriesFileName = "file-dialog-directories.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string? LoadImageDirectory() => NormalizeDirectory(Load().ImageDirectory);

    public static string? LoadAudioDirectory() => NormalizeDirectory(Load().AudioDirectory);

    public static string? LoadOutputDirectory() => NormalizeDirectory(Load().OutputDirectory);

    public static void SaveImagePath(string path)
    {
        var dto = Load();
        dto.ImageDirectory = GetExistingDirectoryFromPath(path) ?? dto.ImageDirectory;
        Save(dto);
    }

    public static void SaveAudioPath(string path)
    {
        var dto = Load();
        dto.AudioDirectory = GetExistingDirectoryFromPath(path) ?? dto.AudioDirectory;
        Save(dto);
    }

    public static void SaveOutputPath(string path)
    {
        var dto = Load();
        dto.OutputDirectory = GetExistingDirectoryFromPath(path) ?? dto.OutputDirectory;
        Save(dto);
    }

    private static string GetDirectoriesPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Drauniav");
        return Path.Combine(dir, DirectoriesFileName);
    }

    private static DialogDirectoriesDto Load()
    {
        try
        {
            string path = GetDirectoriesPath();
            if (!File.Exists(path))
                return new DialogDirectoriesDto();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DialogDirectoriesDto>(json) ?? new DialogDirectoriesDto();
        }
        catch
        {
            return new DialogDirectoriesDto();
        }
    }

    private static void Save(DialogDirectoriesDto dto)
    {
        try
        {
            string path = GetDirectoriesPath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort persistence only.
        }
    }

    private static string? GetExistingDirectoryFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string? dir = Path.GetDirectoryName(path);
        return NormalizeDirectory(dir);
    }

    private static string? NormalizeDirectory(string? directory)
    {
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : null;
    }

    private sealed class DialogDirectoriesDto
    {
        public string? ImageDirectory { get; set; }
        public string? AudioDirectory { get; set; }
        public string? OutputDirectory { get; set; }
    }
}
