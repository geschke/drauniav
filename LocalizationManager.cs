using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Drauniav;

public enum AppLanguage
{
    System,
    German,
    English
}

public static class LocalizationManager
{
    private const string PreferencesFileName = "preferences.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static event EventHandler? LanguageChanged;

    public static AppLanguage SelectedLanguage { get; private set; } = AppLanguage.System;

    public static string CurrentLanguageCode =>
        ResolveLanguageCode(SelectedLanguage, CultureInfo.CurrentUICulture);

    public static void Initialize(Application app)
    {
        AppLanguage preferred = LoadPreferredLanguage();
        ApplyLanguageInternal(app, preferred, persist: false, raiseEvent: false);
    }

    public static void SetPreferredLanguage(AppLanguage language)
    {
        if (Application.Current == null)
            return;

        ApplyLanguageInternal(Application.Current, language, persist: true, raiseEvent: true);
    }

    private static void ApplyLanguageInternal(Application app, AppLanguage language, bool persist, bool raiseEvent)
    {
        SelectedLanguage = language;

        string code = ResolveLanguageCode(language, CultureInfo.CurrentUICulture);
        var culture = new CultureInfo(code);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            string? source = app.Resources.MergedDictionaries[i].Source?.OriginalString;
            if (!string.IsNullOrEmpty(source) && source.Contains("Localization/Strings.", StringComparison.OrdinalIgnoreCase))
                app.Resources.MergedDictionaries.RemoveAt(i);
        }

        var langDict = new ResourceDictionary
        {
            Source = new Uri($"Localization/Strings.{code}.xaml", UriKind.Relative)
        };
        app.Resources.MergedDictionaries.Add(langDict);

        if (persist)
            SavePreferredLanguage(language);

        if (raiseEvent)
            LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static string ResolveLanguageCode(AppLanguage language, CultureInfo systemCulture)
    {
        return language switch
        {
            AppLanguage.German => "de",
            AppLanguage.English => "en",
            _ => systemCulture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en"
        };
    }

    private static string GetPreferencesPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Drauniav");
        return Path.Combine(dir, PreferencesFileName);
    }

    private static AppLanguage LoadPreferredLanguage()
    {
        try
        {
            string path = GetPreferencesPath();
            if (!File.Exists(path))
                return AppLanguage.System;

            string json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<AppPreferencesDto>(json);
            if (dto == null)
                return AppLanguage.System;

            return dto.Language switch
            {
                nameof(AppLanguage.German) => AppLanguage.German,
                nameof(AppLanguage.English) => AppLanguage.English,
                _ => AppLanguage.System
            };
        }
        catch
        {
            return AppLanguage.System;
        }
    }

    private static void SavePreferredLanguage(AppLanguage language)
    {
        try
        {
            string path = GetPreferencesPath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var dto = new AppPreferencesDto { Language = language.ToString() };
            string json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort persistence only.
        }
    }

    private sealed class AppPreferencesDto
    {
        public string Language { get; set; } = nameof(AppLanguage.System);
    }
}
