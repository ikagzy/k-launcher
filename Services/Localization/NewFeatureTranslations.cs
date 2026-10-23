namespace KLauncher.Services;

/// <summary>
/// Translations for the features added after the main catalogs: batch install,
/// game install by name, animation speed and the search hints. Each language
/// lists its values in <see cref="Keys"/> order; the languages live in the
/// NewFeatureTranslations.*.cs files, registered from the static constructor.
/// A language whose list has the wrong length is ignored (English is shown)
/// rather than showing texts under the wrong keys.
/// </summary>
internal static partial class NewFeatureTranslations
{
    private static readonly string[] Keys =
    [
        "StoreInstallByName", "StoreInstallAction", "StoreInstallActionEpic", "StoreGameNameHint",
        "StoreGameNamePlaceholder", "StoreGameNameRequired", "StoreBatchInstall", "StoreBatchInstallHint",
        "StoreBatchInstallPlaceholder", "StoreBatchInstallAction", "StoreBatchInstallEmpty", "StoreBatchInstallSummary",
        "SettingsMotion", "MotionFast", "MotionNormal", "MotionSlow", "MotionOff", "MotionDescription",
        "AppFileFilter", "SearchLanguages", "SearchHint", "SearchAppsHint",
        "NiniteButton", "NiniteTooltip", "NiniteTitle", "NiniteSubtitle", "NiniteSearchHint", "NiniteSearch",
        "NiniteSearching", "NiniteNoResults", "NiniteResultCount", "NiniteGames", "NiniteGamesHint", "NiniteClear",
        "NiniteInstall", "NiniteStop", "NiniteClose", "NiniteSelected", "NiniteNothingSelected", "NiniteWingetMissing",
        "NiniteInstalling", "NiniteInProgress", "NiniteHandingOff", "NiniteWaiting", "NiniteSkipped", "NiniteSkip",
        "NiniteSkipTooltip", "NiniteSkippedByUser", "NiniteTimedOut", "NiniteLog", "NiniteDone", "NiniteConfirmStop",
        "NiniteCatSearchResults", "NiniteCatBrowsers", "NiniteCatCommunication", "NiniteCatGaming", "NiniteCatMedia",
        "NiniteCatDevelopment", "NiniteCatTools", "NiniteCatOffice", "NiniteCatImaging", "NiniteCatStorage",
        "NiniteCatSecurity", "NiniteCatRuntimes", "NinitePresets", "NinitePresetGaming", "NinitePresetSoftware",
        "NinitePresetSchool", "NiniteBadgeInstalled", "NiniteBadgeUpdate", "NiniteBadgeUpToDate", "NiniteStatusLoading",
        "NiniteStatusSummary", "NiniteStatusFailed", "NiniteUpdateAll", "NiniteUpdateAllTooltip", "NiniteUninstall",
        "NiniteUninstallTooltip", "NiniteNothingToUninstall", "NiniteConfirmUninstall", "NiniteElevateOnce",
        "NiniteElevateOnceTooltip", "NiniteWaitingForApproval", "NiniteRetryFailed", "NiniteRetryFailedTooltip",
        "NiniteBackToList", "NiniteDoneAll", "NiniteUpdating", "NiniteUninstalling", "NiniteUpdateInProgress",
        "NiniteUninstallInProgress", "NiniteActionUpgrade", "NiniteActionUninstall",
        "WingetMsgInstalled", "WingetMsgUpdated", "WingetMsgRemoved", "WingetMsgInstalledRestart",
        "WingetMsgUpdatedRestart", "WingetMsgRemovedRestart", "WingetMsgUpToDate", "WingetMsgAlreadyInstalled",
        "WingetMsgNotInstalled", "WingetMsgNotFound", "WingetMsgHashMismatch", "WingetMsgCancelled",
        "WingetMsgInvalidId", "WingetMsgRefreshing", "WingetMsgTryingAlternate", "WingetMsgHelperClosed"
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Translations =
        new(StringComparer.OrdinalIgnoreCase);

    static NewFeatureTranslations()
    {
        RegisterPart1A();
        RegisterPart1B();
        RegisterPart1C();
        RegisterPart1D();
        RegisterPart2A();
        RegisterPart2B();
        RegisterPart2C();
        RegisterPart2D();
        RegisterPart3A();
        RegisterPart3B();
        RegisterPart3C();
        RegisterPart3D();
    }

    // Each part file implements one of these; a missing part is simply skipped.
    static partial void RegisterPart1A();
    static partial void RegisterPart1B();
    static partial void RegisterPart1C();
    static partial void RegisterPart1D();
    static partial void RegisterPart2A();
    static partial void RegisterPart2B();
    static partial void RegisterPart2C();
    static partial void RegisterPart2D();
    static partial void RegisterPart3A();
    static partial void RegisterPart3B();
    static partial void RegisterPart3C();
    static partial void RegisterPart3D();

    public static IReadOnlyList<string> AllKeys => Keys;

    public static bool TryGetValue(string languageCode, string key, out string value)
    {
        if (Translations.TryGetValue(languageCode, out var translations) &&
            translations.TryGetValue(key, out value!))
            return true;

        value = string.Empty;
        return false;
    }

    private static void Add(string languageCode, params string[] values)
    {
        if (values.Length != Keys.Length)
            return;

        var translations = new Dictionary<string, string>(Keys.Length, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Keys.Length; index++)
            translations[Keys[index]] = values[index];
        Translations[languageCode] = translations;
    }

    // Interface texts the older catalogs miss for some languages.
    private static readonly string[] InterfaceKeys =
    [
        "SettingsInterface", "InterfaceToggleHint", "InterfaceGrid", "InterfaceList", "InterfaceListHeader",
        "InterfaceListNoEntries", "HideContents", "ShowContents", "ContentsHidden", "SelectContents",
        "BatchSelect", "BatchSelectActive", "LaunchSelected", "CancelSelection", "SelectedCount", "InterfaceSwipeHint"
    ];

    private static void AddInterface(string languageCode, params string[] values)
    {
        if (values.Length != InterfaceKeys.Length)
            return;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < InterfaceKeys.Length; index++)
            map[InterfaceKeys[index]] = values[index];
        AddExtra(languageCode, map);
    }

    /// <summary>Extra keys for a language that the older catalogs do not cover.</summary>
    private static void AddExtra(string languageCode, IReadOnlyDictionary<string, string> values)
    {
        if (!Translations.TryGetValue(languageCode, out var translations))
            Translations[languageCode] = translations = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
            translations[key] = value;
    }
}
