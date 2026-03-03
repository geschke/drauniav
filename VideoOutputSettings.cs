namespace Drauniav;

public sealed class VideoOutputSettings
{
    public bool UseImageResolution { get; set; } = true;
    public int Width { get; set; }
    public int Height { get; set; }
    public bool KeepAspectRatio { get; set; } = true;
    public int LockedAspectNumerator { get; set; }
    public int LockedAspectDenominator { get; set; }

    public VideoOutputSettings Clone() =>
        new()
        {
            UseImageResolution = UseImageResolution,
            Width = Width,
            Height = Height,
            KeepAspectRatio = KeepAspectRatio,
            LockedAspectNumerator = LockedAspectNumerator,
            LockedAspectDenominator = LockedAspectDenominator
        };

    public void ResetToImage(int imageWidth, int imageHeight)
    {
        UseImageResolution = true;
        Width = imageWidth;
        Height = imageHeight;
        KeepAspectRatio = true;
        int gcd = GreatestCommonDivisor(imageWidth, imageHeight);
        LockedAspectNumerator = imageWidth / gcd;
        LockedAspectDenominator = imageHeight / gcd;
    }

    public static VideoOutputSettings CreateDefault() => new();

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
}
