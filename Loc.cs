using System.Windows;

namespace Drauniav;

public static class Loc
{
    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string value)
            return value;

        return key;
    }
}
