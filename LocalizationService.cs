using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace unity_cli_ui;

public static class LocalizationService
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static ResourceDictionary? _languageResources;

    public static string CurrentLanguage { get; private set; } = Chinese;
    public static bool IsEnglish => CurrentLanguage == English;
    public static event EventHandler? LanguageChanged;

    public static void Initialize(string? language) => SetLanguage(language, notify: false);

    public static void SetLanguage(string? language) => SetLanguage(language, notify: true);

    public static string Get(string key)
    {
        if (_languageResources is null || !_languageResources.Contains(key))
        {
            return key;
        }
        return _languageResources[key] as string ?? key;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    private static void SetLanguage(string? language, bool notify)
    {
        var selectedLanguage = string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : Chinese;
        if (notify && selectedLanguage == CurrentLanguage && _languageResources is not null)
        {
            return;
        }

        CurrentLanguage = selectedLanguage;
        var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (Application.Current is { } application)
        {
            if (_languageResources is not null)
            {
                application.Resources.MergedDictionaries.Remove(_languageResources);
            }

            _languageResources = LoadLanguageResources(CurrentLanguage);
            application.Resources.MergedDictionaries.Add(_languageResources);
        }

        if (notify)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static ResourceDictionary LoadLanguageResources(string language)
    {
        var assemblyName = typeof(LocalizationService).Assembly.GetName().Name;
        var uri = new Uri($"/{assemblyName};component/Resources/Strings.{language}.json", UriKind.Relative);
        var resource = Application.GetResourceStream(uri)
            ?? throw new InvalidOperationException($"Language resource was not found: {uri}");
        using var stream = resource.Stream;
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new JsonException($"Language resource is empty: {uri}");
        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in values)
        {
            dictionary[key] = value;
        }
        return dictionary;
    }
}
