namespace KLauncher.Services;

/// <summary>
/// "Window fade" setting texts (name, description), Turkish and English;
/// other languages use the English text.
/// </summary>
internal static class WindowFadeTexts
{
    public static bool TryGet(string languageCode, string key, out string value)
    {
        value = string.Empty;
        if (key.Equals("OpenErrorLog", StringComparison.OrdinalIgnoreCase))
        {
            value = languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase) ? "Hata kaydını aç" : "Open error log";
            return true;
        }

        var isName = key.Equals("SettingsWindowFade", StringComparison.OrdinalIgnoreCase);
        if (!isName && !key.Equals("WindowFadeDescription", StringComparison.OrdinalIgnoreCase))
            return false;

        var pair = For(languageCode) ?? For("en")!;
        value = isName ? pair[0] : pair[1];
        return true;
    }

    private static string[]? For(string languageCode) => languageCode switch
    {
        "tr" => ["Pencere solması", "Açıkken K-Launcher küçültülürken ve arka plana geçince saydamlaşır; kapalıyken hep tam görünür kalır"],
        "en" => ["Window fade", "When on, K-Launcher fades out when minimized or in the background; when off it always stays fully visible"],
        _ => null
    };
}
