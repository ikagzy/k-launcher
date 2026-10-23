namespace KLauncher.Services;

/// <summary>
/// Translations of the 4.0 texts (showcase, particles, driver tools, release
/// notes, install location, touch screen code, tray, moved hard-coded
/// messages) for the 118 languages other than Turkish and English, plus the
/// info window texts that some older catalogs never had.
/// Each language lists its values in <see cref="Keys"/> / <see cref="InfoKeys"/>
/// order inside a switch, so only the selected language's array is ever
/// created. A list with the wrong length is ignored (English is shown) rather
/// than showing texts under the wrong keys.
/// </summary>
internal static partial class MoreTranslations
{
    internal static readonly string[] Keys =
    [
        "ExitLauncher", "InstallLocationDescription", "InstallLocationHeading", "InstallLocationOpen",
        "InterfaceOverview", "InterfaceOverviewDescription", "InterfaceOverviewHeader", "LaunchAtStartup",
        "NiniteCatDrivers", "ParticlesDescription", "ParticlesOn", "ReleaseNotesText", "ReleaseNotesTitle",
        "ReleaseNotesTooltip", "SettingsParticles", "ShowLauncher", "StartupSettingError",
        "TouchBoard", "TouchChip", "TouchChipCst", "TouchChipFt", "TouchChipGt", "TouchChipXpt",
        "TouchCopied", "TouchCopy", "TouchCopyFailed", "TouchFinder", "TouchFinderFound", "TouchFinderHint",
        "TouchFinderListen", "TouchFinderListening", "TouchFinderNoLibraries", "TouchFinderNone",
        "TouchFinderNoPorts", "TouchFinderPickPort", "TouchFinderPortError", "TouchFinderPress",
        "TouchFinderRefresh", "TouchFinderShow", "TouchFinderShown", "TouchFinderStop", "TouchFinderTimeout",
        "TouchFinderUnsupported", "TouchHeight", "TouchIdeFailed", "TouchIdeInstalled", "TouchInstallIde",
        "TouchInstallingIde", "TouchInvertX", "TouchInvertY", "TouchLibraries", "TouchMenu", "TouchOpenIde",
        "TouchOrientation", "TouchOrientationHint", "TouchOutput", "TouchOutputLvgl", "TouchOutputSerial",
        "TouchOutputTft", "TouchPinNotUsed", "TouchPins", "TouchPresets", "TouchRotation", "TouchSave",
        "TouchSaved", "TouchSaveFailed", "TouchScreen", "TouchSeparateSpi", "TouchSubtitle", "TouchSwap",
        "TouchTitle", "TouchWidth",
        "ThemeSaveFailed", "ThemeShortcutIconFailed", "AccentColors", "StoreMenuSelect", "StoreMenuInstall",
        "StoreMenuUninstall", "StoreActionFailed", "StoreActionStartFailed", "UnknownError", "BatchSucceeded",
        "BatchFailed", "ValorantLaunchFailed", "LaunchFailed", "AlreadyRunning", "StartupFailed",
        "DialogYes", "DialogNo"
    ];

    internal static readonly string[] InfoKeys =
    [
        "AddAppDialogTitle", "AddYourself", "AddYourselfDesc", "Browsers", "BrowsersDesc", "DailyApps",
        "DailyAppsDesc", "DuplicateAppsMessage", "GameLaunchers", "GameLaunchersDesc", "HttpHint",
        "InstalledPwa", "InstalledPwaDesc", "LinkAddTitle", "LinkNamePlaceholder", "LinkRemoveHint",
        "LinkRemoveTitle", "NoLinksToRemove"
    ];

