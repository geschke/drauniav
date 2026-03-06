using System.Globalization;

namespace Drauniav;

public sealed class VisualizerSettings
{
    public string PresetName { get; set; } = "Soft Line";
    public string FilterType { get; set; } = "showfreqs"; // showwaves | showfreqs
    public string ChannelMode { get; set; } = "mono"; // mono | stereo_split | stereo_mirror
    public string Mode { get; set; } = "line";
    public int Rate { get; set; } = 24;
    public double VolumeDb { get; set; } = 18.0;
    public int LineThickness { get; set; } = 2; // 1 = no dilation pass
    public double Alpha { get; set; } = 0.95;   // 0..1
    public bool UseColorKey { get; set; } = true;
    public double ColorKeySimilarity { get; set; } = 0.10;
    public double ColorKeyBlend { get; set; } = 0.0;
    public string AScale { get; set; } = "sqrt";
    public int WinSize { get; set; } = 4096;
    public string FScale { get; set; } = "log";
    public bool SmoothSpectrum { get; set; } = false;
    public int Smoothness { get; set; } = 60;
    public bool AutoHeadroom { get; set; } = false;
    public bool UseMinAmplitude { get; set; } = false;
    public int MinAmplitude { get; set; } = 20;

    public VisualizerSettings Clone() =>
        new()
        {
            PresetName = PresetName,
            FilterType = FilterType,
            ChannelMode = ChannelMode,
            Mode = Mode,
            Rate = Rate,
            VolumeDb = VolumeDb,
            LineThickness = LineThickness,
            Alpha = Alpha,
            UseColorKey = UseColorKey,
            ColorKeySimilarity = ColorKeySimilarity,
            ColorKeyBlend = ColorKeyBlend,
            AScale = AScale,
            WinSize = WinSize,
            FScale = FScale,
            SmoothSpectrum = SmoothSpectrum,
            Smoothness = Smoothness,
            AutoHeadroom = AutoHeadroom,
            UseMinAmplitude = UseMinAmplitude,
            MinAmplitude = MinAmplitude
        };

    public string ToSummaryText()
    {
        string alphaText = $"{Math.Round(Alpha * 100):0}%";
        return $"{PresetName} | {FilterType}:{Mode} | {Rate} fps | {alphaText} alpha";
    }

    public static string[] PresetNames => ["Soft Line", "Classic Bars", "Wide Wave", "Crisp Spectrum"];

    public static VisualizerSettings CreateDefault() => CreatePreset("Soft Line");

    public static VisualizerSettings CreatePreset(string presetName)
    {
        return presetName switch
        {
            "Classic Bars" => new VisualizerSettings
            {
                PresetName = "Classic Bars",
                FilterType = "showfreqs",
                ChannelMode = "mono",
                Mode = "bar",
                Rate = 24,
                VolumeDb = 16.0,
                LineThickness = 2,
                Alpha = 0.92,
                UseColorKey = true,
                ColorKeySimilarity = 0.12,
                ColorKeyBlend = 0.0,
                AScale = "sqrt",
                WinSize = 4096,
                FScale = "log",
                SmoothSpectrum = false,
                Smoothness = 50,
                AutoHeadroom = false,
                UseMinAmplitude = false,
                MinAmplitude = 25
            },
            "Wide Wave" => new VisualizerSettings
            {
                PresetName = "Wide Wave",
                FilterType = "showwaves",
                ChannelMode = "mono",
                Mode = "p2p",
                Rate = 30,
                VolumeDb = 10.0,
                LineThickness = 3,
                Alpha = 0.90,
                UseColorKey = true,
                ColorKeySimilarity = 0.10,
                ColorKeyBlend = 0.0,
                AScale = "sqrt",
                WinSize = 4096,
                FScale = "log",
                SmoothSpectrum = false,
                Smoothness = 0,
                AutoHeadroom = false,
                UseMinAmplitude = false,
                MinAmplitude = 0
            },
            "Crisp Spectrum" => new VisualizerSettings
            {
                PresetName = "Crisp Spectrum",
                FilterType = "showfreqs",
                ChannelMode = "mono",
                Mode = "line",
                Rate = 30,
                VolumeDb = 18.0,
                LineThickness = 3,
                Alpha = 0.98,
                UseColorKey = true,
                ColorKeySimilarity = 0.08,
                ColorKeyBlend = 0.0,
                AScale = "sqrt",
                WinSize = 4096,
                FScale = "log",
                SmoothSpectrum = false,
                Smoothness = 35,
                AutoHeadroom = false,
                UseMinAmplitude = false,
                MinAmplitude = 30
            },
            _ => new VisualizerSettings
            {
                PresetName = "Soft Line",
                FilterType = "showfreqs",
                ChannelMode = "mono",
                Mode = "line",
                Rate = 24,
                VolumeDb = 18.0,
                LineThickness = 2,
                Alpha = 0.95,
                UseColorKey = true,
                ColorKeySimilarity = 0.10,
                ColorKeyBlend = 0.0,
                AScale = "sqrt",
                WinSize = 4096,
                FScale = "log",
                SmoothSpectrum = false,
                Smoothness = 60,
                AutoHeadroom = false,
                UseMinAmplitude = false,
                MinAmplitude = 20
            }
        };
    }

    public static string DetectPresetName(VisualizerSettings value)
    {
        foreach (string preset in PresetNames)
        {
            if (IsEquivalent(value, CreatePreset(preset)))
                return preset;
        }
        return "Custom";
    }

    private static bool IsEquivalent(VisualizerSettings a, VisualizerSettings b)
    {
        return a.FilterType == b.FilterType
            && a.ChannelMode == b.ChannelMode
            && a.Mode == b.Mode
            && a.Rate == b.Rate
            && Math.Abs(a.VolumeDb - b.VolumeDb) < 0.001
            && a.LineThickness == b.LineThickness
            && Math.Abs(a.Alpha - b.Alpha) < 0.001
            && a.UseColorKey == b.UseColorKey
            && Math.Abs(a.ColorKeySimilarity - b.ColorKeySimilarity) < 0.001
            && Math.Abs(a.ColorKeyBlend - b.ColorKeyBlend) < 0.001
            && a.AScale == b.AScale
            && a.WinSize == b.WinSize
            && a.FScale == b.FScale
            && a.SmoothSpectrum == b.SmoothSpectrum
            && a.Smoothness == b.Smoothness
            && a.AutoHeadroom == b.AutoHeadroom
            && a.UseMinAmplitude == b.UseMinAmplitude
            && a.MinAmplitude == b.MinAmplitude;
    }

    public static string FormatDouble(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