    internal static readonly string[] AutoKeys =
    [
        "AutoSteamSearching", "AutoSteamNoAppId", "AutoSteamFound", "AutoSteamAlreadyInstalled", "AutoSteamOpening",
        "AutoSteamLinkFailed", "AutoSteamQueued", "AutoSteamStarted", "AutoSteamNoDialog", "AutoSteamNoWindow",
        "AutoEpicSearching", "AutoEpicNoCatalog", "AutoEpicNotInLibrary", "AutoEpicFound", "AutoAlreadyInstalled",
        "AutoEpicLinkFailed", "AutoEpicStarted", "AutoEpicNoWindow", "AutoWaitingConfirm", "AutoConfirmed",
        "AutoNameEmpty", "AutoSearchingUi", "AutoLauncherMissing", "AutoOpenFailed", "AutoStartFailed",
        "AutoWindowMissing", "AutoSearchBoxMissing", "AutoSelected", "AutoNoResult", "AutoInstallButtonMissing",
        "AutoUninstallButtonMissing", "AutoConfirmFailed", "AutoInstallStarted", "AutoUninstallStarted",
        "RiotServicesMissing", "RiotStartFailed", "RiotGameNotOpened", "RiotServiceSlowRunning", "RiotServiceSlow",
        "RiotLockfileFormat", "RiotLockfileLocked", "RiotLockfileDenied", "RiotErrorCode", "RiotRejected"
    ];

    private static readonly object Gate = new();
    private static string? _loadedLanguage;
    private static Dictionary<string, string>? _loaded;

    public static bool TryGetValue(string languageCode, string key, out string value)
    {
        var table = Load(languageCode);
        if (table is not null && table.TryGetValue(key, out value!))
            return true;

        value = string.Empty;
        return false;
    }

    // Only one language is kept; switching languages replaces it.
    private static Dictionary<string, string>? Load(string languageCode)
    {
        lock (Gate)
        {
            if (string.Equals(_loadedLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
                return _loaded;

            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Fill(table, Keys, Main(languageCode));
            Fill(table, InfoKeys, Info(languageCode));
            Fill(table, AutoKeys, Auto(languageCode));
            _loadedLanguage = languageCode;
            _loaded = table.Count > 0 ? table : null;
            return _loaded;
        }
    }

    private static void Fill(Dictionary<string, string> table, string[] keys, string[]? values)
    {
        if (values is null || values.Length != keys.Length)
            return;

        for (var index = 0; index < keys.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(values[index]))
                table[keys[index]] = values[index];
        }
    }

    // Used by the translation check: the raw list for a language, or null.
    internal static string[]? MainFor(string languageCode) => Main(languageCode);
    internal static string[]? InfoFor(string languageCode) => Info(languageCode);
    internal static string[]? AutoFor(string languageCode) => Auto(languageCode);

    private static string[]? Main(string languageCode)
    {
        string[]? result = null;
        MainA(languageCode, ref result);
        MainB(languageCode, ref result);
        MainC(languageCode, ref result);
        MainD(languageCode, ref result);
        MainE(languageCode, ref result);
        MainF(languageCode, ref result);
        MainG(languageCode, ref result);
        MainH(languageCode, ref result);
        MainI(languageCode, ref result);
        MainJ(languageCode, ref result);
        MainK(languageCode, ref result);
        MainL(languageCode, ref result);
        return result;
    }

    private static string[]? Info(string languageCode)
    {
        string[]? result = null;
        InfoA(languageCode, ref result);
        return result;
    }

    static partial void MainA(string languageCode, ref string[]? result);
    static partial void MainB(string languageCode, ref string[]? result);
    static partial void MainC(string languageCode, ref string[]? result);
    static partial void MainD(string languageCode, ref string[]? result);
    static partial void MainE(string languageCode, ref string[]? result);
    static partial void MainF(string languageCode, ref string[]? result);
    static partial void MainG(string languageCode, ref string[]? result);
    static partial void MainH(string languageCode, ref string[]? result);
    static partial void MainI(string languageCode, ref string[]? result);
    static partial void MainJ(string languageCode, ref string[]? result);
    static partial void MainK(string languageCode, ref string[]? result);
    static partial void MainL(string languageCode, ref string[]? result);
    static partial void InfoA(string languageCode, ref string[]? result);

    private static string[]? Auto(string languageCode)
    {
        string[]? result = null;
        AutoA(languageCode, ref result);
        AutoB(languageCode, ref result);
        AutoC(languageCode, ref result);
        AutoD(languageCode, ref result);
        AutoE(languageCode, ref result);
        AutoF(languageCode, ref result);
        return result;
    }

    static partial void AutoA(string languageCode, ref string[]? result);
    static partial void AutoB(string languageCode, ref string[]? result);
    static partial void AutoC(string languageCode, ref string[]? result);
    static partial void AutoD(string languageCode, ref string[]? result);
    static partial void AutoE(string languageCode, ref string[]? result);
    static partial void AutoF(string languageCode, ref string[]? result);
}
