using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace KLauncher.Services;

public sealed record LanguageOption(string Code, string DisplayName);

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly string SettingsPath =
        LauncherStorage.GetFilePath("language.txt");

    private static readonly IReadOnlyList<LanguageOption> SupportedLanguages =
    [
        new("tr", "Türkçe"),
        new("en", "English"),
        new("es", "Español"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("ru", "Русский"),
        new("ja", "日本語"),
        new("it", "Italiano"),
        new("pt", "Português"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("ar", "العربية"),
        new("zh", "简体中文"),
        new("ko", "한국어"),
        new("hi", "हिन्दी"),
        new("id", "Bahasa Indonesia"),
        new("ms", "Bahasa Melayu"),
        new("th", "ไทย"),
        new("vi", "Tiếng Việt"),
        new("sv", "Svenska"),
        new("no", "Norsk"),
        new("da", "Dansk"),
        new("fi", "Suomi"),
        new("el", "Ελληνικά"),
        new("cs", "Čeština"),
        new("sk", "Slovenčina"),
        new("hu", "Magyar"),
        new("ro", "Română"),
        new("bg", "Български"),
        new("uk", "Українська"),
        new("hr", "Hrvatski"),
        new("sr", "Српски"),
        new("he", "עברית"),
        new("bn", "বাংলা"),
        new("ur", "اردو"),
        new("fa", "فارسی"),
        new("sw", "Kiswahili"),
        new("et", "Eesti"),
        new("lv", "Latviešu"),
        new("lt", "Lietuvių"),
        .. ExtendedLanguageCatalog.Languages
    ];

    private static readonly string[] CommonTranslationKeys =
    [
        "AddApp",
        "SupportedAppsTooltip",
        "SettingsTooltip",
        "CardMenuHint",
        "FooterHint",
        "LanguageMenuTitle",
        "QuickMenu",
        "Launch",
        "RemoveApp",
        "Games",
        "QuickLinks",
        "AddQuickLink",
        "RemoveQuickLink",
        "OpenInstallFolder",
        "Rescan",
        "Close",
        "Remove",
        "Cancel",
        "Save"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["tr"] = new Dictionary<string, string>
            {
                ["AddApp"] = "UYGULAMA EKLE  +",
                ["AppFileFilter"] = "Uygulamalar ve kısayollar (*.exe;*.lnk;*.appref-ms)|*.exe;*.lnk;*.appref-ms|Tüm dosyalar (*.*)|*.*",
                ["SupportedAppsTooltip"] = "Desteklenen uygulamalar",
                ["SettingsTooltip"] = "Ayarlar ve dil",
                ["CardMenuHint"] = "SAĞ TIK  /  MENÜ",
                ["FooterHint"] = "SOL TIK  BAŞLAT    /    SAĞ TIK  MENÜ",
                ["LanguageMenuTitle"] = "DİL SEÇİMİ",
                ["QuickMenu"] = "HIZLI MENÜ",
                ["Launch"] = "BAŞLAT",
                ["RemoveApp"] = "UYGULAMAYI KALDIR",
                ["Games"] = "OYUNLAR",
                ["QuickLinks"] = "HIZLI BAĞLANTILAR",
                ["AddQuickLink"] = "HIZLI BAĞLANTI EKLE",
                ["RemoveQuickLink"] = "HIZLI BAĞLANTIYI KALDIR",
                ["OpenInstallFolder"] = "KURULUM KLASÖRÜNÜ AÇ",
                ["Rescan"] = "YENİDEN TARA",
                ["LaunchAtStartup"] = "Windows ile başlat",
                ["StartupSettingError"] = "Başlangıç ayarı kaydedilemedi.",
                ["ShowLauncher"] = "K-Launcher'ı aç",
                ["ExitLauncher"] = "K-Launcher'dan çık",
                ["InfoTitle"] = "K-LAUNCHER 4.0 // DESTEKLENEN UYGULAMALAR",
                ["AutoDetected"] = "OTOMATİK ALGILANANLAR",
                ["GameLaunchers"] = "OYUN BAŞLATICILARI",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher ve Xbox. Kurulu oyunlar ilgili başlatıcının menüsünde listelenir.",
                ["Browsers"] = "TARAYICILAR",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi ve Opera GX.",
                ["DailyApps"] = "İLETİŞİM VE GÜNLÜK UYGULAMALAR",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code ve Medal.",
                ["InstalledPwa"] = "KURULU UYGULAMA / PWA",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger ve Google Maps; Windows uygulaması veya PWA olarak kuruluysa algılanır.",
                ["AddYourself"] = "KENDİN EKLE",
                ["AddYourselfDesc"] = "Üstteki UYGULAMA EKLE düğmesiyle .exe, .lnk veya .appref-ms dosyası ekleyebilirsin. Eklediğin karta sağ tıklayıp K-Launcher'dan kaldırabilirsin. Sadece kurulu ve başlatılabilir olanlar listede görünür.",
                ["Ok"] = "TAMAM",
                ["AddAppDialogTitle"] = "K-Launcher'a uygulama ekle",
                ["DuplicateAppsMessage"] = "Seçilen uygulamalar zaten listede veya desteklenen bir uygulama/kısayol değil.",
                ["LinkRemoveTitle"] = "HIZLI BAĞLANTIYI KALDIR",
                ["LinkRemoveHint"] = "Kaldırmak istediğin bağlantının yanındaki düğmeye bas.",
                ["Close"] = "KAPAT",
                ["NoLinksToRemove"] = "Kaldırılacak hızlı bağlantı kalmadı.",
                ["Remove"] = "KALDIR",
                ["LinkAddTitle"] = "HIZLI BAĞLANTI EKLE",
                ["LinkNamePlaceholder"] = "Bağlantı adı",
                ["Cancel"] = "İPTAL",
                ["Save"] = "KAYDET",
                ["HttpHint"] = "HTTP ve HTTPS bağlantıları desteklenir."
            },
            ["en"] = new Dictionary<string, string>
            {
                ["AddApp"] = "ADD APP  +",
                ["AppFileFilter"] = "Applications and shortcuts (*.exe;*.lnk;*.appref-ms)|*.exe;*.lnk;*.appref-ms|All files (*.*)|*.*",
                ["SupportedAppsTooltip"] = "Supported applications",
                ["SettingsTooltip"] = "Settings and language",
                ["CardMenuHint"] = "RIGHT CLICK  /  MENU",
                ["FooterHint"] = "LEFT CLICK  LAUNCH    /    RIGHT CLICK  MENU",
                ["LanguageMenuTitle"] = "LANGUAGE",
                ["QuickMenu"] = "QUICK MENU",
                ["Launch"] = "LAUNCH",
                ["RemoveApp"] = "REMOVE APP",
                ["Games"] = "GAMES",
                ["QuickLinks"] = "QUICK LINKS",
                ["AddQuickLink"] = "ADD QUICK LINK",
                ["RemoveQuickLink"] = "REMOVE QUICK LINK",
                ["OpenInstallFolder"] = "OPEN INSTALL FOLDER",
                ["Rescan"] = "RESCAN",
                ["LaunchAtStartup"] = "Launch at startup",
                ["StartupSettingError"] = "The startup setting could not be saved.",
                ["ShowLauncher"] = "Open K-Launcher",
                ["ExitLauncher"] = "Exit K-Launcher",
                ["InfoTitle"] = "K-LAUNCHER 4.0 // SUPPORTED APPLICATIONS",
                ["AutoDetected"] = "AUTOMATICALLY DETECTED",
                ["GameLaunchers"] = "GAME LAUNCHERS",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher, and Xbox. Installed games appear in their launcher's menu.",
                ["Browsers"] = "BROWSERS",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi, and Opera GX.",
                ["DailyApps"] = "COMMUNICATION AND EVERYDAY APPS",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code, and Medal.",
                ["InstalledPwa"] = "INSTALLED APPS / PWAs",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger, and Google Maps are detected when installed as Windows apps or PWAs.",
                ["AddYourself"] = "ADD YOUR OWN",
                ["AddYourselfDesc"] = "Use ADD APP above to add an .exe, .lnk, or .appref-ms file. Right-click its card to remove it from K-Launcher. Only installed and launchable apps are shown.",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "Add an app to K-Launcher",
                ["DuplicateAppsMessage"] = "The selected apps are already listed or are not a supported app or shortcut.",
                ["LinkRemoveTitle"] = "REMOVE QUICK LINK",
                ["LinkRemoveHint"] = "Press the button next to the link you want to remove.",
                ["Close"] = "CLOSE",
                ["NoLinksToRemove"] = "There are no quick links left to remove.",
                ["Remove"] = "REMOVE",
                ["LinkAddTitle"] = "ADD QUICK LINK",
                ["LinkNamePlaceholder"] = "Link name",
                ["Cancel"] = "CANCEL",
                ["Save"] = "SAVE",
                ["HttpHint"] = "HTTP and HTTPS links are supported."
            },
            ["es"] = new Dictionary<string, string>
            {
                ["AddApp"] = "AÑADIR APP  +",
                ["SupportedAppsTooltip"] = "Aplicaciones compatibles",
                ["SettingsTooltip"] = "Ajustes e idioma",
                ["CardMenuHint"] = "CLIC DERECHO  /  MENÚ",
                ["FooterHint"] = "CLIC IZQUIERDO  ABRIR    /    CLIC DERECHO  MENÚ",
                ["LanguageMenuTitle"] = "IDIOMA",
                ["QuickMenu"] = "MENÚ RÁPIDO",
                ["Launch"] = "ABRIR",
                ["RemoveApp"] = "ELIMINAR APP",
                ["Games"] = "JUEGOS",
                ["QuickLinks"] = "ACCESOS DIRECTOS",
                ["AddQuickLink"] = "AÑADIR ACCESO DIRECTO",
                ["RemoveQuickLink"] = "ELIMINAR ACCESO DIRECTO",
                ["OpenInstallFolder"] = "ABRIR CARPETA DE INSTALACIÓN",
                ["Rescan"] = "VOLVER A BUSCAR",
                ["LaunchAtStartup"] = "Iniciar con Windows",
                ["StartupSettingError"] = "No se pudo guardar el inicio automático.",
                ["ShowLauncher"] = "Abrir K-Launcher",
                ["ExitLauncher"] = "Salir de K-Launcher",
                ["InfoTitle"] = "K-LAUNCHER 4.0 // APLICACIONES COMPATIBLES",
                ["AutoDetected"] = "DETECCIÓN AUTOMÁTICA",
                ["GameLaunchers"] = "LANZADORES DE JUEGOS",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher y Xbox. Los juegos instalados aparecen en el menú de su lanzador.",
                ["Browsers"] = "NAVEGADORES",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi y Opera GX.",
                ["DailyApps"] = "COMUNICACIÓN Y APPS DIARIAS",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code y Medal.",
                ["InstalledPwa"] = "APPS INSTALADAS / PWA",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger y Google Maps se detectan si están instaladas como aplicaciones de Windows o PWA.",
                ["AddYourself"] = "AÑADE UNA APP",
                ["AddYourselfDesc"] = "Usa AÑADIR APP para agregar archivos .exe, .lnk o .appref-ms. Haz clic derecho en su tarjeta para quitarla de K-Launcher. Solo aparecen las aplicaciones instaladas y ejecutables.",
                ["Ok"] = "ACEPTAR",
                ["AddAppDialogTitle"] = "Añadir una app a K-Launcher",
                ["DuplicateAppsMessage"] = "Las aplicaciones ya están en la lista o no son aplicaciones o accesos directos compatibles.",
                ["LinkRemoveTitle"] = "ELIMINAR ACCESO DIRECTO",
                ["LinkRemoveHint"] = "Pulsa el botón junto al acceso que quieras eliminar.",
                ["Close"] = "CERRAR",
                ["NoLinksToRemove"] = "No quedan accesos directos para eliminar.",
                ["Remove"] = "ELIMINAR",
                ["LinkAddTitle"] = "AÑADIR ACCESO DIRECTO",
                ["LinkNamePlaceholder"] = "Nombre del enlace",
                ["Cancel"] = "CANCELAR",
                ["Save"] = "GUARDAR",
                ["HttpHint"] = "Se admiten enlaces HTTP y HTTPS."
            },
            ["de"] = new Dictionary<string, string>
            {
                ["AddApp"] = "APP HINZUFÜGEN  +",
                ["SupportedAppsTooltip"] = "Unterstützte Apps",
                ["SettingsTooltip"] = "Einstellungen und Sprache",
                ["CardMenuHint"] = "RECHTSKLICK  /  MENÜ",
                ["FooterHint"] = "LINKSKLICK  STARTEN    /    RECHTSKLICK  MENÜ",
                ["LanguageMenuTitle"] = "SPRACHE",
                ["QuickMenu"] = "KURZMENÜ",
                ["Launch"] = "STARTEN",
                ["RemoveApp"] = "APP ENTFERNEN",
                ["Games"] = "SPIELE",
                ["QuickLinks"] = "SCHNELLZUGRIFFE",
                ["AddQuickLink"] = "SCHNELLZUGRIFF HINZUFÜGEN",
                ["RemoveQuickLink"] = "SCHNELLZUGRIFF ENTFERNEN",
                ["OpenInstallFolder"] = "INSTALLATIONSORDNER ÖFFNEN",
                ["Rescan"] = "ERNEUT SUCHEN",
                ["LaunchAtStartup"] = "Mit Windows starten",
                ["StartupSettingError"] = "Die Autostart-Einstellung konnte nicht gespeichert werden.",
                ["ShowLauncher"] = "K-Launcher öffnen",
                ["ExitLauncher"] = "K-Launcher beenden",
                ["InfoTitle"] = "K-LAUNCHER 4.0 // UNTERSTÜTZTE APPS",
                ["AutoDetected"] = "AUTOMATISCH ERKANNT",
                ["GameLaunchers"] = "SPIELE-LAUNCHER",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher und Xbox. Installierte Spiele erscheinen im Menü ihres Launchers.",
                ["Browsers"] = "BROWSER",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi und Opera GX.",
                ["DailyApps"] = "KOMMUNIKATION UND ALLTÄGLICHE APPS",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code und Medal.",
                ["InstalledPwa"] = "INSTALLIERTE APPS / PWAs",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger und Google Maps werden erkannt, wenn sie als Windows-App oder PWA installiert sind.",
                ["AddYourself"] = "SELBST HINZUFÜGEN",
                ["AddYourselfDesc"] = "Über APP HINZUFÜGEN kannst du .exe-, .lnk- oder .appref-ms-Dateien hinzufügen. Per Rechtsklick auf die Karte entfernst du sie aus K-Launcher. Angezeigt werden nur installierte und startbare Apps.",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "App zu K-Launcher hinzufügen",
                ["DuplicateAppsMessage"] = "Die Apps stehen bereits in der Liste oder sind keine unterstützte App bzw. Verknüpfung.",
                ["LinkRemoveTitle"] = "SCHNELLZUGRIFF ENTFERNEN",
                ["LinkRemoveHint"] = "Klicke auf die Schaltfläche neben dem Link, den du entfernen möchtest.",
                ["Close"] = "SCHLIESSEN",
                ["NoLinksToRemove"] = "Es sind keine Schnellzugriffe mehr vorhanden.",
                ["Remove"] = "ENTFERNEN",
                ["LinkAddTitle"] = "SCHNELLZUGRIFF HINZUFÜGEN",
                ["LinkNamePlaceholder"] = "Linkname",
                ["Cancel"] = "ABBRECHEN",
                ["Save"] = "SPEICHERN",
                ["HttpHint"] = "HTTP- und HTTPS-Links werden unterstützt."
            },
            ["fr"] = new Dictionary<string, string>
            {
                ["AddApp"] = "AJOUTER UNE APP  +",
                ["SupportedAppsTooltip"] = "Applications prises en charge",
                ["SettingsTooltip"] = "Paramètres et langue",
                ["CardMenuHint"] = "CLIC DROIT  /  MENU",
                ["FooterHint"] = "CLIC GAUCHE  LANCER    /    CLIC DROIT  MENU",
                ["LanguageMenuTitle"] = "LANGUE",
                ["QuickMenu"] = "MENU RAPIDE",
                ["Launch"] = "LANCER",
                ["RemoveApp"] = "SUPPRIMER L’APP",
                ["Games"] = "JEUX",
                ["QuickLinks"] = "LIENS RAPIDES",
                ["AddQuickLink"] = "AJOUTER UN LIEN RAPIDE",
                ["RemoveQuickLink"] = "SUPPRIMER UN LIEN RAPIDE",
                ["OpenInstallFolder"] = "OUVRIR LE DOSSIER D’INSTALLATION",
                ["Rescan"] = "ANALYSER À NOUVEAU",
                ["LaunchAtStartup"] = "Lancer au démarrage de Windows",
                ["StartupSettingError"] = "Le démarrage automatique n’a pas pu être enregistré.",
                ["ShowLauncher"] = "Ouvrir K-Launcher",
                ["ExitLauncher"] = "Quitter K-Launcher",
                ["InfoTitle"] = "K-LAUNCHER 4.0 // APPLICATIONS PRISES EN CHARGE",
                ["AutoDetected"] = "DÉTECTION AUTOMATIQUE",
                ["GameLaunchers"] = "LANCEURS DE JEUX",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher et Xbox. Les jeux installés apparaissent dans le menu de leur lanceur.",
                ["Browsers"] = "NAVIGATEURS",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi et Opera GX.",
                ["DailyApps"] = "COMMUNICATION ET APPS DU QUOTIDIEN",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code et Medal.",
                ["InstalledPwa"] = "APPS INSTALLÉES / PWA",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger et Google Maps sont détectés s’ils sont installés comme applications Windows ou PWA.",
                ["AddYourself"] = "AJOUTER UNE APP",
                ["AddYourselfDesc"] = "Utilisez AJOUTER UNE APP pour ajouter un fichier .exe, .lnk ou .appref-ms. Cliquez avec le bouton droit sur sa carte pour la retirer de K-Launcher. Seules les apps installées et lançables sont affichées.",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "Ajouter une app à K-Launcher",
                ["DuplicateAppsMessage"] = "Les apps sélectionnées sont déjà listées ou ne sont pas des apps ou raccourcis pris en charge.",
                ["LinkRemoveTitle"] = "SUPPRIMER UN LIEN RAPIDE",
                ["LinkRemoveHint"] = "Appuyez sur le bouton à côté du lien à supprimer.",
                ["Close"] = "FERMER",
                ["NoLinksToRemove"] = "Il ne reste aucun lien rapide à supprimer.",
                ["Remove"] = "SUPPRIMER",
                ["LinkAddTitle"] = "AJOUTER UN LIEN RAPIDE",
                ["LinkNamePlaceholder"] = "Nom du lien",
                ["Cancel"] = "ANNULER",
                ["Save"] = "ENREGISTRER",
                ["HttpHint"] = "Les liens HTTP et HTTPS sont pris en charge."
            },
            ["ru"] = new Dictionary<string, string>
            {
                ["AddApp"] = "ДОБАВИТЬ ПРИЛОЖЕНИЕ  +",
                ["SupportedAppsTooltip"] = "Поддерживаемые приложения",
                ["SettingsTooltip"] = "Настройки и язык",
                ["CardMenuHint"] = "ПРАВЫЙ ЩЕЛЧОК  /  МЕНЮ",
                ["FooterHint"] = "ЛЕВЫЙ ЩЕЛЧОК  ЗАПУСК    /    ПРАВЫЙ ЩЕЛЧОК  МЕНЮ",
                ["LanguageMenuTitle"] = "ЯЗЫК",
                ["QuickMenu"] = "БЫСТРОЕ МЕНЮ",
                ["Launch"] = "ЗАПУСТИТЬ",
                ["RemoveApp"] = "УДАЛИТЬ ПРИЛОЖЕНИЕ",
                ["Games"] = "ИГРЫ",
                ["QuickLinks"] = "БЫСТРЫЕ ССЫЛКИ",
                ["AddQuickLink"] = "ДОБАВИТЬ БЫСТРУЮ ССЫЛКУ",
                ["RemoveQuickLink"] = "УДАЛИТЬ БЫСТРУЮ ССЫЛКУ",
                ["OpenInstallFolder"] = "ОТКРЫТЬ ПАПКУ УСТАНОВКИ",
                ["Rescan"] = "СКАНИРОВАТЬ СНОВА",
                ["LaunchAtStartup"] = "Запускать вместе с Windows",
                ["StartupSettingError"] = "Не удалось сохранить настройку автозапуска.",
                ["ShowLauncher"] = "Открыть K-Launcher",
                ["ExitLauncher"] = "Закрыть K-Launcher",
                ["InfoTitle"] = "K-LAUNCHER 4.0 // ПОДДЕРЖИВАЕМЫЕ ПРИЛОЖЕНИЯ",
                ["AutoDetected"] = "ОБНАРУЖИВАЮТСЯ АВТОМАТИЧЕСКИ",
                ["GameLaunchers"] = "ИГРОВЫЕ ЛАУНЧЕРЫ",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher и Xbox. Установленные игры отображаются в меню своего лаунчера.",
                ["Browsers"] = "БРАУЗЕРЫ",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi и Opera GX.",
                ["DailyApps"] = "ОБЩЕНИЕ И ПОВСЕДНЕВНЫЕ ПРИЛОЖЕНИЯ",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code и Medal.",
                ["InstalledPwa"] = "УСТАНОВЛЕННЫЕ ПРИЛОЖЕНИЯ / PWA",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger и Google Maps обнаруживаются, если установлены как приложения Windows или PWA.",
                ["AddYourself"] = "ДОБАВИТЬ САМОСТОЯТЕЛЬНО",
                ["AddYourselfDesc"] = "Нажмите ДОБАВИТЬ ПРИЛОЖЕНИЕ, чтобы добавить файл .exe, .lnk или .appref-ms. Щёлкните правой кнопкой по карточке, чтобы удалить её из K-Launcher. Показываются только установленные приложения, которые можно запустить.",
                ["Ok"] = "ОК",
                ["AddAppDialogTitle"] = "Добавить приложение в K-Launcher",
                ["DuplicateAppsMessage"] = "Выбранные приложения уже есть в списке или это неподдерживаемые приложения либо ярлыки.",
                ["LinkRemoveTitle"] = "УДАЛИТЬ БЫСТРУЮ ССЫЛКУ",
                ["LinkRemoveHint"] = "Нажмите кнопку рядом со ссылкой, которую хотите удалить.",
                ["Close"] = "ЗАКРЫТЬ",
                ["NoLinksToRemove"] = "Быстрых ссылок для удаления не осталось.",
                ["Remove"] = "УДАЛИТЬ",
                ["LinkAddTitle"] = "ДОБАВИТЬ БЫСТРУЮ ССЫЛКУ",
                ["LinkNamePlaceholder"] = "Название ссылки",
                ["Cancel"] = "ОТМЕНА",
                ["Save"] = "СОХРАНИТЬ",
                ["HttpHint"] = "Поддерживаются ссылки HTTP и HTTPS."
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["AddApp"] = "アプリを追加  +",
                ["SupportedAppsTooltip"] = "対応アプリ",
                ["SettingsTooltip"] = "設定と言語",
                ["CardMenuHint"] = "右クリック  /  メニュー",
                ["FooterHint"] = "左クリック  起動    /    右クリック  メニュー",
                ["LanguageMenuTitle"] = "言語",
                ["QuickMenu"] = "クイックメニュー",
                ["Launch"] = "起動",
                ["RemoveApp"] = "アプリを削除",
                ["Games"] = "ゲーム",
                ["QuickLinks"] = "クイックリンク",
                ["AddQuickLink"] = "クイックリンクを追加",
                ["RemoveQuickLink"] = "クイックリンクを削除",
                ["OpenInstallFolder"] = "インストールフォルダーを開く",
                ["Rescan"] = "再スキャン",
                ["LaunchAtStartup"] = "Windowsの起動時に実行",
                ["StartupSettingError"] = "スタートアップ設定を保存できませんでした。",
                ["ShowLauncher"] = "K-Launcherを開く",
                ["ExitLauncher"] = "K-Launcherを終了",
                ["InfoTitle"] = "K-LAUNCHER 4.0 // 対応アプリ",
                ["AutoDetected"] = "自動検出",
                ["GameLaunchers"] = "ゲームランチャー",
                ["GameLaunchersDesc"] = "Steam、Epic Games、EA app、Ubisoft Connect、Battle.net、GOG Galaxy、Riot Client、Rockstar Games Launcher、Amazon Games、itch.io、HoYoPlay、Minecraft Launcher、Xbox。インストール済みのゲームはランチャーのメニューに表示されます。",
                ["Browsers"] = "ブラウザー",
                ["BrowsersDesc"] = "Chrome、Microsoft Edge、Firefox、Brave、Vivaldi、Opera GX。",
                ["DailyApps"] = "コミュニケーションと日常アプリ",
                ["DailyAppsDesc"] = "Discord、WhatsApp、Telegram、Signal、Microsoft Teams、Zoom、Slack、ChatGPT、Spotify、VLC、Google Drive、OBS Studio、VS Code、Medal。",
                ["InstalledPwa"] = "インストール済みアプリ / PWA",
                ["InstalledPwaDesc"] = "YouTube、Instagram、TikTok、Facebook、Messenger、Google Maps は、Windows アプリまたは PWA としてインストールされている場合に検出されます。",
                ["AddYourself"] = "手動で追加",
                ["AddYourselfDesc"] = "上部の「アプリを追加」から .exe、.lnk、.appref-ms ファイルを追加できます。カードを右クリックすると K-Launcher から削除できます。インストール済みで起動可能なアプリのみ表示されます。",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "K-Launcher にアプリを追加",
                ["DuplicateAppsMessage"] = "選択したアプリはすでに一覧にあるか、対応していないアプリまたはショートカットです。",
                ["LinkRemoveTitle"] = "クイックリンクを削除",
                ["LinkRemoveHint"] = "削除するリンクの横にあるボタンを押してください。",
                ["Close"] = "閉じる",
                ["NoLinksToRemove"] = "削除できるクイックリンクはありません。",
                ["Remove"] = "削除",
                ["LinkAddTitle"] = "クイックリンクを追加",
                ["LinkNamePlaceholder"] = "リンク名",
                ["Cancel"] = "キャンセル",
                ["Save"] = "保存",
                ["HttpHint"] = "HTTP と HTTPS のリンクに対応しています。"
            },
            ["it"] = new Dictionary<string, string>
            {
                ["AddApp"] = "AGGIUNGI APP  +",
                ["SupportedAppsTooltip"] = "Applicazioni supportate",
                ["SettingsTooltip"] = "Impostazioni e lingua",
                ["CardMenuHint"] = "CLIC DESTRO  /  MENU",
                ["FooterHint"] = "CLIC SINISTRO  AVVIA    /    CLIC DESTRO  MENU",
                ["LanguageMenuTitle"] = "LINGUA",
                ["QuickMenu"] = "MENU RAPIDO",
                ["Launch"] = "AVVIA",
                ["RemoveApp"] = "RIMUOVI APP",
                ["Games"] = "GIOCHI",
                ["QuickLinks"] = "COLLEGAMENTI RAPIDI",
                ["AddQuickLink"] = "AGGIUNGI COLLEGAMENTO RAPIDO",
                ["RemoveQuickLink"] = "RIMUOVI COLLEGAMENTO RAPIDO",
                ["OpenInstallFolder"] = "APRI CARTELLA DI INSTALLAZIONE",
                ["Rescan"] = "SCANSIONA DI NUOVO",
                ["InfoTitle"] = "K-LAUNCHER 4.0  //  APPLICAZIONI SUPPORTATE",
                ["AutoDetected"] = "RILEVATE AUTOMATICAMENTE",
                ["GameLaunchers"] = "PIATTAFORME DI GIOCO",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher e Xbox. I giochi installati appaiono nel menu del rispettivo launcher.",
                ["Browsers"] = "BROWSER",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi e Opera GX.",
                ["DailyApps"] = "COMUNICAZIONE E APP QUOTIDIANE",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code e Medal.",
                ["InstalledPwa"] = "APP / PWA INSTALLATE",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger e Google Maps vengono rilevati se installati come app Windows o PWA.",
                ["AddYourself"] = "AGGIUNGI TU",
                ["AddYourselfDesc"] = "Usa AGGIUNGI APP in alto per aggiungere file .exe, .lnk o .appref-ms. Fai clic destro sulla scheda per rimuoverla da K-Launcher. Vengono mostrate solo le app installate e avviabili.",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "Aggiungi un'app a K-Launcher",
                ["DuplicateAppsMessage"] = "Le app selezionate sono già nell'elenco oppure non sono app o collegamenti supportati.",
                ["LinkRemoveTitle"] = "RIMUOVI COLLEGAMENTO RAPIDO",
                ["LinkRemoveHint"] = "Premi il pulsante accanto al collegamento da rimuovere.",
                ["Close"] = "CHIUDI",
                ["NoLinksToRemove"] = "Non ci sono altri collegamenti rapidi da rimuovere.",
                ["Remove"] = "RIMUOVI",
                ["LinkAddTitle"] = "AGGIUNGI COLLEGAMENTO RAPIDO",
                ["LinkNamePlaceholder"] = "Nome del collegamento",
                ["Cancel"] = "ANNULLA",
                ["Save"] = "SALVA",
                ["HttpHint"] = "Sono supportati i collegamenti HTTP e HTTPS."
            },
            ["pt"] = new Dictionary<string, string>
            {
                ["AddApp"] = "ADICIONAR APP  +",
                ["SupportedAppsTooltip"] = "Aplicações compatíveis",
                ["SettingsTooltip"] = "Definições e idioma",
                ["CardMenuHint"] = "CLIQUE DIREITO  /  MENU",
                ["FooterHint"] = "CLIQUE ESQUERDO  INICIAR    /    CLIQUE DIREITO  MENU",
                ["LanguageMenuTitle"] = "IDIOMA",
                ["QuickMenu"] = "MENU RÁPIDO",
                ["Launch"] = "INICIAR",
                ["RemoveApp"] = "REMOVER APP",
                ["Games"] = "JOGOS",
                ["QuickLinks"] = "LIGAÇÕES RÁPIDAS",
                ["AddQuickLink"] = "ADICIONAR LIGAÇÃO RÁPIDA",
                ["RemoveQuickLink"] = "REMOVER LIGAÇÃO RÁPIDA",
                ["OpenInstallFolder"] = "ABRIR PASTA DE INSTALAÇÃO",
                ["Rescan"] = "ANALISAR NOVAMENTE",
                ["InfoTitle"] = "K-LAUNCHER 4.0  //  APLICAÇÕES COMPATÍVEIS",
                ["AutoDetected"] = "DETETADAS AUTOMATICAMENTE",
                ["GameLaunchers"] = "PLATAFORMAS DE JOGOS",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher e Xbox. Os jogos instalados aparecem no menu do respetivo launcher.",
                ["Browsers"] = "NAVEGADORES",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi e Opera GX.",
                ["DailyApps"] = "COMUNICAÇÃO E APPS DO DIA A DIA",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code e Medal.",
                ["InstalledPwa"] = "APPS / PWAS INSTALADAS",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger e Google Maps são detetados quando instalados como aplicações Windows ou PWAs.",
                ["AddYourself"] = "ADICIONAR MANUALMENTE",
                ["AddYourselfDesc"] = "Use ADICIONAR APP para adicionar ficheiros .exe, .lnk ou .appref-ms. Clique com o botão direito no cartão para o remover do K-Launcher. Só são apresentadas apps instaladas e executáveis.",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "Adicionar uma app ao K-Launcher",
                ["DuplicateAppsMessage"] = "As apps selecionadas já estão na lista ou não são aplicações ou atalhos compatíveis.",
                ["LinkRemoveTitle"] = "REMOVER LIGAÇÃO RÁPIDA",
                ["LinkRemoveHint"] = "Prima o botão junto à ligação que pretende remover.",
                ["Close"] = "FECHAR",
                ["NoLinksToRemove"] = "Não existem mais ligações rápidas para remover.",
                ["Remove"] = "REMOVER",
                ["LinkAddTitle"] = "ADICIONAR LIGAÇÃO RÁPIDA",
                ["LinkNamePlaceholder"] = "Nome da ligação",
                ["Cancel"] = "CANCELAR",
                ["Save"] = "GUARDAR",
                ["HttpHint"] = "São suportadas ligações HTTP e HTTPS."
            },
            ["nl"] = new Dictionary<string, string>
            {
                ["AddApp"] = "APP TOEVOEGEN  +",
                ["SupportedAppsTooltip"] = "Ondersteunde toepassingen",
                ["SettingsTooltip"] = "Instellingen en taal",
                ["CardMenuHint"] = "RECHTERMUISKNOP  /  MENU",
                ["FooterHint"] = "LINKERKLIK  STARTEN    /    RECHTERKLIK  MENU",
                ["LanguageMenuTitle"] = "TAAL",
                ["QuickMenu"] = "SNELMENU",
                ["Launch"] = "STARTEN",
                ["RemoveApp"] = "APP VERWIJDEREN",
                ["Games"] = "GAMES",
                ["QuickLinks"] = "SNELKOPPELINGEN",
                ["AddQuickLink"] = "SNELKOPPELING TOEVOEGEN",
                ["RemoveQuickLink"] = "SNELKOPPELING VERWIJDEREN",
                ["OpenInstallFolder"] = "INSTALLATIEMAP OPENEN",
                ["Rescan"] = "OPNIEUW SCANNEN",
                ["InfoTitle"] = "K-LAUNCHER 4.0  //  ONDERSTEUNDE TOEPASSINGEN",
                ["AutoDetected"] = "AUTOMATISCH HERKEND",
                ["GameLaunchers"] = "GAMEPLATFORMS",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher en Xbox. Geïnstalleerde games staan in het menu van hun launcher.",
                ["Browsers"] = "BROWSERS",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi en Opera GX.",
                ["DailyApps"] = "COMMUNICATIE EN DAGELIJKSE APPS",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code en Medal.",
                ["InstalledPwa"] = "GEÏNSTALLEERDE APPS / PWA'S",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger en Google Maps worden herkend wanneer ze als Windows-app of PWA zijn geïnstalleerd.",
                ["AddYourself"] = "ZELF TOEVOEGEN",
                ["AddYourselfDesc"] = "Gebruik APP TOEVOEGEN om een .exe-, .lnk- of .appref-ms-bestand toe te voegen. Klik met rechts op de kaart om deze uit K-Launcher te verwijderen. Alleen geïnstalleerde en startbare apps worden getoond.",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "Een app toevoegen aan K-Launcher",
                ["DuplicateAppsMessage"] = "De geselecteerde apps staan al in de lijst of zijn geen ondersteunde app of snelkoppeling.",
                ["LinkRemoveTitle"] = "SNELKOPPELING VERWIJDEREN",
                ["LinkRemoveHint"] = "Klik op de knop naast de snelkoppeling die je wilt verwijderen.",
                ["Close"] = "SLUITEN",
                ["NoLinksToRemove"] = "Er zijn geen snelkoppelingen meer om te verwijderen.",
                ["Remove"] = "VERWIJDEREN",
                ["LinkAddTitle"] = "SNELKOPPELING TOEVOEGEN",
                ["LinkNamePlaceholder"] = "Naam van snelkoppeling",
                ["Cancel"] = "ANNULEREN",
                ["Save"] = "OPSLAAN",
                ["HttpHint"] = "HTTP- en HTTPS-koppelingen worden ondersteund."
            },
            ["pl"] = new Dictionary<string, string>
            {
                ["AddApp"] = "DODAJ APLIKACJĘ  +",
                ["SupportedAppsTooltip"] = "Obsługiwane aplikacje",
                ["SettingsTooltip"] = "Ustawienia i język",
                ["CardMenuHint"] = "PRAWY PRZYCISK  /  MENU",
                ["FooterHint"] = "LEWY KLIK  URUCHOM    /    PRAWY KLIK  MENU",
                ["LanguageMenuTitle"] = "JĘZYK",
                ["QuickMenu"] = "SZYBKIE MENU",
                ["Launch"] = "URUCHOM",
                ["RemoveApp"] = "USUŃ APLIKACJĘ",
                ["Games"] = "GRY",
                ["QuickLinks"] = "SZYBKIE ŁĄCZA",
                ["AddQuickLink"] = "DODAJ SZYBKIE ŁĄCZE",
                ["RemoveQuickLink"] = "USUŃ SZYBKIE ŁĄCZE",
                ["OpenInstallFolder"] = "OTWÓRZ FOLDER INSTALACYJNY",
                ["Rescan"] = "SKANUJ PONOWNIE",
                ["InfoTitle"] = "K-LAUNCHER 4.0  //  OBSŁUGIWANE APLIKACJE",
                ["AutoDetected"] = "WYKRYTE AUTOMATYCZNIE",
                ["GameLaunchers"] = "LAUNCHERY GIER",
                ["GameLaunchersDesc"] = "Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher i Xbox. Zainstalowane gry są widoczne w menu odpowiedniego launchera.",
                ["Browsers"] = "PRZEGLĄDARKI",
                ["BrowsersDesc"] = "Chrome, Microsoft Edge, Firefox, Brave, Vivaldi i Opera GX.",
                ["DailyApps"] = "KOMUNIKACJA I CODZIENNE APLIKACJE",
                ["DailyAppsDesc"] = "Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code i Medal.",
                ["InstalledPwa"] = "ZAINSTALOWANE APLIKACJE / PWA",
                ["InstalledPwaDesc"] = "YouTube, Instagram, TikTok, Facebook, Messenger i Google Maps są wykrywane, gdy są zainstalowane jako aplikacje Windows lub PWA.",
                ["AddYourself"] = "DODAJ SAMODZIELNIE",
                ["AddYourselfDesc"] = "Użyj przycisku DODAJ APLIKACJĘ, aby dodać plik .exe, .lnk lub .appref-ms. Kliknij kartę prawym przyciskiem, aby usunąć ją z K-Launcher. Wyświetlane są tylko zainstalowane i możliwe do uruchomienia aplikacje.",
                ["Ok"] = "OK",
                ["AddAppDialogTitle"] = "Dodaj aplikację do K-Launcher",
                ["DuplicateAppsMessage"] = "Wybrane aplikacje są już na liście albo nie są obsługiwaną aplikacją lub skrótem.",
                ["LinkRemoveTitle"] = "USUŃ SZYBKIE ŁĄCZE",
                ["LinkRemoveHint"] = "Naciśnij przycisk obok łącza, które chcesz usunąć.",
                ["Close"] = "ZAMKNIJ",
                ["NoLinksToRemove"] = "Nie ma już szybkich łączy do usunięcia.",
                ["Remove"] = "USUŃ",
                ["LinkAddTitle"] = "DODAJ SZYBKIE ŁĄCZE",
                ["LinkNamePlaceholder"] = "Nazwa łącza",
                ["Cancel"] = "ANULUJ",
                ["Save"] = "ZAPISZ",
                ["HttpHint"] = "Obsługiwane są łącza HTTP i HTTPS."
            },
            ["ar"] = CreateCommonTranslations("إضافة تطبيق  +", "التطبيقات المدعومة", "الإعدادات واللغة", "انقر بزر الفأرة الأيمن  /  القائمة", "انقر بزر الفأرة الأيسر للتشغيل  /  انقر بزر الفأرة الأيمن للقائمة", "اللغة", "القائمة السريعة", "تشغيل", "إزالة التطبيق", "الألعاب", "روابط سريعة", "إضافة رابط سريع", "إزالة الرابط السريع", "فتح مجلد التثبيت", "إعادة الفحص", "إغلاق", "إزالة", "إلغاء", "حفظ"),
            ["zh"] = CreateCommonTranslations("添加应用  +", "支持的应用", "设置和语言", "右键单击  /  菜单", "左键单击启动  /  右键单击菜单", "语言", "快捷菜单", "启动", "移除应用", "游戏", "快捷链接", "添加快捷链接", "移除快捷链接", "打开安装文件夹", "重新扫描", "关闭", "移除", "取消", "保存"),
            ["ko"] = CreateCommonTranslations("앱 추가  +", "지원되는 애플리케이션", "설정 및 언어", "마우스 오른쪽 버튼  /  메뉴", "왼쪽 클릭 실행  /  오른쪽 클릭 메뉴", "언어", "빠른 메뉴", "실행", "앱 제거", "게임", "빠른 링크", "빠른 링크 추가", "빠른 링크 제거", "설치 폴더 열기", "다시 검색", "닫기", "제거", "취소", "저장"),
            ["hi"] = CreateCommonTranslations("ऐप जोड़ें  +", "समर्थित ऐप्लिकेशन", "सेटिंग और भाषा", "राइट क्लिक  /  मेनू", "बाएँ क्लिक से शुरू करें  /  दाएँ क्लिक से मेनू", "भाषा", "त्वरित मेनू", "शुरू करें", "ऐप हटाएँ", "गेम", "त्वरित लिंक", "त्वरित लिंक जोड़ें", "त्वरित लिंक हटाएँ", "इंस्टॉल फ़ोल्डर खोलें", "फिर से स्कैन करें", "बंद करें", "हटाएँ", "रद्द करें", "सहेजें"),
            ["id"] = CreateCommonTranslations("TAMBAH APLIKASI  +", "Aplikasi yang didukung", "Pengaturan dan bahasa", "KLIK KANAN  /  MENU", "KLIK KIRI  JALANKAN  /  KLIK KANAN  MENU", "BAHASA", "MENU CEPAT", "JALANKAN", "HAPUS APLIKASI", "PERMAINAN", "TAUTAN CEPAT", "TAMBAH TAUTAN CEPAT", "HAPUS TAUTAN CEPAT", "BUKA FOLDER INSTALASI", "PINDAI ULANG", "TUTUP", "HAPUS", "BATAL", "SIMPAN"),
            ["ms"] = CreateCommonTranslations("TAMBAH APLIKASI  +", "Aplikasi yang disokong", "Tetapan dan bahasa", "KLIK KANAN  /  MENU", "KLIK KIRI  JALANKAN  /  KLIK KANAN  MENU", "BAHASA", "MENU PANTAS", "JALANKAN", "BUANG APLIKASI", "PERMAINAN", "PAUTAN PANTAS", "TAMBAH PAUTAN PANTAS", "BUANG PAUTAN PANTAS", "BUKA FOLDER PEMASANGAN", "IMBAS SEMULA", "TUTUP", "BUANG", "BATAL", "SIMPAN"),
            ["th"] = CreateCommonTranslations("เพิ่มแอป  +", "แอปที่รองรับ", "การตั้งค่าและภาษา", "คลิกขวา  /  เมนู", "คลิกซ้ายเพื่อเปิด  /  คลิกขวาเพื่อแสดงเมนู", "ภาษา", "เมนูด่วน", "เปิด", "ลบแอป", "เกม", "ลิงก์ด่วน", "เพิ่มลิงก์ด่วน", "ลบลิงก์ด่วน", "เปิดโฟลเดอร์ติดตั้ง", "สแกนอีกครั้ง", "ปิด", "ลบ", "ยกเลิก", "บันทึก"),
            ["vi"] = CreateCommonTranslations("THÊM ỨNG DỤNG  +", "Ứng dụng được hỗ trợ", "Cài đặt và ngôn ngữ", "NHẤP CHUỘT PHẢI  /  MENU", "NHẤP TRÁI ĐỂ MỞ  /  NHẤP PHẢI ĐỂ HIỆN MENU", "NGÔN NGỮ", "MENU NHANH", "MỞ", "XÓA ỨNG DỤNG", "TRÒ CHƠI", "LIÊN KẾT NHANH", "THÊM LIÊN KẾT NHANH", "XÓA LIÊN KẾT NHANH", "MỞ THƯ MỤC CÀI ĐẶT", "QUÉT LẠI", "ĐÓNG", "XÓA", "HỦY", "LƯU"),
            ["sv"] = CreateCommonTranslations("LÄGG TILL APP  +", "Appar som stöds", "Inställningar och språk", "HÖGERKLICKA  /  MENY", "VÄNSTERKLICKA  STARTA  /  HÖGERKLICKA  MENY", "SPRÅK", "SNABBMENY", "STARTA", "TA BORT APP", "SPEL", "SNABBLÄNKAR", "LÄGG TILL SNABBLÄNK", "TA BORT SNABBLÄNK", "ÖPPNA INSTALLATIONSMAPP", "SKANNA IGEN", "STÄNG", "TA BORT", "AVBRYT", "SPARA"),
            ["no"] = CreateCommonTranslations("LEGG TIL APP  +", "Støttede apper", "Innstillinger og språk", "HØYREKLIKK  /  MENY", "VENSTREKLIKK  START  /  HØYREKLIKK  MENY", "SPRÅK", "HURTIGMENY", "START", "FJERN APP", "SPILL", "HURTIGLENKER", "LEGG TIL HURTIGLENKE", "FJERN HURTIGLENKE", "ÅPNE INSTALLASJONSMAPPE", "SKANN PÅ NYTT", "LUKK", "FJERN", "AVBRYT", "LAGRE"),
            ["da"] = CreateCommonTranslations("TILFØJ APP  +", "Understøttede apps", "Indstillinger og sprog", "HØJREKLIK  /  MENU", "VENSTREKLIK  START  /  HØJREKLIK  MENU", "SPROG", "HURTIGMENU", "START", "FJERN APP", "SPIL", "HURTIGLINKS", "TILFØJ HURTIGLINK", "FJERN HURTIGLINK", "ÅBN INSTALLATIONSMAPPE", "SCAN IGEN", "LUK", "FJERN", "ANNULLER", "GEM"),
            ["fi"] = CreateCommonTranslations("LISÄÄ SOVELLUS  +", "Tuetut sovellukset", "Asetukset ja kieli", "KLIKKAA HIIRAN OIKEALLA  /  VALIKKO", "KLIKKAA VASEMMALLA  KÄYNNISTÄ  /  OIKEALLA  VALIKKO", "KIELI", "PIKAVALIKKO", "KÄYNNISTÄ", "POISTA SOVELLUS", "PELIT", "PIKALINKIT", "LISÄÄ PIKALINKKI", "POISTA PIKALINKKI", "AVAA ASENNUSKANSIO", "SKANNAA UUDELLEEN", "SULJE", "POISTA", "PERUUTA", "TALLENNA"),
            ["el"] = CreateCommonTranslations("ΠΡΟΣΘΗΚΗ ΕΦΑΡΜΟΓΗΣ  +", "Υποστηριζόμενες εφαρμογές", "Ρυθμίσεις και γλώσσα", "ΔΕΞΙ ΚΛΙΚ  /  ΜΕΝΟΥ", "ΑΡΙΣΤΕΡΟ ΚΛΙΚ  ΕΚΚΙΝΗΣΗ  /  ΔΕΞΙ ΚΛΙΚ  ΜΕΝΟΥ", "ΓΛΩΣΣΑ", "ΓΡΗΓΟΡΟ ΜΕΝΟΥ", "ΕΚΚΙΝΗΣΗ", "ΑΦΑΙΡΕΣΗ ΕΦΑΡΜΟΓΗΣ", "ΠΑΙΧΝΙΔΙΑ", "ΓΡΗΓΟΡΟΙ ΣΥΝΔΕΣΜΟΙ", "ΠΡΟΣΘΗΚΗ ΣΥΝΔΕΣΜΟΥ", "ΑΦΑΙΡΕΣΗ ΣΥΝΔΕΣΜΟΥ", "ΑΝΟΙΓΜΑ ΦΑΚΕΛΟΥ ΕΓΚΑΤΑΣΤΑΣΗΣ", "ΝΕΑ ΣΑΡΩΣΗ", "ΚΛΕΙΣΙΜΟ", "ΑΦΑΙΡΕΣΗ", "ΑΚΥΡΩΣΗ", "ΑΠΟΘΗΚΕΥΣΗ"),
            ["cs"] = CreateCommonTranslations("PŘIDAT APLIKACI  +", "Podporované aplikace", "Nastavení a jazyk", "KLIKNUTÍ PRAVÝM TLAČÍTKEM  /  MENU", "LEVÝ KLIK  SPUSTIT  /  PRAVÝ KLIK  MENU", "JAZYK", "RYCHLÁ NABÍDKA", "SPUSTIT", "ODEBRAT APLIKACI", "HRY", "RYCHLÉ ODKAZY", "PŘIDAT RYCHLÝ ODKAZ", "ODEBRAT RYCHLÝ ODKAZ", "OTEVŘÍT INSTALAČNÍ SLOŽKU", "ZKONTROLOVAT ZNOVU", "ZAVŘÍT", "ODEBRAT", "ZRUŠIT", "ULOŽIT"),
            ["sk"] = CreateCommonTranslations("PRIDAŤ APLIKÁCIU  +", "Podporované aplikácie", "Nastavenia a jazyk", "KLIKNUTIE PRAVÝM TLAČIDLOM  /  MENU", "ĽAVÝ KLIK  SPUSTIŤ  /  PRAVÝ KLIK  MENU", "JAZYK", "RÝCHLE MENU", "SPUSTIŤ", "ODSTRÁNIŤ APLIKÁCIU", "HRY", "RÝCHLE ODKAZY", "PRIDAŤ RÝCHLY ODKAZ", "ODSTRÁNIŤ RÝCHLY ODKAZ", "OTVORIŤ INŠTALAČNÝ PRIEČINOK", "ZNOVA SKENOVAŤ", "ZAVRIEŤ", "ODSTRÁNIŤ", "ZRUŠIŤ", "ULOŽIŤ"),
            ["hu"] = CreateCommonTranslations("ALKALMAZÁS HOZZÁADÁSA  +", "Támogatott alkalmazások", "Beállítások és nyelv", "JOBB KATTINTÁS  /  MENÜ", "BAL KATTINTÁS  INDÍTÁS  /  JOBB KATTINTÁS  MENÜ", "NYELV", "GYORSMENÜ", "INDÍTÁS", "ALKALMAZÁS ELTÁVOLÍTÁSA", "JÁTÉKOK", "GYORS LINKEK", "GYORS LINK HOZZÁADÁSA", "GYORS LINK ELTÁVOLÍTÁSA", "TELEPÍTÉSI MAPPA MEGNYITÁSA", "ÚJRAKERESÉS", "BEZÁRÁS", "ELTÁVOLÍTÁS", "MÉGSE", "MENTÉS"),
            ["ro"] = CreateCommonTranslations("ADAUGĂ APLICAȚIE  +", "Aplicații acceptate", "Setări și limbă", "CLIC DREAPTA  /  MENIU", "CLIC STÂNGA  PORNIRE  /  CLIC DREAPTA  MENIU", "LIMBĂ", "MENIU RAPID", "PORNEȘTE", "ELIMINĂ APLICAȚIA", "JOCURI", "LINKURI RAPIDE", "ADAUGĂ LINK RAPID", "ELIMINĂ LINKUL RAPID", "DESCHIDE DOSARUL DE INSTALARE", "SCANEAZĂ DIN NOU", "ÎNCHIDE", "ELIMINĂ", "ANULEAZĂ", "SALVEAZĂ"),
            ["bg"] = CreateCommonTranslations("ДОБАВИ ПРИЛОЖЕНИЕ  +", "Поддържани приложения", "Настройки и език", "ДЕСЕН КЛИК  /  МЕНЮ", "ЛЯВ КЛИК  СТАРТИРАНЕ  /  ДЕСЕН КЛИК  МЕНЮ", "ЕЗИК", "БЪРЗО МЕНЮ", "СТАРТИРАЙ", "ПРЕМАХНИ ПРИЛОЖЕНИЕ", "ИГРИ", "БЪРЗИ ВРЪЗКИ", "ДОБАВИ БЪРЗА ВРЪЗКА", "ПРЕМАХНИ БЪРЗА ВРЪЗКА", "ОТВОРИ ИНСТАЛАЦИОННАТА ПАПКА", "СКАНИРАЙ ОТНОВО", "ЗАТВОРИ", "ПРЕМАХНИ", "ОТКАЗ", "ЗАПАЗИ"),
            ["uk"] = CreateCommonTranslations("ДОДАТИ ЗАСТОСУНОК  +", "Підтримувані застосунки", "Налаштування та мова", "ПРАВИЙ КЛІК  /  МЕНЮ", "ЛІВИЙ КЛІК  ЗАПУСТИТИ  /  ПРАВИЙ КЛІК  МЕНЮ", "МОВА", "ШВИДКЕ МЕНЮ", "ЗАПУСТИТИ", "ВИДАЛИТИ ЗАСТОСУНОК", "ІГРИ", "ШВИДКІ ПОСИЛАННЯ", "ДОДАТИ ШВИДКЕ ПОСИЛАННЯ", "ВИДАЛИТИ ШВИДКЕ ПОСИЛАННЯ", "ВІДКРИТИ ПАПКУ ВСТАНОВЛЕННЯ", "СКАНУВАТИ ЗНОВУ", "ЗАКРИТИ", "ВИДАЛИТИ", "СКАСУВАТИ", "ЗБЕРЕГТИ"),
            ["hr"] = CreateCommonTranslations("DODAJ APLIKACIJU  +", "Podržane aplikacije", "Postavke i jezik", "DESNI KLIK  /  IZBORNIK", "LIJEVI KLIK  POKRENI  /  DESNI KLIK  IZBORNIK", "JEZIK", "BRZI IZBORNIK", "POKRENI", "UKLONI APLIKACIJU", "IGRE", "BRZE POVEZNICE", "DODAJ BRZU POVEZNICU", "UKLONI BRZU POVEZNICU", "OTVORI INSTALACIJSKU MAPU", "PONOVNO SKENIRAJ", "ZATVORI", "UKLONI", "ODUSTANI", "SPREMI"),
            ["sr"] = CreateCommonTranslations("ДОДАЈ АПЛИКАЦИЈУ  +", "Подржане апликације", "Подешавања и језик", "ДЕСНИ КЛИК  /  МЕНИ", "ЛЕВИ КЛИК  ПОКРЕНИ  /  ДЕСНИ КЛИК  МЕНИ", "ЈЕЗИК", "БРЗИ МЕНИ", "ПОКРЕНИ", "УКЛОНИ АПЛИКАЦИЈУ", "ИГРЕ", "БРЗЕ ВЕЗЕ", "ДОДАЈ БРЗУ ВЕЗУ", "УКЛОНИ БРЗУ ВЕЗУ", "ОТВОРИ ИНСТАЛАЦИОНИ ФОЛДЕР", "ПОНОВО СКЕНИРАЈ", "ЗАТВОРИ", "УКЛОНИ", "ОТКАЖИ", "САЧУВАЈ"),
            ["he"] = CreateCommonTranslations("הוספת אפליקציה  +", "אפליקציות נתמכות", "הגדרות ושפה", "לחיצה ימנית  /  תפריט", "לחיצה שמאלית להפעלה  /  לחיצה ימנית לתפריט", "שפה", "תפריט מהיר", "הפעלה", "הסרת אפליקציה", "משחקים", "קישורים מהירים", "הוספת קישור מהיר", "הסרת קישור מהיר", "פתיחת תיקיית התקנה", "סריקה מחדש", "סגירה", "הסרה", "ביטול", "שמירה"),
            ["bn"] = CreateCommonTranslations("অ্যাপ যোগ করুন  +", "সমর্থিত অ্যাপ", "সেটিংস ও ভাষা", "ডান ক্লিক  /  মেনু", "চালু করতে বাম ক্লিক  /  মেনুর জন্য ডান ক্লিক", "ভাষা", "দ্রুত মেনু", "চালু করুন", "অ্যাপ সরান", "গেম", "দ্রুত লিংক", "দ্রুত লিংক যোগ করুন", "দ্রুত লিংক সরান", "ইনস্টল ফোল্ডার খুলুন", "আবার স্ক্যান করুন", "বন্ধ করুন", "সরান", "বাতিল", "সংরক্ষণ"),
            ["ur"] = CreateCommonTranslations("ایپ شامل کریں  +", "معاون ایپس", "ترتیبات اور زبان", "رائٹ کلک  /  مینو", "چلانے کے لیے لیفٹ کلک  /  مینو کے لیے رائٹ کلک", "زبان", "فوری مینو", "چلائیں", "ایپ ہٹائیں", "گیمز", "فوری لنکس", "فوری لنک شامل کریں", "فوری لنک ہٹائیں", "انسٹالیشن فولڈر کھولیں", "دوبارہ اسکین کریں", "بند کریں", "ہٹائیں", "منسوخ کریں", "محفوظ کریں"),
            ["fa"] = CreateCommonTranslations("افزودن برنامه  +", "برنامه‌های پشتیبانی‌شده", "تنظیمات و زبان", "کلیک راست  /  منو", "کلیک چپ برای اجرا  /  کلیک راست برای منو", "زبان", "منوی سریع", "اجرا", "حذف برنامه", "بازی‌ها", "پیوندهای سریع", "افزودن پیوند سریع", "حذف پیوند سریع", "باز کردن پوشه نصب", "اسکن دوباره", "بستن", "حذف", "لغو", "ذخیره"),
            ["sw"] = CreateCommonTranslations("ONGEZA PROGRAMU  +", "Programu zinazotumika", "Mipangilio na lugha", "BONYEZA KULIA  /  MENYU", "BONYEZA KUSHOTO  FUNGUA  /  BONYEZA KULIA  MENYU", "LUGHA", "MENYU YA HARAKA", "FUNGUA", "ONDOA PROGRAMU", "MICHEZO", "VIUNGO VYA HARAKA", "ONGEZA KIUNGO CHA HARAKA", "ONDOA KIUNGO CHA HARAKA", "FUNGUA FOLDA YA USAKINISHAJI", "CHANGANUA TENA", "FUNGA", "ONDOA", "GHAIRI", "HIFADHI"),
            ["et"] = CreateCommonTranslations("LISA RAKENDUS  +", "Toetatud rakendused", "Seaded ja keel", "PAREMklõps  /  MENÜÜ", "VASAKKLIKK  KÄIVITA  /  PAREMKLIKK  MENÜÜ", "KEEL", "KIIRMENÜÜ", "KÄIVITA", "EEMALDA RAKENDUS", "MÄNGUD", "KIIRLINGID", "LISA KIIRLINK", "EEMALDA KIIRLINK", "AVA INSTALLIKAUST", "SKANNI UUESTI", "SULGE", "EEMALDA", "TÜHISTA", "SALVESTA"),
            ["lv"] = CreateCommonTranslations("PIEVIENOT LIETOTNI  +", "Atbalstītās lietotnes", "Iestatījumi un valoda", "LABĀS POGAS KLIKŠĶIS  /  IZVĒLNE", "KREISAIS KLIKŠĶIS  PALAIST  /  LABAIS KLIKŠĶIS  IZVĒLNE", "VALODA", "ĀTRĀ IZVĒLNE", "PALAIST", "NOŅEMT LIETOTNI", "SPĒLES", "ĀTRĀS SAITES", "PIEVIENOT ĀTRO SAITI", "NOŅEMT ĀTRO SAITI", "ATVĒRT INSTALĀCIJAS MAPI", "SKENĒT VĒLREIZ", "AIZVĒRT", "NOŅEMT", "ATCELT", "SAGLABĀT"),
            ["lt"] = CreateCommonTranslations("PRIDĖTI PROGRAMĄ  +", "Palaikomos programos", "Nustatymai ir kalba", "DEŠINYSIS SPUSTELĖJIMAS  /  MENIU", "KAIRYSIS SPUSTELĖJIMAS  PALEISTI  /  DEŠINYSIS  MENIU", "KALBA", "SPARTUSIS MENIU", "PALEISTI", "PAŠALINTI PROGRAMĄ", "ŽAIDIMAI", "SPARTIEJI RYŠIAI", "PRIDĖTI SPARTŲJĮ RYŠĮ", "PAŠALINTI SPARTŲJĮ RYŠĮ", "ATIDARYTI DIEGIMO APLANKĄ", "NUSKAITYTI IŠ NAUJO", "UŽDARYTI", "PAŠALINTI", "ATŠAUKTI", "IŠSAUGOTI")
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AdditionalTranslations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ar"] = CreateAdditionalTranslations("التطبيقات المدعومة", "يتم اكتشافها تلقائيًا", "مشغلات الألعاب: Steam وEpic Games وEA app وUbisoft Connect وBattle.net وGOG Galaxy وRiot Client وRockstar Games Launcher وAmazon Games وitch.io وHoYoPlay وMinecraft Launcher وXbox.\n\nالمتصفحات: Chrome وMicrosoft Edge وFirefox وBrave وVivaldi وOpera GX.\n\nالتواصل والتطبيقات اليومية: Discord وWhatsApp وTelegram وSignal وMicrosoft Teams وZoom وSlack وChatGPT وSpotify وVLC وGoogle Drive وOBS Studio وVS Code وMedal.\n\nتطبيقات Windows وPWA المثبتة: YouTube وInstagram وTikTok وFacebook وMessenger وGoogle Maps.\n\nإضافة تطبيق: استخدم زر إضافة تطبيق لإضافة ملفات .exe أو .lnk أو .appref-ms. تظهر التطبيقات المثبتة والقابلة للتشغيل فقط.", "موافق"),
            ["zh"] = CreateAdditionalTranslations("支持的应用", "自动检测", "游戏启动器：Steam、Epic Games、EA app、Ubisoft Connect、Battle.net、GOG Galaxy、Riot Client、Rockstar Games Launcher、Amazon Games、itch.io、HoYoPlay、Minecraft Launcher 和 Xbox。\n\n浏览器：Chrome、Microsoft Edge、Firefox、Brave、Vivaldi 和 Opera GX。\n\n通讯和日常应用：Discord、WhatsApp、Telegram、Signal、Microsoft Teams、Zoom、Slack、ChatGPT、Spotify、VLC、Google Drive、OBS Studio、VS Code 和 Medal。\n\n已安装的 Windows 应用和 PWA：YouTube、Instagram、TikTok、Facebook、Messenger 和 Google Maps。\n\n添加自己的应用：使用“添加应用”按钮添加 .exe、.lnk 或 .appref-ms 文件。这里只显示已安装且可启动的应用。", "确定"),
            ["ko"] = CreateAdditionalTranslations("지원되는 애플리케이션", "자동 감지됨", "게임 런처: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher, Xbox.\n\n브라우저: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi, Opera GX.\n\n커뮤니케이션 및 일상 앱: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code, Medal.\n\n설치된 Windows 앱 및 PWA: YouTube, Instagram, TikTok, Facebook, Messenger, Google Maps.\n\n앱 직접 추가: 앱 추가 버튼으로 .exe, .lnk 또는 .appref-ms 파일을 추가하세요. 설치되어 실행할 수 있는 앱만 표시됩니다.", "확인"),
            ["hi"] = CreateAdditionalTranslations("समर्थित ऐप्लिकेशन", "स्वतः पहचाने गए", "गेम लॉन्चर: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher और Xbox।\n\nब्राउज़र: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi और Opera GX।\n\nसंचार और रोज़मर्रा के ऐप: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code और Medal।\n\nइंस्टॉल किए गए Windows ऐप और PWA: YouTube, Instagram, TikTok, Facebook, Messenger और Google Maps।\n\nअपना ऐप जोड़ें: .exe, .lnk या .appref-ms फ़ाइल जोड़ने के लिए ऐप जोड़ें बटन का उपयोग करें। केवल इंस्टॉल और चलाए जा सकने वाले ऐप दिखते हैं।", "ठीक है"),
            ["id"] = CreateAdditionalTranslations("Aplikasi yang Didukung", "TERDETEKSI OTOMATIS", "Peluncur game: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher, dan Xbox.\n\nBrowser: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi, dan Opera GX.\n\nKomunikasi dan aplikasi harian: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code, dan Medal.\n\nAplikasi Windows dan PWA terpasang: YouTube, Instagram, TikTok, Facebook, Messenger, dan Google Maps.\n\nTambahkan aplikasi sendiri: gunakan tombol Tambah Aplikasi untuk menambahkan file .exe, .lnk, atau .appref-ms. Hanya aplikasi terpasang yang dapat dijalankan yang ditampilkan.", "OK"),
            ["ms"] = CreateAdditionalTranslations("Aplikasi yang Disokong", "DIKESAN SECARA AUTOMATIK", "Pelancar permainan: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher dan Xbox.\n\nPelayar: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi dan Opera GX.\n\nKomunikasi dan aplikasi harian: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code dan Medal.\n\nAplikasi Windows dan PWA yang dipasang: YouTube, Instagram, TikTok, Facebook, Messenger dan Google Maps.\n\nTambah aplikasi sendiri: gunakan butang Tambah Aplikasi untuk menambah fail .exe, .lnk atau .appref-ms. Hanya aplikasi yang dipasang dan boleh dijalankan dipaparkan.", "OK"),
            ["th"] = CreateAdditionalTranslations("แอปที่รองรับ", "ตรวจพบอัตโนมัติ", "ตัวเรียกใช้เกม: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher และ Xbox\n\nเบราว์เซอร์: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi และ Opera GX\n\nการสื่อสารและแอปทั่วไป: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code และ Medal\n\nแอป Windows และ PWA ที่ติดตั้ง: YouTube, Instagram, TikTok, Facebook, Messenger และ Google Maps\n\nเพิ่มแอปเอง: ใช้ปุ่มเพิ่มแอปเพื่อเพิ่มไฟล์ .exe, .lnk หรือ .appref-ms จะแสดงเฉพาะแอปที่ติดตั้งและเปิดใช้งานได้", "ตกลง"),
            ["vi"] = CreateAdditionalTranslations("Ứng dụng được hỗ trợ", "TỰ ĐỘNG PHÁT HIỆN", "Trình khởi chạy trò chơi: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher và Xbox.\n\nTrình duyệt: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi và Opera GX.\n\nLiên lạc và ứng dụng hằng ngày: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code và Medal.\n\nỨng dụng Windows và PWA đã cài: YouTube, Instagram, TikTok, Facebook, Messenger và Google Maps.\n\nThêm ứng dụng của bạn: dùng nút Thêm ứng dụng để thêm tệp .exe, .lnk hoặc .appref-ms. Chỉ hiển thị ứng dụng đã cài đặt và có thể chạy.", "Đồng ý"),
            ["sv"] = CreateAdditionalTranslations("Appar som stöds", "DETEKTERAS AUTOMATISKT", "Spelstartare: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher och Xbox.\n\nWebbläsare: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi och Opera GX.\n\nKommunikation och vardagsappar: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code och Medal.\n\nInstallerade Windows-appar och PWA: YouTube, Instagram, TikTok, Facebook, Messenger och Google Maps.\n\nLägg till en egen app: använd knappen Lägg till app för att lägga till .exe-, .lnk- eller .appref-ms-filer. Endast installerade appar som kan startas visas.", "OK"),
            ["no"] = CreateAdditionalTranslations("Støttede apper", "OPPDAGES AUTOMATISK", "Spillstartere: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher og Xbox.\n\nNettlesere: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi og Opera GX.\n\nKommunikasjon og hverdagsapper: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code og Medal.\n\nInstallerte Windows-apper og PWA-er: YouTube, Instagram, TikTok, Facebook, Messenger og Google Maps.\n\nLegg til en egen app: bruk knappen Legg til app for å legge til .exe-, .lnk- eller .appref-ms-filer. Bare installerte apper som kan startes, vises.", "OK"),
            ["da"] = CreateAdditionalTranslations("Understøttede apps", "REGISTRERES AUTOMATISK", "Spilstartprogrammer: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher og Xbox.\n\nBrowsere: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi og Opera GX.\n\nKommunikation og hverdagsapps: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code og Medal.\n\nInstallerede Windows-apps og PWA'er: YouTube, Instagram, TikTok, Facebook, Messenger og Google Maps.\n\nTilføj din egen app: brug knappen Tilføj app til at tilføje .exe-, .lnk- eller .appref-ms-filer. Kun installerede apps, der kan startes, vises.", "OK"),
            ["fi"] = CreateAdditionalTranslations("Tuetut sovellukset", "TUNNISTETAAN AUTOMAATTISESTI", "Pelikäynnistimet: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher ja Xbox.\n\nSelaimet: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi ja Opera GX.\n\nViestintä- ja päivittäiset sovellukset: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code ja Medal.\n\nAsennetut Windows-sovellukset ja PWA:t: YouTube, Instagram, TikTok, Facebook, Messenger ja Google Maps.\n\nLisää oma sovellus: lisää .exe-, .lnk- tai .appref-ms-tiedosto Lisää sovellus -painikkeella. Vain asennetut ja käynnistettävät sovellukset näytetään.", "OK"),
            ["el"] = CreateAdditionalTranslations("Υποστηριζόμενες εφαρμογές", "ΑΥΤΟΜΑΤΟΣ ΕΝΤΟΠΙΣΜΟΣ", "Εκκινητές παιχνιδιών: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher και Xbox.\n\nΠεριηγητές: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi και Opera GX.\n\nΕπικοινωνία και καθημερινές εφαρμογές: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code και Medal.\n\nΕγκατεστημένες εφαρμογές Windows και PWA: YouTube, Instagram, TikTok, Facebook, Messenger και Google Maps.\n\nΠροσθήκη εφαρμογής: χρησιμοποιήστε το κουμπί προσθήκης για αρχεία .exe, .lnk ή .appref-ms. Εμφανίζονται μόνο εγκατεστημένες εφαρμογές που μπορούν να εκκινηθούν.", "Εντάξει"),
            ["cs"] = CreateAdditionalTranslations("Podporované aplikace", "AUTOMATICKY ZJIŠTĚNO", "Herní launchery: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher a Xbox.\n\nProhlížeče: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi a Opera GX.\n\nKomunikace a běžné aplikace: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code a Medal.\n\nNainstalované aplikace Windows a PWA: YouTube, Instagram, TikTok, Facebook, Messenger a Google Maps.\n\nPřidání vlastní aplikace: tlačítkem Přidat aplikaci přidejte soubor .exe, .lnk nebo .appref-ms. Zobrazují se pouze nainstalované a spustitelné aplikace.", "OK"),
            ["sk"] = CreateAdditionalTranslations("Podporované aplikácie", "AUTOMATICKY ZISTENÉ", "Herné launchery: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher a Xbox.\n\nPrehliadače: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi a Opera GX.\n\nKomunikácia a bežné aplikácie: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code a Medal.\n\nNainštalované aplikácie Windows a PWA: YouTube, Instagram, TikTok, Facebook, Messenger a Google Maps.\n\nPridanie vlastnej aplikácie: tlačidlom Pridať aplikáciu pridajte súbor .exe, .lnk alebo .appref-ms. Zobrazujú sa iba nainštalované aplikácie, ktoré možno spustiť.", "OK"),
            ["hu"] = CreateAdditionalTranslations("Támogatott alkalmazások", "AUTOMATIKUSAN FELISMERETT", "Játékindítók: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher és Xbox.\n\nBöngészők: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi és Opera GX.\n\nKommunikációs és mindennapi alkalmazások: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code és Medal.\n\nTelepített Windows-alkalmazások és PWA-k: YouTube, Instagram, TikTok, Facebook, Messenger és Google Maps.\n\nSaját alkalmazás hozzáadása: az Alkalmazás hozzáadása gombbal .exe, .lnk vagy .appref-ms fájlt adhat hozzá. Csak telepített és indítható alkalmazások jelennek meg.", "OK"),
            ["ro"] = CreateAdditionalTranslations("Aplicații acceptate", "DETECTATE AUTOMAT", "Lansatoare de jocuri: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher și Xbox.\n\nBrowsere: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi și Opera GX.\n\nComunicare și aplicații de zi cu zi: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code și Medal.\n\nAplicații Windows și PWA instalate: YouTube, Instagram, TikTok, Facebook, Messenger și Google Maps.\n\nAdaugă propria aplicație: folosește butonul Adaugă aplicație pentru fișiere .exe, .lnk sau .appref-ms. Sunt afișate doar aplicațiile instalate și lansabile.", "OK"),
            ["bg"] = CreateAdditionalTranslations("Поддържани приложения", "ОТКРИВА СЕ АВТОМАТИЧНО", "Стартови програми за игри: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher и Xbox.\n\nБраузъри: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi и Opera GX.\n\nКомуникация и ежедневни приложения: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code и Medal.\n\nИнсталирани Windows приложения и PWA: YouTube, Instagram, TikTok, Facebook, Messenger и Google Maps.\n\nДобавяне на приложение: използвайте бутона за добавяне, за да добавите .exe, .lnk или .appref-ms файл. Показват се само инсталирани приложения, които могат да се стартират.", "Добре"),
            ["uk"] = CreateAdditionalTranslations("Підтримувані застосунки", "ВИЯВЛЕНО АВТОМАТИЧНО", "Ігрові лаунчери: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher та Xbox.\n\nБраузери: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi та Opera GX.\n\nЗв’язок і повсякденні застосунки: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code та Medal.\n\nВстановлені програми Windows і PWA: YouTube, Instagram, TikTok, Facebook, Messenger та Google Maps.\n\nДодайте власний застосунок: скористайтеся кнопкою додавання, щоб додати файл .exe, .lnk або .appref-ms. Відображаються лише встановлені застосунки, які можна запустити.", "Гаразд"),
            ["hr"] = CreateAdditionalTranslations("Podržane aplikacije", "AUTOMATSKI OTKRIVENO", "Pokretači igara: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher i Xbox.\n\nPreglednici: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi i Opera GX.\n\nKomunikacija i svakodnevne aplikacije: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code i Medal.\n\nInstalirane Windows aplikacije i PWA: YouTube, Instagram, TikTok, Facebook, Messenger i Google Maps.\n\nDodajte vlastitu aplikaciju: gumbom za dodavanje dodajte .exe, .lnk ili .appref-ms datoteku. Prikazuju se samo instalirane aplikacije koje se mogu pokrenuti.", "U redu"),
            ["sr"] = CreateAdditionalTranslations("Подржане апликације", "АУТОМАТСКИ ОТКРИВЕНО", "Покретачи игара: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher и Xbox.\n\nПрегледачи: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi и Opera GX.\n\nКомуникација и свакодневне апликације: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code и Medal.\n\nИнсталиране Windows апликације и PWA: YouTube, Instagram, TikTok, Facebook, Messenger и Google Maps.\n\nДодајте сопствену апликацију: дугметом за додавање додајте .exe, .lnk или .appref-ms датотеку. Приказују се само инсталиране апликације које могу да се покрену.", "У реду"),
            ["he"] = CreateAdditionalTranslations("אפליקציות נתמכות", "זוהו אוטומטית", "משגרי משחקים: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher ו-Xbox.\n\nדפדפנים: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi ו-Opera GX.\n\nתקשורת ואפליקציות יומיומיות: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code ו-Medal.\n\nאפליקציות Windows ו-PWA מותקנות: YouTube, Instagram, TikTok, Facebook, Messenger ו-Google Maps.\n\nהוספת אפליקציה משלך: השתמשו בלחצן הוספת אפליקציה כדי להוסיף קובץ .exe, .lnk או .appref-ms. מוצגות רק אפליקציות מותקנות שניתן להפעיל.", "אישור"),
            ["bn"] = CreateAdditionalTranslations("সমর্থিত অ্যাপ", "স্বয়ংক্রিয়ভাবে শনাক্ত", "গেম লঞ্চার: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher এবং Xbox।\n\nব্রাউজার: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi এবং Opera GX।\n\nযোগাযোগ ও দৈনন্দিন অ্যাপ: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code এবং Medal।\n\nইনস্টল করা Windows অ্যাপ ও PWA: YouTube, Instagram, TikTok, Facebook, Messenger এবং Google Maps।\n\nনিজের অ্যাপ যোগ করুন: .exe, .lnk বা .appref-ms ফাইল যোগ করতে অ্যাপ যোগ করুন বোতাম ব্যবহার করুন। শুধু ইনস্টল করা ও চালানো যায় এমন অ্যাপ দেখানো হয়।", "ঠিক আছে"),
            ["ur"] = CreateAdditionalTranslations("معاون ایپس", "خودکار طور پر شناخت شدہ", "گیم لانچر: Steam، Epic Games، EA app، Ubisoft Connect، Battle.net، GOG Galaxy، Riot Client، Rockstar Games Launcher، Amazon Games، itch.io، HoYoPlay، Minecraft Launcher اور Xbox۔\n\nبراؤزر: Chrome، Microsoft Edge، Firefox، Brave، Vivaldi اور Opera GX۔\n\nرابطے اور روزمرہ کی ایپس: Discord، WhatsApp، Telegram، Signal، Microsoft Teams، Zoom، Slack، ChatGPT، Spotify، VLC، Google Drive، OBS Studio، VS Code اور Medal۔\n\nانسٹال شدہ Windows ایپس اور PWA: YouTube، Instagram، TikTok، Facebook، Messenger اور Google Maps۔\n\nاپنی ایپ شامل کریں: .exe، .lnk یا .appref-ms فائل شامل کرنے کے لیے ایپ شامل کریں کا بٹن استعمال کریں۔ صرف انسٹال اور چلنے کے قابل ایپس دکھائی جاتی ہیں۔", "ٹھیک ہے"),
            ["fa"] = CreateAdditionalTranslations("برنامه‌های پشتیبانی‌شده", "شناسایی خودکار", "اجراکننده‌های بازی: Steam، Epic Games، EA app، Ubisoft Connect، Battle.net، GOG Galaxy، Riot Client، Rockstar Games Launcher، Amazon Games، itch.io، HoYoPlay، Minecraft Launcher و Xbox.\n\nمرورگرها: Chrome، Microsoft Edge، Firefox، Brave، Vivaldi و Opera GX.\n\nارتباطات و برنامه‌های روزمره: Discord، WhatsApp، Telegram، Signal، Microsoft Teams، Zoom، Slack، ChatGPT، Spotify، VLC، Google Drive، OBS Studio، VS Code و Medal.\n\nبرنامه‌های نصب‌شده Windows و PWA: YouTube، Instagram، TikTok، Facebook، Messenger و Google Maps.\n\nافزودن برنامه دلخواه: برای افزودن فایل .exe، .lnk یا .appref-ms از دکمه افزودن برنامه استفاده کنید. فقط برنامه‌های نصب‌شده و قابل اجرا نمایش داده می‌شوند.", "تأیید"),
            ["sw"] = CreateAdditionalTranslations("Programu zinazotumika", "ZINAGUNDULIWA KIOTOMATIKI", "Vifungua michezo: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher na Xbox.\n\nVivinjari: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi na Opera GX.\n\nMawasiliano na programu za kila siku: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code na Medal.\n\nProgramu za Windows na PWA zilizosakinishwa: YouTube, Instagram, TikTok, Facebook, Messenger na Google Maps.\n\nOngeza programu yako: tumia kitufe cha kuongeza programu kuchagua faili ya .exe, .lnk au .appref-ms. Programu zilizosakinishwa na zinazoweza kufunguliwa pekee ndizo zinazoonyeshwa.", "Sawa"),
            ["et"] = CreateAdditionalTranslations("Toetatud rakendused", "AUTOMAATSELT TUVASTATUD", "Mängukäivitajad: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher ja Xbox.\n\nBrauserid: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi ja Opera GX.\n\nSuhtlus- ja igapäevarakendused: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code ja Medal.\n\nInstallitud Windowsi rakendused ja PWA-d: YouTube, Instagram, TikTok, Facebook, Messenger ja Google Maps.\n\nLisa oma rakendus: kasuta rakenduse lisamise nuppu .exe-, .lnk- või .appref-ms-faili lisamiseks. Kuvatakse ainult installitud ja käivitatavad rakendused.", "OK"),
            ["lv"] = CreateAdditionalTranslations("Atbalstītās lietotnes", "AUTOMĀTISKI NOTEIKTAS", "Spēļu palaidēji: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher un Xbox.\n\nPārlūkprogrammas: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi un Opera GX.\n\nSaziņas un ikdienas lietotnes: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code un Medal.\n\nInstalētās Windows lietotnes un PWA: YouTube, Instagram, TikTok, Facebook, Messenger un Google Maps.\n\nPievienojiet savu lietotni: izmantojiet lietotnes pievienošanas pogu, lai pievienotu .exe, .lnk vai .appref-ms failu. Tiek rādītas tikai instalētas un palaižamas lietotnes.", "Labi"),
            ["lt"] = CreateAdditionalTranslations("Palaikomos programos", "APTIKTA AUTOMATIŠKAI", "Žaidimų paleidimo priemonės: Steam, Epic Games, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Riot Client, Rockstar Games Launcher, Amazon Games, itch.io, HoYoPlay, Minecraft Launcher ir Xbox.\n\nNaršyklės: Chrome, Microsoft Edge, Firefox, Brave, Vivaldi ir Opera GX.\n\nRyšių ir kasdienės programos: Discord, WhatsApp, Telegram, Signal, Microsoft Teams, Zoom, Slack, ChatGPT, Spotify, VLC, Google Drive, OBS Studio, VS Code ir Medal.\n\nĮdiegtos Windows programos ir PWA: YouTube, Instagram, TikTok, Facebook, Messenger ir Google Maps.\n\nPridėkite savo programą: naudokite programos pridėjimo mygtuką .exe, .lnk arba .appref-ms failui pasirinkti. Rodomos tik įdiegtos ir paleidžiamos programos.", "Gerai")
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LocalizedAppNames =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ru"] = CreateAppNames(("Steam", "Стим"), ("Epic Games", "Эпик Геймс"), ("EA App", "Приложение EA"), ("Ubisoft Connect", "Юбисофт Коннект"), ("Battle.net", "Батл.нет"), ("GOG Galaxy", "ГОГ Гэлакси"), ("Riot Client", "Клиент Райот"), ("Rockstar Games Launcher", "Лаунчер Рокстар Геймс"), ("Amazon Games", "Игры Амазон"), ("HoYoPlay", "ХоёПлей"), ("Discord", "Дискорд"), ("WhatsApp", "Вотсап"), ("Telegram", "Телеграм"), ("Zoom", "Зум"), ("Microsoft Teams", "Майкрософт Тимс"), ("Minecraft Launcher", "Лаунчер Майнкрафт"), ("Spotify", "Спотифай"), ("VS Code", "ВС Код"), ("Google Chrome", "Гугл Хром"), ("Microsoft Edge", "Майкрософт Эдж"), ("Mozilla Firefox", "Мозилла Файрфокс"), ("Opera GX", "Опера ГХ"), ("Xbox", "Иксбокс"), ("ChatGPT", "ЧатГПТ")),
            ["ja"] = CreateAppNames(("Steam", "スチーム"), ("Epic Games", "エピックゲームズ"), ("EA App", "EAアプリ"), ("Ubisoft Connect", "ユービーアイソフトコネクト"), ("Battle.net", "バトルネット"), ("GOG Galaxy", "GOGギャラクシー"), ("Riot Client", "ライアットクライアント"), ("Rockstar Games Launcher", "ロックスターゲームスランチャー"), ("Amazon Games", "アマゾンゲームズ"), ("HoYoPlay", "ホヨプレイ"), ("Discord", "ディスコード"), ("WhatsApp", "ワッツアップ"), ("Telegram", "テレグラム"), ("Zoom", "ズーム"), ("Microsoft Teams", "マイクロソフトチームズ"), ("Minecraft Launcher", "マインクラフトランチャー"), ("Spotify", "スポティファイ"), ("VS Code", "VSコード"), ("Google Chrome", "グーグルクローム"), ("Microsoft Edge", "マイクロソフトエッジ"), ("Mozilla Firefox", "モジラファイアフォックス"), ("Opera GX", "オペラGX"), ("Xbox", "エックスボックス"), ("ChatGPT", "チャットGPT")),
            ["zh"] = CreateAppNames(("Steam", "蒸汽平台"), ("Epic Games", "Epic游戏"), ("EA App", "EA应用"), ("Ubisoft Connect", "育碧Connect"), ("Battle.net", "战网"), ("GOG Galaxy", "GOG银河"), ("Riot Client", "Riot客户端"), ("Rockstar Games Launcher", "Rockstar游戏启动器"), ("Amazon Games", "亚马逊游戏"), ("HoYoPlay", "米哈游启动器"), ("Discord", "迪斯科德"), ("WhatsApp", "瓦次艾普"), ("Telegram", "电报"), ("Zoom", "Zoom会议"), ("Microsoft Teams", "微软团队"), ("Minecraft Launcher", "我的世界启动器"), ("Spotify", "声田"), ("VS Code", "VS代码"), ("Google Chrome", "谷歌浏览器"), ("Microsoft Edge", "微软Edge"), ("Mozilla Firefox", "火狐浏览器"), ("Opera GX", "欧朋GX"), ("Xbox", "微软Xbox"), ("ChatGPT", "聊天GPT")),
            ["ko"] = CreateAppNames(("Steam", "스팀"), ("Epic Games", "에픽게임즈"), ("EA App", "EA 앱"), ("Ubisoft Connect", "유비소프트 커넥트"), ("Battle.net", "배틀넷"), ("GOG Galaxy", "GOG 갤럭시"), ("Riot Client", "라이엇 클라이언트"), ("Rockstar Games Launcher", "락스타 게임즈 런처"), ("Amazon Games", "아마존 게임즈"), ("HoYoPlay", "호요플레이"), ("Discord", "디스코드"), ("WhatsApp", "왓츠앱"), ("Telegram", "텔레그램"), ("Zoom", "줌"), ("Microsoft Teams", "마이크로소프트 팀즈"), ("Minecraft Launcher", "마인크래프트 런처"), ("Spotify", "스포티파이"), ("VS Code", "VS 코드"), ("Google Chrome", "구글 크롬"), ("Microsoft Edge", "마이크로소프트 엣지"), ("Mozilla Firefox", "모질라 파이어폭스"), ("Opera GX", "오페라 GX"), ("Xbox", "엑스박스"), ("ChatGPT", "챗GPT")),
            ["hi"] = CreateAppNames(("Steam", "स्टीम"), ("Epic Games", "एपिक गेम्स"), ("EA App", "ईए ऐप"), ("Ubisoft Connect", "यूबीसॉफ्ट कनेक्ट"), ("Battle.net", "बैटल.नेट"), ("GOG Galaxy", "जीओजी गैलेक्सी"), ("Riot Client", "रायट क्लाइंट"), ("Rockstar Games Launcher", "रॉकस्टार गेम्स लॉन्चर"), ("Amazon Games", "अमेज़न गेम्स"), ("HoYoPlay", "होयोप्ले"), ("Discord", "डिस्कॉर्ड"), ("WhatsApp", "व्हाट्सऐप"), ("Telegram", "टेलीग्राम"), ("Zoom", "ज़ूम"), ("Microsoft Teams", "माइक्रोसॉफ्ट टीम्स"), ("Minecraft Launcher", "माइनक्राफ्ट लॉन्चर"), ("Spotify", "स्पॉटिफ़ाई"), ("VS Code", "वीएस कोड"), ("Google Chrome", "गूगल क्रोम"), ("Microsoft Edge", "माइक्रोसॉफ्ट एज"), ("Mozilla Firefox", "मोज़िला फ़ायरफ़ॉक्स"), ("Opera GX", "ओपेरा जीएक्स"), ("Xbox", "एक्सबॉक्स"), ("ChatGPT", "चैटजीपीटी")),
            ["th"] = CreateAppNames(("Steam", "สตีม"), ("Epic Games", "เอพิกเกมส์"), ("EA App", "แอป EA"), ("Ubisoft Connect", "ยูบิซอฟต์คอนเน็กต์"), ("Battle.net", "แบตเทิลเน็ต"), ("GOG Galaxy", "จีโอโกกาแล็กซี"), ("Riot Client", "ไคลเอนต์ไรออต"), ("Rockstar Games Launcher", "ร็อกสตาร์เกมส์ลอนเชอร์"), ("Amazon Games", "อเมซอนเกมส์"), ("HoYoPlay", "โฮโยเพลย์"), ("Discord", "ดิสคอร์ด"), ("WhatsApp", "วอตส์แอป"), ("Telegram", "เทเลแกรม"), ("Zoom", "ซูม"), ("Microsoft Teams", "ไมโครซอฟต์ทีมส์"), ("Minecraft Launcher", "มายคราฟต์ลอนเชอร์"), ("Spotify", "สปอติฟาย"), ("VS Code", "วีเอสโค้ด"), ("Google Chrome", "กูเกิลโครม"), ("Microsoft Edge", "ไมโครซอฟต์เอดจ์"), ("Mozilla Firefox", "มอซิลลาไฟร์ฟอกซ์"), ("Opera GX", "โอเปรา GX"), ("Xbox", "เอ็กซ์บ็อกซ์"), ("ChatGPT", "แชตจีพีที")),
            ["uk"] = CreateAppNames(("Steam", "Стим"), ("Epic Games", "Епік Геймс"), ("EA App", "Застосунок EA"), ("Ubisoft Connect", "Юбісофт Коннект"), ("Battle.net", "Батл.нет"), ("GOG Galaxy", "ГОГ Галаксі"), ("Riot Client", "Клієнт Райот"), ("Rockstar Games Launcher", "Лаунчер Рокстар Геймс"), ("Amazon Games", "Ігри Амазон"), ("HoYoPlay", "ХоЙоПлей"), ("Discord", "Дискорд"), ("WhatsApp", "Вотсап"), ("Telegram", "Телеграм"), ("Zoom", "Зум"), ("Microsoft Teams", "Майкрософт Тімс"), ("Minecraft Launcher", "Лаунчер Майнкрафт"), ("Spotify", "Спотіфай"), ("VS Code", "ВС Код"), ("Google Chrome", "Гугл Хром"), ("Microsoft Edge", "Майкрософт Едж"), ("Mozilla Firefox", "Мозілла Файрфокс"), ("Opera GX", "Опера ГХ"), ("Xbox", "Іксбокс"), ("ChatGPT", "ЧатГПТ")),
            ["sr"] = CreateAppNames(("Steam", "Стим"), ("Epic Games", "Епик Гејмс"), ("EA App", "EA апликација"), ("Ubisoft Connect", "Јубисофт Конект"), ("Battle.net", "Батл.нет"), ("GOG Galaxy", "ГОГ Галакси"), ("Riot Client", "Рајот клијент"), ("Rockstar Games Launcher", "Рокстар Гејмс покретач"), ("Amazon Games", "Амазон Гејмс"), ("HoYoPlay", "ХоЈоПлеј"), ("Discord", "Дискорд"), ("WhatsApp", "Вацап"), ("Telegram", "Телеграм"), ("Zoom", "Зум"), ("Microsoft Teams", "Мајкрософт Тимс"), ("Minecraft Launcher", "Мајнкрафт покретач"), ("Spotify", "Спотифај"), ("VS Code", "ВС Код"), ("Google Chrome", "Гугл Хром"), ("Microsoft Edge", "Мајкрософт Еџ"), ("Mozilla Firefox", "Мозила Фајерфокс"), ("Opera GX", "Опера ГХ"), ("Xbox", "Иксбокс"), ("ChatGPT", "ЧетГПТ")),
            ["ar"] = CreateAppNames(("Steam", "ستيم"), ("Epic Games", "إيبك جيمز"), ("EA App", "تطبيق إي إيه"), ("Ubisoft Connect", "يوبيسوفت كونكت"), ("Battle.net", "باتل.نت"), ("GOG Galaxy", "جوج جالاكسي"), ("Riot Client", "رايوت كلاينت"), ("Rockstar Games Launcher", "مشغل روكستار"), ("Amazon Games", "ألعاب أمازون"), ("HoYoPlay", "هويو بلاي"), ("Discord", "ديسكورد"), ("WhatsApp", "واتساب"), ("Telegram", "تيليجرام"), ("Zoom", "زوم"), ("Microsoft Teams", "مايكروسوفت تيمز"), ("Minecraft Launcher", "مشغل ماينكرافت"), ("Spotify", "سبوتيفاي"), ("VS Code", "في إس كود"), ("Google Chrome", "جوجل كروم"), ("Microsoft Edge", "مايكروسوفت إيدج"), ("Mozilla Firefox", "موزيلا فايرفوكس"), ("Opera GX", "أوبرا جي إكس"), ("Xbox", "إكس بوكس"), ("ChatGPT", "شات جي بي تي")),
            ["he"] = CreateAppNames(("Steam", "סטים"), ("Epic Games", "אפיק גיימס"), ("EA App", "אפליקציית EA"), ("Ubisoft Connect", "יוביסופט קונקט"), ("Battle.net", "באטל.נט"), ("GOG Galaxy", "גוג גלקסי"), ("Riot Client", "לקוח ריוט"), ("Rockstar Games Launcher", "לאנצ׳ר רוקסטאר"), ("Amazon Games", "אמזון גיימס"), ("HoYoPlay", "הויו פליי"), ("Discord", "דיסקורד"), ("WhatsApp", "וואטסאפ"), ("Telegram", "טלגרם"), ("Zoom", "זום"), ("Microsoft Teams", "מיקרוסופט טימס"), ("Minecraft Launcher", "לאנצ׳ר מיינקראפט"), ("Spotify", "ספוטיפיי"), ("VS Code", "וי אס קוד"), ("Google Chrome", "גוגל כרום"), ("Microsoft Edge", "מיקרוסופט אדג׳"), ("Mozilla Firefox", "מוזילה פיירפוקס"), ("Opera GX", "אופרה ג׳י אקס"), ("Xbox", "אקסבוקס"), ("ChatGPT", "צ׳אט ג׳י פי טי")),
            ["bn"] = CreateAppNames(("Steam", "স্টিম"), ("Epic Games", "এপিক গেমস"), ("EA App", "ইএ অ্যাপ"), ("Ubisoft Connect", "ইউবিসফট কানেক্ট"), ("Battle.net", "ব্যাটল.নেট"), ("GOG Galaxy", "জিওজি গ্যালাক্সি"), ("Riot Client", "রায়ট ক্লায়েন্ট"), ("Rockstar Games Launcher", "রকস্টার গেমস লঞ্চার"), ("Amazon Games", "অ্যামাজন গেমস"), ("HoYoPlay", "হোয়োপ্লে"), ("Discord", "ডিসকর্ড"), ("WhatsApp", "হোয়াটসঅ্যাপ"), ("Telegram", "টেলিগ্রাম"), ("Zoom", "জুম"), ("Microsoft Teams", "মাইক্রোসফট টিমস"), ("Minecraft Launcher", "মাইনক্রাফ্ট লঞ্চার"), ("Spotify", "স্পটিফাই"), ("VS Code", "ভিএস কোড"), ("Google Chrome", "গুগল ক্রোম"), ("Microsoft Edge", "মাইক্রোসফট এজ"), ("Mozilla Firefox", "মজিলা ফায়ারফক্স"), ("Opera GX", "অপেরা জিএক্স"), ("Xbox", "এক্সবক্স"), ("ChatGPT", "চ্যাটজিপিটি")),
            ["ur"] = CreateAppNames(("Steam", "اسٹیم"), ("Epic Games", "ایپک گیمز"), ("EA App", "ای اے ایپ"), ("Ubisoft Connect", "یوبی سوفٹ کنیکٹ"), ("Battle.net", "بیٹل.نیٹ"), ("GOG Galaxy", "جی او جی گلیکسی"), ("Riot Client", "رائٹ کلائنٹ"), ("Rockstar Games Launcher", "راک اسٹار گیمز لانچر"), ("Amazon Games", "ایمیزون گیمز"), ("HoYoPlay", "ہویو پلے"), ("Discord", "ڈسکارڈ"), ("WhatsApp", "واٹس ایپ"), ("Telegram", "ٹیلیگرام"), ("Zoom", "زوم"), ("Microsoft Teams", "مائیکروسافٹ ٹیمز"), ("Minecraft Launcher", "مائن کرافٹ لانچر"), ("Spotify", "اسپاٹیفائی"), ("VS Code", "وی ایس کوڈ"), ("Google Chrome", "گوگل کروم"), ("Microsoft Edge", "مائیکروسافٹ ایج"), ("Mozilla Firefox", "موزیلا فائر فاکس"), ("Opera GX", "اوپیرا جی ایکس"), ("Xbox", "ایکس باکس"), ("ChatGPT", "چیٹ جی پی ٹی")),
            ["fa"] = CreateAppNames(("Steam", "استیم"), ("Epic Games", "اپیک گیمز"), ("EA App", "برنامه EA"), ("Ubisoft Connect", "یوبی‌سافت کانکت"), ("Battle.net", "بتل.نت"), ("GOG Galaxy", "جی‌او‌جی گلکسی"), ("Riot Client", "رایوت کلاینت"), ("Rockstar Games Launcher", "لانچر راک‌استار"), ("Amazon Games", "بازی‌های آمازون"), ("HoYoPlay", "هویوپلی"), ("Discord", "دیسکورد"), ("WhatsApp", "واتساپ"), ("Telegram", "تلگرام"), ("Zoom", "زوم"), ("Microsoft Teams", "مایکروسافت تیمز"), ("Minecraft Launcher", "لانچر ماینکرفت"), ("Spotify", "اسپاتیفای"), ("VS Code", "وی‌اس‌کد"), ("Google Chrome", "گوگل کروم"), ("Microsoft Edge", "مایکروسافت اج"), ("Mozilla Firefox", "موزیلا فایرفاکس"), ("Opera GX", "اپرا جی‌ایکس"), ("Xbox", "ایکس‌باکس"), ("ChatGPT", "چت‌جی‌پی‌تی"))
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SettingsTranslations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["tr"] = CreateSettingsTranslations("Temalar", "Diller", "Eklentiler", "YAKINDA"),
            ["en"] = CreateSettingsTranslations("Themes", "Languages", "Plugins", "COMING SOON"),
            ["es"] = CreateSettingsTranslations("Temas", "Idiomas", "Complementos", "PRÓXIMAMENTE"),
            ["de"] = CreateSettingsTranslations("Designs", "Sprachen", "Erweiterungen", "DEMNÄCHST"),
            ["fr"] = CreateSettingsTranslations("Thèmes", "Langues", "Extensions", "BIENTÔT"),
            ["ru"] = CreateSettingsTranslations("Темы", "Языки", "Плагины", "СКОРО"),
            ["ja"] = CreateSettingsTranslations("テーマ", "言語", "プラグイン", "近日公開"),
            ["it"] = CreateSettingsTranslations("Temi", "Lingue", "Plugin", "PROSSIMAMENTE"),
            ["pt"] = CreateSettingsTranslations("Temas", "Idiomas", "Plugins", "EM BREVE"),
            ["nl"] = CreateSettingsTranslations("Thema's", "Talen", "Plug-ins", "BINNENKORT"),
            ["pl"] = CreateSettingsTranslations("Motywy", "Języki", "Wtyczki", "WKRÓTCE"),
            ["ar"] = CreateSettingsTranslations("السمات", "اللغات", "الإضافات", "قريبًا"),
            ["zh"] = CreateSettingsTranslations("主题", "语言", "插件", "即将推出"),
            ["ko"] = CreateSettingsTranslations("테마", "언어", "플러그인", "출시 예정"),
            ["hi"] = CreateSettingsTranslations("थीम", "भाषाएँ", "प्लगइन", "जल्द आ रहा है"),
            ["id"] = CreateSettingsTranslations("Tema", "Bahasa", "Plugin", "SEGERA HADIR"),
            ["ms"] = CreateSettingsTranslations("Tema", "Bahasa", "Pemalam", "AKAN DATANG"),
            ["th"] = CreateSettingsTranslations("ธีม", "ภาษา", "ส่วนเสริม", "เร็ว ๆ นี้"),
            ["vi"] = CreateSettingsTranslations("Giao diện", "Ngôn ngữ", "Tiện ích", "SẮP RA MẮT"),
            ["sv"] = CreateSettingsTranslations("Teman", "Språk", "Tillägg", "KOMMER SNART"),
            ["no"] = CreateSettingsTranslations("Temaer", "Språk", "Utvidelser", "KOMMER SNART"),
            ["da"] = CreateSettingsTranslations("Temaer", "Sprog", "Udvidelser", "KOMMER SNART"),
            ["fi"] = CreateSettingsTranslations("Teemat", "Kielet", "Lisäosat", "TULOSSA PIAN"),
            ["el"] = CreateSettingsTranslations("Θέματα", "Γλώσσες", "Πρόσθετα", "ΣΥΝΤΟΜΑ"),
            ["cs"] = CreateSettingsTranslations("Motivy", "Jazyky", "Doplňky", "Již brzy"),
            ["sk"] = CreateSettingsTranslations("Témy", "Jazyky", "Doplnky", "Už čoskoro"),
            ["hu"] = CreateSettingsTranslations("Témák", "Nyelvek", "Bővítmények", "HAMAROSAN"),
            ["ro"] = CreateSettingsTranslations("Teme", "Limbi", "Extensii", "ÎN CURÂND"),
            ["bg"] = CreateSettingsTranslations("Теми", "Езици", "Разширения", "ОЧАКВАЙТЕ СКОРО"),
            ["uk"] = CreateSettingsTranslations("Теми", "Мови", "Розширення", "СКОРО"),
            ["hr"] = CreateSettingsTranslations("Teme", "Jezici", "Dodaci", "USKORO"),
            ["sr"] = CreateSettingsTranslations("Теме", "Језици", "Додаци", "УСКОРО"),
            ["he"] = CreateSettingsTranslations("ערכות נושא", "שפות", "תוספים", "בקרוב"),
            ["bn"] = CreateSettingsTranslations("থিম", "ভাষা", "প্লাগইন", "শীঘ্রই আসছে"),
            ["ur"] = CreateSettingsTranslations("تھیمز", "زبانیں", "پلگ ان", "جلد آرہا ہے"),
            ["fa"] = CreateSettingsTranslations("پوسته‌ها", "زبان‌ها", "افزونه‌ها", "به‌زودی"),
            ["sw"] = CreateSettingsTranslations("Mandhari", "Lugha", "Programu-jalizi", "INAKUJA HIVI KARIBUNI"),
            ["et"] = CreateSettingsTranslations("Teemad", "Keeled", "Pluginad", "TULEKUL"),
            ["lv"] = CreateSettingsTranslations("Motīvi", "Valodas", "Spraudņi", "DRĪZUMĀ"),
            ["lt"] = CreateSettingsTranslations("Temos", "Kalbos", "Papildiniai", "JAU NETRUKUS")
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AppActionTranslations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["tr"] = CreateAppActionTranslations("YÖNETİCİ OLARAK ÇALIŞTIR", "EKRANDAN GİZLE", "GİZLİ UYGULAMALAR", "GERİ YÜKLE", "TÜMÜNÜ GERİ YÜKLE"),
            ["en"] = CreateAppActionTranslations("RUN AS ADMINISTRATOR", "HIDE FROM SCREEN", "HIDDEN APPS", "RESTORE", "RESTORE ALL"),
            ["es"] = CreateAppActionTranslations("EJECUTAR COMO ADMINISTRADOR", "OCULTAR DE LA PANTALLA", "APPS OCULTAS", "RESTAURAR", "RESTAURAR TODO"),
            ["de"] = CreateAppActionTranslations("ALS ADMINISTRATOR AUSFÜHREN", "AUF DEM BILDSCHIRM AUSBLENDEN", "AUSGEBLENDETE APPS", "WIEDERHERSTELLEN", "ALLE WIEDERHERSTELLEN"),
            ["fr"] = CreateAppActionTranslations("EXÉCUTER EN TANT QU’ADMINISTRATEUR", "MASQUER DE L’ÉCRAN", "APPS MASQUÉES", "RESTAURER", "TOUT RESTAURER"),
            ["ru"] = CreateAppActionTranslations("ЗАПУСТИТЬ ОТ ИМЕНИ АДМИНИСТРАТОРА", "СКРЫТЬ С ЭКРАНА", "СКРЫТЫЕ ПРИЛОЖЕНИЯ", "ВОССТАНОВИТЬ", "ВОССТАНОВИТЬ ВСЕ"),
            ["ja"] = CreateAppActionTranslations("管理者として実行", "画面から非表示", "非表示のアプリ", "復元", "すべて復元"),
            ["it"] = CreateAppActionTranslations("ESEGUI COME AMMINISTRATORE", "NASCONDI DALLO SCHERMO", "APP NASCOSTE", "RIPRISTINA", "RIPRISTINA TUTTO"),
            ["pt"] = CreateAppActionTranslations("EXECUTAR COMO ADMINISTRADOR", "OCULTAR DA TELA", "APPS OCULTOS", "RESTAURAR", "RESTAURAR TUDO"),
            ["nl"] = CreateAppActionTranslations("UITVOEREN ALS BEHEERDER", "VERBERGEN OP HET SCHERM", "VERBORGEN APPS", "HERSTELLEN", "ALLES HERSTELLEN"),
            ["pl"] = CreateAppActionTranslations("URUCHOM JAKO ADMINISTRATOR", "UKRYJ NA EKRANIE", "UKRYTE APLIKACJE", "PRZYWRÓĆ", "PRZYWRÓĆ WSZYSTKIE"),
            ["ar"] = CreateAppActionTranslations("تشغيل كمسؤول", "إخفاء من الشاشة", "التطبيقات المخفية", "استعادة", "استعادة الكل"),
            ["zh"] = CreateAppActionTranslations("以管理员身份运行", "从屏幕隐藏", "隐藏的应用", "恢复", "全部恢复"),
            ["ko"] = CreateAppActionTranslations("관리자 권한으로 실행", "화면에서 숨기기", "숨긴 앱", "복원", "모두 복원"),
            ["hi"] = CreateAppActionTranslations("व्यवस्थापक के रूप में चलाएँ", "स्क्रीन से छिपाएँ", "छिपे हुए ऐप", "पुनर्स्थापित करें", "सभी पुनर्स्थापित करें"),
            ["id"] = CreateAppActionTranslations("JALANKAN SEBAGAI ADMINISTRATOR", "SEMBUNYIKAN DARI LAYAR", "APLIKASI TERSEMBUNYI", "PULIHKAN", "PULIHKAN SEMUA"),
            ["ms"] = CreateAppActionTranslations("JALANKAN SEBAGAI PENTADBIR", "SEMBUNYIKAN DARIPADA SKRIN", "APLIKASI TERSEMBUNYI", "PULIHKAN", "PULIHKAN SEMUA"),
            ["th"] = CreateAppActionTranslations("เรียกใช้ในฐานะผู้ดูแลระบบ", "ซ่อนจากหน้าจอ", "แอปที่ซ่อน", "คืนค่า", "คืนค่าทั้งหมด"),
            ["vi"] = CreateAppActionTranslations("CHẠY VỚI QUYỀN QUẢN TRỊ", "ẨN KHỎI MÀN HÌNH", "ỨNG DỤNG ĐÃ ẨN", "KHÔI PHỤC", "KHÔI PHỤC TẤT CẢ"),
            ["sv"] = CreateAppActionTranslations("KÖR SOM ADMINISTRATÖR", "DÖLJ FRÅN SKÄRMEN", "DOLDA APPAR", "ÅTERSTÄLL", "ÅTERSTÄLL ALLA"),
            ["no"] = CreateAppActionTranslations("KJØR SOM ADMINISTRATOR", "SKJUL FRA SKJERMEN", "SKJULTE APPER", "GJENOPPRETT", "GJENOPPRETT ALLE"),
            ["da"] = CreateAppActionTranslations("KØR SOM ADMINISTRATOR", "SKJUL FRA SKÆRMEN", "SKJULTE APPS", "GENDAN", "GENDAN ALLE"),
            ["fi"] = CreateAppActionTranslations("SUORITA JÄRJESTELMÄNVALVOJANA", "PIILOTA NÄYTÖLTÄ", "PIILOTETUT SOVELLUKSET", "PALAUTA", "PALAUTA KAIKKI"),
            ["el"] = CreateAppActionTranslations("ΕΚΤΕΛΕΣΗ ΩΣ ΔΙΑΧΕΙΡΙΣΤΗΣ", "ΑΠΟΚΡΥΨΗ ΑΠΟ ΤΗΝ ΟΘΟΝΗ", "ΚΡΥΦΕΣ ΕΦΑΡΜΟΓΕΣ", "ΕΠΑΝΑΦΟΡΑ", "ΕΠΑΝΑΦΟΡΑ ΟΛΩΝ"),
            ["cs"] = CreateAppActionTranslations("SPUSTIT JAKO SPRÁVCE", "SKRÝT Z OBRAZOVKY", "SKRYTÉ APLIKACE", "OBNOVIT", "OBNOVIT VŠE"),
            ["sk"] = CreateAppActionTranslations("SPUSTIŤ AKO SPRÁVCA", "SKRYŤ Z OBRAZOVKY", "SKRYTÉ APLIKÁCIE", "OBNOVIŤ", "OBNOVIŤ VŠETKO"),
            ["hu"] = CreateAppActionTranslations("FUTTATÁS RENDSZERGAZDAKÉNT", "ELREJTÉS A KÉPERNYŐRŐL", "REJTETT ALKALMAZÁSOK", "VISSZAÁLLÍTÁS", "ÖSSZES VISSZAÁLLÍTÁSA"),
            ["ro"] = CreateAppActionTranslations("RULEAZĂ CA ADMINISTRATOR", "ASCUNDE DE PE ECRAN", "APLICAȚII ASCUNSE", "RESTABILEȘTE", "RESTABILEȘTE TOT"),
            ["bg"] = CreateAppActionTranslations("СТАРТИРАНЕ КАТО АДМИНИСТРАТОР", "СКРИВАНЕ ОТ ЕКРАНА", "СКРИТИ ПРИЛОЖЕНИЯ", "ВЪЗСТАНОВЯВАНЕ", "ВЪЗСТАНОВЯВАНЕ НА ВСИЧКИ"),
            ["uk"] = CreateAppActionTranslations("ЗАПУСТИТИ ВІД ІМЕНІ АДМІНІСТРАТОРА", "ПРИХОВАТИ З ЕКРАНА", "ПРИХОВАНІ ПРОГРАМИ", "ВІДНОВИТИ", "ВІДНОВИТИ ВСІ"),
            ["hr"] = CreateAppActionTranslations("POKRENI KAO ADMINISTRATOR", "SAKRIJ S EKRANA", "SKRIVENE APLIKACIJE", "VRATI", "VRATI SVE"),
            ["sr"] = CreateAppActionTranslations("ПОКРЕНИ КАО АДМИНИСТРАТОР", "САКРИЈ СА ЕКРАНА", "СКРИВЕНЕ АПЛИКАЦИЈЕ", "ВРАТИ", "ВРАТИ СВЕ"),
            ["he"] = CreateAppActionTranslations("הפעל כמנהל מערכת", "הסתר מהמסך", "אפליקציות מוסתרות", "שחזר", "שחזר הכול"),
            ["bn"] = CreateAppActionTranslations("প্রশাসক হিসেবে চালান", "স্ক্রিন থেকে লুকান", "লুকানো অ্যাপ", "পুনরুদ্ধার", "সব পুনরুদ্ধার"),
            ["ur"] = CreateAppActionTranslations("بطور منتظم چلائیں", "اسکرین سے چھپائیں", "چھپی ہوئی ایپس", "بحال کریں", "سب بحال کریں"),
            ["fa"] = CreateAppActionTranslations("اجرا به‌عنوان مدیر", "پنهان‌کردن از صفحه", "برنامه‌های پنهان", "بازیابی", "بازیابی همه"),
            ["sw"] = CreateAppActionTranslations("ENDESHA KAMA MSIMAMIZI", "FICHA KWENYE SKRINI", "PROGRAMU ZILIZOFICHWA", "REJESHA", "REJESHA ZOTE"),
            ["et"] = CreateAppActionTranslations("KÄIVITA ADMINISTRAATORINA", "PEIDA EKRAANILT", "PEIDETUD RAKENDUSED", "TAASTA", "TAASTA KÕIK"),
            ["lv"] = CreateAppActionTranslations("PALAIST KĀ ADMINISTRATORAM", "PASLĒPT EKRĀNĀ", "SLĒPTĀS LIETOTNES", "ATJAUNOT", "ATJAUNOT VISU"),
            ["lt"] = CreateAppActionTranslations("PALEISTI KAIP ADMINISTRATORIUS", "SLĖPTI EKRANE", "PASLĖPTOS PROGRAMOS", "ATKURTI", "ATKURTI VISKĄ")
        };

    private static IReadOnlyDictionary<string, string> CreateSettingsTranslations(string themes, string languages, string plugins, string comingSoon) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SettingsThemes"] = themes,
            ["SettingsLanguages"] = languages,
            ["SettingsPlugins"] = plugins,
            ["SettingsComingSoon"] = comingSoon
        };

    private static IReadOnlyDictionary<string, string> CreateAppActionTranslations(
        string runAsAdministrator,
        string hideApp,
        string hiddenApps,
        string restoreApp,
        string restoreAllApps) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RunAsAdministrator"] = runAsAdministrator,
            ["HideApp"] = hideApp,
            ["HiddenApps"] = hiddenApps,
            ["RestoreApp"] = restoreApp,
            ["RestoreAllApps"] = restoreAllApps
        };

    private static IReadOnlyDictionary<string, string> CreateAppNames(params (string Original, string Localized)[] names) =>
        names.ToDictionary(name => name.Original, name => name.Localized, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> CreateAdditionalTranslations(
        string title,
        string detected,
        string body,
        string ok)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["InfoTitle"] = $"K-LAUNCHER 4.0  //  {title.ToUpperInvariant()}",
            ["AutoDetected"] = detected,
            ["InfoBody"] = body,
            ["Ok"] = ok
        };
    }

    private static IReadOnlyDictionary<string, string> CreateCommonTranslations(params string[] values)
    {
        if (values.Length != CommonTranslationKeys.Length)
            throw new ArgumentException("Common translation value count does not match its keys.", nameof(values));

        return CommonTranslationKeys
            .Select((key, index) => new KeyValuePair<string, string>(key, values[index]))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private string _languageCode = "tr";

    // Services that are not part of the UI (store automation, Riot) read
    // their messages through this instance.
    public static LocalizationService? Current { get; private set; }

    // Languages written right to left; the windows mirror their layout.
    private static readonly HashSet<string> RightToLeftLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "ar", "he", "fa", "ur", "ps", "sd", "ug", "dv", "yi" };

    public bool IsRightToLeft => RightToLeftLanguages.Contains(_languageCode);

    public System.Windows.FlowDirection FlowDirection => IsRightToLeft
        ? System.Windows.FlowDirection.RightToLeft
        : System.Windows.FlowDirection.LeftToRight;

    /// <summary>Text for <paramref name="key"/> in the current language, formatted with <paramref name="args"/>.</summary>
    public static string Text(string key, params object?[] args)
    {
        var template = Current?[key] ?? key;
        return args.Length == 0 ? template : string.Format(template, args);
    }

    public LocalizationService()
    {
        Current = this;
        _languageCode = DetectSystemLanguage();

        try
        {
            var savedCode = File.ReadAllText(SettingsPath).Trim();
            if (IsLanguageSupported(savedCode))
                _languageCode = savedCode;
        }
        catch
        {
            // First launch or unavailable preferences: keep Turkish as the default.
        }
    }

    private static string DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        if (culture.Equals(CultureInfo.InvariantCulture))
            culture = CultureInfo.InstalledUICulture;

        var languageCode = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (!IsLanguageSupported(languageCode))
        {
            var currentLanguageCode = CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToLowerInvariant();
            languageCode = IsLanguageSupported(currentLanguageCode) ? currentLanguageCode : "en";
        }

        return languageCode;
    }

    public static IReadOnlyList<LanguageOption> Languages => SupportedLanguages;

    public string LanguageCode => _languageCode;

    public string GetAppDisplayName(string name)
    {
        string? baseLocalizedName = null;
        if (LocalizedAppNames.TryGetValue(_languageCode, out var names) &&
            names.TryGetValue(name, out var localizedName))
        {
            baseLocalizedName = localizedName;
        }

        // A base map can contain an unchanged English brand name while the
        // supplemental map has the localized spelling. Do not stop at the
        // unchanged entry in that case.
        if (ExtendedLocalizedAppNames.TryGet(_languageCode, name, out var supplementalName) &&
            !string.Equals(supplementalName, name, StringComparison.OrdinalIgnoreCase))
        {
            return supplementalName;
        }

        return baseLocalizedName ?? name;
    }

    // Steam/Epic/Riot automation messages (progress lines and results).
    private static readonly Dictionary<string, (string Tr, string En)> AutomationTexts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AutoSteamSearching"] = ("Steam: \"{0}\" aranıyor…", "Steam: searching for \"{0}\"…"),
            ["AutoSteamNoAppId"] = ("Steam: \"{0}\" için AppID bulunamadı, arayüz yöntemine geçiliyor.", "Steam: no AppID found for \"{0}\", switching to the interface method."),
            ["AutoSteamFound"] = ("Steam: {0} bulundu (AppID {1}).", "Steam: found {0} (AppID {1})."),
            ["AutoSteamAlreadyInstalled"] = ("{0} zaten kurulu (ya da Steam'de indiriliyor).", "{0} is already installed (or downloading in Steam)."),
            ["AutoSteamOpening"] = ("Steam: steam://install/{0} açılıyor.", "Steam: opening steam://install/{0}."),
            ["AutoSteamLinkFailed"] = ("Steam kurulum bağlantısı açılamadı. Steam kurulu mu?", "Could not open the Steam install link. Is Steam installed?"),
            ["AutoSteamQueued"] = ("Steam: {0} kuyruğa girdi.", "Steam: {0} is queued."),
            ["AutoSteamStarted"] = ("{0} için Steam indirmesi başlatıldı.", "Steam download started for {0}."),
            ["AutoSteamNoDialog"] = ("Steam: {0} için onay penceresi bulunamadı, mağaza sayfası açılıyor.", "Steam: no confirmation window for {0}, opening the store page."),
            ["AutoSteamNoWindow"] = ("{0} için Steam kurulum penceresi açılmadı. Oyun hesabında olmayabilir; mağaza sayfası açıldı.", "The Steam install window did not open for {0}. The game may not be in your account; the store page was opened."),
            ["AutoEpicSearching"] = ("Epic: \"{0}\" katalogda aranıyor…", "Epic: searching the catalog for \"{0}\"…"),
            ["AutoEpicNoCatalog"] = ("Epic: katalog önbelleği bulunamadı, arayüz yöntemine geçiliyor.", "Epic: catalog cache not found, switching to the interface method."),
            ["AutoEpicNotInLibrary"] = ("Epic: \"{0}\" kütüphanede bulunamadı.", "Epic: \"{0}\" was not found in the library."),
            ["AutoEpicFound"] = ("Epic: {0} bulundu ({1}).", "Epic: found {0} ({1})."),
            ["AutoAlreadyInstalled"] = ("{0} zaten kurulu.", "{0} is already installed."),
            ["AutoEpicLinkFailed"] = ("Epic kurulum bağlantısı açılamadı. Epic Games Launcher kurulu mu?", "Could not open the Epic install link. Is the Epic Games Launcher installed?"),
            ["AutoEpicStarted"] = ("{0} için Epic indirmesi başlatıldı.", "Epic download started for {0}."),
            ["AutoEpicNoWindow"] = ("{0} için Epic kurulum penceresi açılmadı. Epic penceresinden kurulumu onaylayabilirsin.", "The Epic install window did not open for {0}. You can confirm the install in the Epic window."),
            ["AutoWaitingConfirm"] = ("{0} onay penceresi bekleniyor… {1} sn", "Waiting for the {0} confirmation window… {1} s"),
            ["AutoConfirmed"] = ("{0}: \"{1}\" onaylandı", "{0}: \"{1}\" confirmed"),
            ["AutoNameEmpty"] = ("Oyun adı boş olamaz.", "The game name cannot be empty."),
            ["AutoSearchingUi"] = ("{0} arayüzünde aranıyor…", "Searching in the {0} interface…"),
            ["AutoLauncherMissing"] = ("{0} launcher'ı bulunamadı. Önce resmi launcher'ı kurup oturum açmalısın.", "{0} launcher not found. Install the official launcher and sign in first."),
            ["AutoOpenFailed"] = ("{0} açılamadı: {1}", "Could not open {0}: {1}"),
            ["AutoStartFailed"] = ("{0} başlatılamadı.", "Could not start {0}."),
            ["AutoWindowMissing"] = ("{0} penceresi bulunamadı. Launcher'ın açık ve oturumun etkin olduğundan emin ol.", "{0} window not found. Make sure the launcher is open and you are signed in."),
            ["AutoSearchBoxMissing"] = ("{0} arama kutusu bulunamadı. Launcher arayüzü beklenenden farklı olabilir.", "{0} search box not found. The launcher interface may be different than expected."),
            ["AutoSelected"] = ("{0} {1} içinde seçildi.", "{0} selected in {1}."),
            ["AutoNoResult"] = ("{0} için {1} arama sonucu bulunamadı. Launcher arama sonuçlarını kontrol edebilirsin.", "No {1} search result for {0}. You can check the launcher's search results."),
            ["AutoInstallButtonMissing"] = ("{0} seçildi ancak yükleme düğmesi bulunamadı. {1} penceresinden işlemi tamamlayabilirsin.", "{0} was selected, but the install button was not found. You can finish in the {1} window."),
            ["AutoUninstallButtonMissing"] = ("{0} seçildi ancak kaldırma düğmesi bulunamadı. {1} penceresinden işlemi tamamlayabilirsin.", "{0} was selected, but the uninstall button was not found. You can finish in the {1} window."),
            ["AutoConfirmFailed"] = ("{0} için {1} kurulum penceresi onaylanamadı. Açılan pencereden \"Yükle\" düğmesine basabilirsin.", "Could not confirm the {1} install window for {0}. You can press \"Install\" in the window that opened."),
            ["AutoInstallStarted"] = ("{0} için yükleme işlemi başlatıldı.", "Install started for {0}."),
            ["AutoUninstallStarted"] = ("{0} için kaldırma işlemi başlatıldı.", "Uninstall started for {0}."),
            ["RiotServicesMissing"] = ("RiotClientServices.exe bulunamadı. Riot Client kurulumunu kontrol et.", "RiotClientServices.exe not found. Check your Riot Client installation."),
            ["RiotStartFailed"] = ("Riot Client başlatılamadı: {0}", "Could not start Riot Client: {0}"),
            ["RiotGameNotOpened"] = ("Riot Client başlatma isteğini kabul etti, ancak VALORANT süreci açılmadı. Riot Client oturumunu ve güncelleme durumunu kontrol et. Riot'un 2026 akışında oyun sayfasındaki Play düğmesi gerekebilir.", "Riot Client accepted the launch request, but the VALORANT process did not open. Check your Riot Client session and update status. In Riot's 2026 flow, the Play button on the game page may be needed."),
            ["RiotServiceSlowRunning"] = ("Riot Client açık, ancak yerel başlatma servisi 20 saniye içinde hazır olmadı. Riot Client'ı yeniden başlatıp tekrar dene.", "Riot Client is open, but its local launch service was not ready within 20 seconds. Restart Riot Client and try again."),
            ["RiotServiceSlow"] = ("Riot Client'ın yerel başlatma servisi zamanında hazır olmadı.", "Riot Client's local launch service was not ready in time."),
            ["RiotLockfileFormat"] = ("Riot Client lockfile biçimi beklenenden farklı. Riot Client'ı yeniden başlat.", "The Riot Client lockfile format is different than expected. Restart Riot Client."),
            ["RiotLockfileLocked"] = ("Riot Client lockfile'ı başka bir işlem tarafından kilitli; K-Launcher otomatik başlatma kimlik bilgisini okuyamadı.", "The Riot Client lockfile is locked by another process; K-Launcher could not read the auto-launch credentials."),
            ["RiotLockfileDenied"] = ("K-Launcher Riot Client lockfile'ına erişemedi. Riot Client'ı ve K-Launcher'ı aynı Windows kullanıcısıyla yeniden başlatıp tekrar dene.", "K-Launcher could not access the Riot Client lockfile. Restart Riot Client and K-Launcher under the same Windows user and try again."),
            ["RiotErrorCode"] = (" Riot hata kodu: {0}.", " Riot error code: {0}."),
            ["RiotRejected"] = ("Riot Client VALORANT başlatma isteğini reddetti (HTTP {0}).{1} Riot Client'ta oturum açıldığını ve oyunun güncel olduğunu kontrol et.", "Riot Client rejected the VALORANT launch request (HTTP {0}).{1} Check that you are signed in to Riot Client and the game is up to date.")
        };

    // Messages and labels that used to be written straight into the code.
    private static readonly Dictionary<string, (string Tr, string En)> MovedTexts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ThemeSaveFailed"] = ("Tema bu oturum için uygulandı ancak kaydedilemedi: {0}", "The theme was applied for this session but could not be saved: {0}"),
            ["ThemeShortcutIconFailed"] = ("Tema uygulandı ancak masaüstü kısayolunun simgesi güncellenemedi: {0}", "The theme was applied, but the desktop shortcut icon could not be updated: {0}"),
            ["AccentColors"] = ("VURGU RENKLERİ", "ACCENT COLORS"),
            ["StoreMenuSelect"] = ("MAĞAZADA SEÇ", "SELECT IN STORE"),
            ["StoreMenuInstall"] = ("YÜKLE", "INSTALL"),
            ["StoreMenuUninstall"] = ("KALDIR", "UNINSTALL"),
            ["StoreActionFailed"] = ("Mağaza işlemi tamamlanamadı.", "The store action could not be completed."),
            ["StoreActionStartFailed"] = ("{0} işlemi başlatılamadı:\n\n{1}", "Could not start the {0} action:\n\n{1}"),
            ["UnknownError"] = ("Bilinmeyen hata", "Unknown error"),
            ["BatchSucceeded"] = ("Başarılı: {0}", "Succeeded: {0}"),
            ["BatchFailed"] = ("Başarısız: {0}", "Failed: {0}"),
            ["ValorantLaunchFailed"] = ("VALORANT başlatılamadı: {0}", "Could not start VALORANT: {0}"),
            ["LaunchFailed"] = ("Başlatılamadı:\n\n{0}", "Could not start:\n\n{0}"),
            ["AlreadyRunning"] = ("K-Launcher zaten açık. Lütfen önce açık olan uygulamayı kapatın.", "K-Launcher is already running. Please close the open copy first."),
            ["StartupFailed"] = ("K-Launcher başlatılamadı.\n\n{0}", "K-Launcher could not start.\n\n{0}"),
            ["DialogYes"] = ("EVET", "YES"),
            ["DialogNo"] = ("HAYIR", "NO")
        };

    private static readonly Dictionary<string, (string Tr, string En)> TouchTexts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["TouchMenu"] = ("Dokunmatik ekran kodu", "Touch screen code"),
            ["TouchTitle"] = ("DOKUNMATİK EKRAN KODU", "TOUCH SCREEN CODE"),
            ["TouchSubtitle"] = (
                "ESP32 gibi kartlara bağlı ekranlar için dokunmatik kodunu hazırlar. Kartı ve dokunmatik çipi seç, kodu kopyala ya da Arduino IDE'de aç.",
                "Builds touch code for displays on ESP32-style boards. Pick the board and touch chip, then copy the code or open it in the Arduino IDE."),
            ["TouchPresets"] = ("HAZIR KARTLAR", "READY-MADE BOARDS"),
            ["TouchBoard"] = ("KART", "BOARD"),
            ["TouchChip"] = ("DOKUNMATİK ÇİP", "TOUCH CHIP"),
            ["TouchChipXpt"] = ("Dirençli (kalemle de çalışır) • SPI • en yaygın ucuz ekranlar", "Resistive (works with a stylus) • SPI • most cheap displays"),
            ["TouchChipFt"] = ("Kapasitif • I2C • Adafruit ve benzeri ekranlar", "Capacitive • I2C • Adafruit and similar displays"),
            ["TouchChipGt"] = ("Kapasitif • I2C • 4.3\" ve 7\" büyük ekranlar", "Capacitive • I2C • large 4.3\" and 7\" displays"),
            ["TouchChipCst"] = ("Kapasitif • I2C • küçük yuvarlak ekranlar", "Capacitive • I2C • small round displays"),
            ["TouchOutput"] = ("KOD TÜRÜ", "CODE TYPE"),
            ["TouchOutputSerial"] = ("Seri port testi", "Serial test"),
            ["TouchOutputTft"] = ("TFT_eSPI çizim örneği", "TFT_eSPI drawing demo"),
            ["TouchOutputLvgl"] = ("LVGL sürücüsü", "LVGL driver"),
            ["TouchScreen"] = ("EKRAN", "SCREEN"),
            ["TouchWidth"] = ("Genişlik", "Width"),
            ["TouchHeight"] = ("Yükseklik", "Height"),
            ["TouchRotation"] = ("Döndürme", "Rotation"),
            ["TouchPins"] = ("PİNLER", "PINS"),
            ["TouchPinNotUsed"] = ("-1 = bağlı değil", "-1 = not connected"),
            ["TouchOrientation"] = ("YÖN", "ORIENTATION"),
            ["TouchSeparateSpi"] = ("Dokunmatik ayrı SPI hattında", "Touch is on its own SPI bus"),
            ["TouchSwap"] = ("X ve Y yer değiştirsin", "Swap X and Y"),
            ["TouchInvertX"] = ("X ters", "Invert X"),
            ["TouchInvertY"] = ("Y ters", "Invert Y"),
            ["TouchOrientationHint"] = ("Dokunduğun yer ekranda ters ya da kaymış çıkarsa bu kutularla düzelt.", "If touches land mirrored or rotated, fix it with these boxes."),
            ["TouchLibraries"] = ("Gerekli kütüphaneler: {0}", "Required libraries: {0}"),
            ["TouchCopy"] = ("KOPYALA", "COPY"),
            ["TouchCopied"] = ("Kod panoya kopyalandı", "Code copied to the clipboard"),
            ["TouchCopyFailed"] = ("Panoya kopyalanamadı, tekrar dene", "Could not copy to the clipboard, try again"),
            ["TouchSave"] = (".INO OLARAK KAYDET", "SAVE AS .INO"),
            ["TouchSaved"] = ("Kaydedildi: {0}", "Saved: {0}"),
            ["TouchSaveFailed"] = ("Kaydedilemedi: {0}", "Could not save: {0}"),
            ["TouchOpenIde"] = ("ARDUINO IDE'DE AÇ", "OPEN IN ARDUINO IDE"),
            ["TouchInstallIde"] = ("ARDUINO IDE'Yİ KUR", "INSTALL ARDUINO IDE"),
            ["TouchInstallingIde"] = ("Arduino IDE kuruluyor…", "Installing Arduino IDE…"),
            ["TouchIdeInstalled"] = ("Arduino IDE kuruldu, şimdi kodu açabilirsin", "Arduino IDE installed, you can open the code now"),
            ["TouchIdeFailed"] = ("Arduino IDE kurulamadı: {0}", "Could not install Arduino IDE: {0}"),
            ["TouchFinder"] = ("OTOMATİK BUL", "FIND AUTOMATICALLY"),
            ["TouchFinderHint"] = (
                "Çipin ne olduğunu bilmiyorsan: 1) bulucu kodu karta yükle, 2) kartın portunu seçip dinle. Çip ve pinler kendiliğinden dolar.",
                "Not sure which chip you have? 1) Upload the finder sketch, 2) pick the board's port and listen. The chip and pins fill in by themselves."),
            ["TouchFinderShow"] = ("1. BULUCU KODU GÖSTER", "1. SHOW FINDER SKETCH"),
            ["TouchFinderShown"] = ("Bulucu kod hazır: KOPYALA ya da ARDUINO IDE'DE AÇ ile karta yükle.", "Finder sketch ready: upload it with COPY or OPEN IN ARDUINO IDE."),
            ["TouchFinderNoLibraries"] = ("Bulucu kod için ek kütüphane gerekmez.", "The finder sketch needs no extra libraries."),
            ["TouchFinderRefresh"] = ("Bağlı kartları yeniden ara", "Look for connected boards again"),
            ["TouchFinderListen"] = ("2. DİNLE VE AYARLA", "2. LISTEN AND SET UP"),
            ["TouchFinderStop"] = ("DİNLEMEYİ DURDUR", "STOP LISTENING"),
            ["TouchFinderNoPorts"] = ("Bağlı kart yok. Kartı USB ile tak ve ↻'ya bas.", "No board connected. Plug it in over USB and press ↻."),
            ["TouchFinderPickPort"] = ("Önce kartın bağlı olduğu portu seç.", "Pick the board's port first."),
            ["TouchFinderListening"] = ("{0} dinleniyor… Bulucu kod karttaysa sonuç birkaç saniyede gelir.", "Listening on {0}… If the finder sketch is on the board, the result arrives in a few seconds."),
            ["TouchFinderPress"] = ("Şimdi ekrana parmağınla basılı tut…", "Now press and hold the screen with your finger…"),
            ["TouchFinderFound"] = ("{0} bulundu ({1}). Ayarlar dolduruldu.", "Found {0} ({1}). Settings filled in."),
            ["TouchFinderUnsupported"] = ("Desteklenmeyen bir dokunmatik çip bulundu: {0}", "Found a touch chip that is not supported yet: {0}"),
            ["TouchFinderNone"] = ("Dokunmatik çip bulunamadı. Kabloları ve pinleri kontrol et.", "No touch chip found. Check the wiring and pins."),
            ["TouchFinderTimeout"] = ("Karttan yanıt gelmedi. Bulucu kod yüklü mü, port doğru mu?", "No answer from the board. Is the finder sketch uploaded, and is it the right port?"),
            ["TouchFinderPortError"] = ("Port açılamadı, başka bir program (ör. seri monitör) kullanıyor olabilir: {0}", "Could not open the port; another program (e.g. a serial monitor) may be using it: {0}")
        };

    private static readonly Dictionary<string, (string Tr, string En)> NiniteTexts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NiniteButton"] = ("TOPLU KURULUM", "BATCH INSTALL"),
            ["NiniteTooltip"] = ("İnternetten uygulama ve oyunları tek seferde kur", "Install apps and games from the internet in one go"),
            ["NiniteTitle"] = ("TOPLU KURULUM", "BATCH INSTALL"),
            ["NiniteSubtitle"] = (
                "İstediklerini işaretle, tek tıkla hepsi sırayla kurulsun. Uygulamalar winget ile sessizce, oyunlar Steam/Epic üzerinden kurulur.",
                "Tick what you want and install everything in one go. Apps install silently through winget, games through Steam/Epic."),
            ["NiniteSearchHint"] = ("Listede olmayan bir uygulamayı ara (örn. LibreOffice)…", "Search for an app that is not listed (e.g. LibreOffice)…"),
            ["NiniteSearch"] = ("ARA", "SEARCH"),
            ["NiniteSearching"] = ("Aranıyor…", "Searching…"),
            ["NiniteNoResults"] = ("Sonuç bulunamadı", "No results"),
            ["NiniteResultCount"] = ("{0} sonuç bulundu", "{0} results"),
            ["NiniteGames"] = ("OYUNLAR (STEAM / EPIC)", "GAMES (STEAM / EPIC)"),
            ["NiniteGamesHint"] = (
                "Her satıra bir oyun yaz. Platform için başına Steam: veya Epic: ekleyebilirsin. Örn. Epic: Fortnite",
                "One game per line. Prefix with Steam: or Epic: to pick the platform, e.g. Epic: Fortnite"),
            ["NiniteClear"] = ("TEMİZLE", "CLEAR"),
            ["NiniteInstall"] = ("SEÇİLENLERİ KUR", "INSTALL SELECTED"),
            ["NiniteStop"] = ("DURDUR", "STOP"),
            ["NiniteClose"] = ("KAPAT", "CLOSE"),
            ["NiniteSelected"] = ("{0} uygulama • {1} oyun seçildi", "{0} apps • {1} games selected"),
            ["NiniteNothingSelected"] = ("Önce en az bir uygulama seç ya da bir oyun adı yaz.", "Select at least one app or enter a game name first."),
            ["NiniteWingetMissing"] = (
                "winget bulunamadı. Microsoft Store'dan \"Uygulama Yükleyicisi\"ni kurup tekrar dene.",
                "winget was not found. Install \"App Installer\" from the Microsoft Store and try again."),
            ["NiniteInstalling"] = ("{0} kuruluyor… ({1}/{2})", "Installing {0}… ({1}/{2})"),
            ["NiniteInProgress"] = ("Kuruluyor…", "Installing…"),
            ["NiniteHandingOff"] = ("{0} üzerinden başlatılıyor…", "Starting through {0}…"),
            ["NiniteWaiting"] = ("Sırada", "Queued"),
            ["NiniteSkipped"] = ("Atlandı", "Skipped"),
            ["NiniteSkip"] = ("ATLA", "SKIP"),
            ["NiniteSkipTooltip"] = ("Takılan öğeyi geç, sıradakiyle devam et", "Skip the stuck item and continue with the next one"),
            ["NiniteSkippedByUser"] = ("Atlandı (sen geçtin)", "Skipped by you"),
            ["NiniteTimedOut"] = ("{0} 3 dakika içinde yanıt vermedi, geçildi. Launcher'dan kontrol edebilirsin.", "{0} did not respond within 3 minutes and was skipped. Check the launcher."),
            ["NiniteLog"] = ("GÜNLÜK", "LOG"),
            ["NiniteDone"] = ("Bitti: {0} kuruldu • {1} zaten kuruluydu • {2} başarısız", "Done: {0} installed • {1} already installed • {2} failed"),
            ["NiniteConfirmStop"] = ("Kurulum sürüyor. Durdurup pencereyi kapatmak istiyor musun?", "Installation is running. Stop and close the window?"),
            ["NiniteCatSearchResults"] = ("Arama sonuçları", "Search results"),
            ["NiniteCatBrowsers"] = ("Tarayıcılar", "Browsers"),
            ["NiniteCatCommunication"] = ("İletişim", "Communication"),
            ["NiniteCatGaming"] = ("Oyun platformları", "Game platforms"),
            ["NiniteCatMedia"] = ("Medya ve yaratıcılık", "Media & creativity"),
            ["NiniteCatDevelopment"] = ("Geliştirme", "Development"),
            ["NiniteCatTools"] = ("Araçlar", "Tools"),
            ["NiniteCatOffice"] = ("Ofis ve okul", "Office & school"),
            ["NiniteCatImaging"] = ("Görsel ve tasarım", "Imaging & design"),
            ["NiniteCatStorage"] = ("Bulut depolama", "Cloud storage"),
            ["NiniteCatSecurity"] = ("Güvenlik ve şifreler", "Security & passwords"),
            ["NiniteCatDrivers"] = ("Sürücü araçları", "Driver tools"),
            ["NiniteCatRuntimes"] = ("Çalışma ortamları", "Runtimes"),
            ["NinitePresets"] = ("HAZIR SETLER", "PRESETS"),
            ["NinitePresetGaming"] = ("Oyun bilgisayarı", "Gaming PC"),
            ["NinitePresetSoftware"] = ("Yazılım", "Software development"),
            ["NinitePresetSchool"] = ("Okul", "School"),
            ["NiniteBadgeInstalled"] = ("kurulu", "installed"),
            ["NiniteBadgeUpdate"] = ("güncelleme", "update"),
            ["NiniteBadgeUpToDate"] = ("Kurulu ve güncel ({0})", "Installed and up to date ({0})"),
            ["NiniteStatusLoading"] = ("Kurulu uygulamalar okunuyor…", "Reading installed apps…"),
            ["NiniteStatusSummary"] = ("Listede {0} kurulu • {1} güncelleme var", "{0} installed from the list • {1} updates"),
            ["NiniteStatusFailed"] = ("Kurulu uygulamalar okunamadı", "Could not read installed apps"),
            ["NiniteUpdateAll"] = ("HEPSİNİ GÜNCELLE", "UPDATE ALL"),
            ["NiniteUpdateAllTooltip"] = ("winget ile kurulu ve güncellemesi olan tüm uygulamaları sırayla günceller", "Updates every winget app that has an update, one after another"),
            ["NiniteUninstall"] = ("SEÇİLENLERİ KALDIR", "UNINSTALL SELECTED"),
            ["NiniteUninstallTooltip"] = ("İşaretli uygulamaları bilgisayardan birlikte kaldırır", "Removes the ticked apps from this PC together"),
            ["NiniteNothingToUninstall"] = ("Önce kaldırılacak uygulamaları işaretle.", "Tick the apps to uninstall first."),
            ["NiniteConfirmUninstall"] = ("{0} uygulama bilgisayardan kaldırılacak:\n\n{1}\n\nDevam edilsin mi?", "{0} apps will be removed from this PC:\n\n{1}\n\nContinue?"),
            ["NiniteElevateOnce"] = ("Tek yönetici onayıyla çalıştır", "Ask for administrator approval once"),
            ["NiniteElevateOnceTooltip"] = (
                "Windows izni bir kez sorar, sonra bütün uygulamalar sırayla kurulur. Kapalıysa bazı uygulamalar ayrı ayrı izin isteyebilir.",
                "Windows asks once, then every app runs in order. When off, some apps may ask separately."),
            ["NiniteWaitingForApproval"] = ("Yönetici onayı bekleniyor…", "Waiting for administrator approval…"),
            ["NiniteRetryFailed"] = ("BAŞARISIZLARI TEKRAR DENE", "RETRY FAILED"),
            ["NiniteRetryFailedTooltip"] = (
                "Hata verenleri yeniden dener. Yönetici olarak kurulamayan uygulamalar için bu sefer normal kullanıcı olarak çalışır.",
                "Runs the failed items again, this time as a normal user for apps that refuse to install as administrator."),
            ["NiniteBackToList"] = ("LİSTEYE DÖN", "BACK TO LIST"),
            ["NiniteDoneAll"] = ("Bitti: {0} başarılı • {1} gerek yoktu • {2} başarısız", "Done: {0} succeeded • {1} not needed • {2} failed"),
            ["NiniteUpdating"] = ("{0} güncelleniyor… ({1}/{2})", "Updating {0}… ({1}/{2})"),
            ["NiniteUninstalling"] = ("{0} kaldırılıyor… ({1}/{2})", "Uninstalling {0}… ({1}/{2})"),
            ["NiniteUpdateInProgress"] = ("Güncelleniyor…", "Updating…"),
            ["NiniteUninstallInProgress"] = ("Kaldırılıyor…", "Uninstalling…"),
            ["NiniteActionUpgrade"] = ("güncelle", "update"),
            ["NiniteActionUninstall"] = ("kaldır", "uninstall"),
            ["WingetMsgInstalled"] = ("Kuruldu", "Installed"),
            ["WingetMsgUpdated"] = ("Güncellendi", "Updated"),
            ["WingetMsgRemoved"] = ("Kaldırıldı", "Removed"),
            ["WingetMsgInstalledRestart"] = ("Kuruldu (yeniden başlatma gerekebilir)", "Installed (a restart may be needed)"),
            ["WingetMsgUpdatedRestart"] = ("Güncellendi (yeniden başlatma gerekebilir)", "Updated (a restart may be needed)"),
            ["WingetMsgRemovedRestart"] = ("Kaldırıldı (yeniden başlatma gerekebilir)", "Removed (a restart may be needed)"),
            ["WingetMsgUpToDate"] = ("Zaten güncel", "Already up to date"),
            ["WingetMsgAlreadyInstalled"] = ("Zaten kurulu", "Already installed"),
            ["WingetMsgNotInstalled"] = ("Kurulu değil", "Not installed"),
            ["WingetMsgNotFound"] = ("Paket bulunamadı", "Package not found"),
            ["WingetMsgHashMismatch"] = (
                "Üretici yeni sürüm yayınlamış, winget kaydı henüz güncellenmemiş. Birkaç saat sonra tekrar dene.",
                "The vendor has released a new version and winget has not caught up yet. Try again in a few hours."),
            ["WingetMsgCancelled"] = ("İptal edildi", "Cancelled"),
            ["WingetMsgInvalidId"] = ("Geçersiz paket kimliği", "Invalid package id"),
            ["WingetMsgRefreshing"] = ("Kurulum dosyası doğrulanamadı, winget kaydı yenileniyor…", "Installer could not be verified, refreshing winget sources…"),
            ["WingetMsgTryingAlternate"] = ("Alternatif paket deneniyor: {0}", "Trying an alternative package: {0}"),
            ["WingetMsgHelperClosed"] = ("Yönetici işlemi beklenmedik şekilde kapandı", "The administrator process closed unexpectedly")
        };

    private const string ReleaseNotesTurkish =
        "## Yeni\n" +
        "Vitrin görünümü (eski adıyla Genel Bakış): sağ tık artık her uygulamada oyunları, bağlantıları ve işlemleri yana kaydırılan büyük kartlarla açıyor. Touchpad ve yatay tekerlekle de kaydırılabiliyor.\n" +
        "Arka plan parçacıklarını açıp kapatma: Temalar panelinde, Animasyonlar'ın altında.\n" +
        "Toplu Kurulum'a \"Sürücü araçları\" kategorisi: Intel Driver & Support Assistant, Snappy Driver Installer Origin, Driver Booster, Driver Easy, NVCleanstall, TinyNvidiaUpdateChecker, Display Driver Uninstaller, Driver Store Explorer, Dell Command Update, Lenovo System Update, HP Support Assistant, ASUS Armoury Crate ve MSI Center.\n" +
        "Bu güncelleme notları penceresi.\n" +
        "## Düzeltmeler\n" +
        "Epic Games: yalnızca Fortnite ve Rocket League değil, kurulu tüm oyunlar algılanıyor. Yeni kurduğun ya da kaldırdığın oyunlar listeye kendiliğinden yansıyor.\n" +
        "Steam ve Epic'te \"Oyun adıyla yükle\" ile \"Bağlantı ekle\" kutularına yazılan yazı görünmüyordu; artık görünüyor ve ortalı.\n" +
        "Diyaloglar ve pencereler Alt+Tab'da ayrı pencere olarak çıkmıyor.\n" +
        "## Önceki güncellemelerden\n" +
        "Animasyon hızı ayarı: Hızlı, Normal, Yavaş veya Kapalı.\n" +
        "Toplu Kurulum: hazır setler, kurulu ve güncelleme işaretleri, hepsini güncelle ve toplu kaldırma.\n" +
        "Yeni özellikler 120 dilde.\n" +
        "Ayarlar menüsünde dokunmatik ekran kodu üretici.";

    private const string ReleaseNotesEnglish =
        "## New\n" +
        "Showcase view (formerly Overview): right-clicking any app now opens its games, links and actions as large cards you swipe sideways. Touchpads and horizontal wheels scroll it too.\n" +
        "Background particles can be turned on or off in the Themes panel, under Animations.\n" +
        "Batch Install has a new \"Driver tools\" category: Intel Driver & Support Assistant, Snappy Driver Installer Origin, Driver Booster, Driver Easy, NVCleanstall, TinyNvidiaUpdateChecker, Display Driver Uninstaller, Driver Store Explorer, Dell Command Update, Lenovo System Update, HP Support Assistant, ASUS Armoury Crate and MSI Center.\n" +
        "This release notes window.\n" +
        "## Fixes\n" +
        "Epic Games: every installed game is detected, not only Fortnite and Rocket League. Games you install or remove show up in the list on their own.\n" +
        "Text typed into the Steam and Epic \"Install by name\" and \"Add link\" boxes was invisible; it now shows and is centred.\n" +
        "Dialogs and windows no longer appear as separate windows in Alt+Tab.\n" +
        "## From earlier updates\n" +
        "Animation speed setting: Fast, Normal, Slow or Off.\n" +
        "Batch Install: ready-made sets, installed and update badges, update all, and batch uninstall.\n" +
        "New features in 120 languages.\n" +
        "Touch screen code generator in the Settings menu.";

    public string this[string key]
    {
        get
        {
            if (Translations.TryGetValue(_languageCode, out var languageTranslations) &&
                languageTranslations.TryGetValue(key, out var value))
                return value;

            if (AdditionalTranslations.TryGetValue(_languageCode, out var additional) &&
                additional.TryGetValue(key, out value))
                return value;

            if (SettingsTranslations.TryGetValue(_languageCode, out var settings) &&
                settings.TryGetValue(key, out value))
                return value;

            if (InterfaceTranslationCatalog.TryGetValue(_languageCode, key, out value))
                return value;

            if (NewFeatureTranslations.TryGetValue(_languageCode, key, out value))
                return value;

            if (MoreTranslations.TryGetValue(_languageCode, key, out value))
                return value;

            if (key.Equals("SettingsInterface", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Arayüz"
                    : "Interface";

            if (key.Equals("InterfaceGrid", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Klasik kart görünümü"
                    : "Classic card view";

            if (key.Equals("InterfaceGridDescription", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Mevcut K-Launcher düzeni"
                    : "The current K-Launcher layout";

            if (key.Equals("InterfaceList", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Liste görünümü"
                    : "List view";

            if (key.Equals("InterfaceListDescription", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Uygulamalar solda, oyunlar ve bağlantılar sağda"
                    : "Apps on the left, games and links on the right";

            if (key.Equals("InterfaceListHeader", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "BİR UYGULAMA SEÇ"
                    : "SELECT AN APPLICATION";

            if (key.Equals("InterfaceListSelectHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "DETAYLARI GÖRMEK İÇİN SAĞ TIKLA"
                    : "RIGHT-CLICK TO VIEW DETAILS";

            if (key.Equals("InterfaceListEmpty", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Steam, Opera veya başka bir uygulamaya sağ tıklayarak oyunlarını ve ek bağlantılarını burada aç."
                    : "Right-click Steam, Opera, or another app to open its games and extra links here.";

            if (key.Equals("InterfaceListNoEntries", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Bu uygulama için ek seçim yok"
                    : "No extra selections for this app";

            if (key.Equals("StoreInstallByName", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "OYUN ADIYLA YÜKLE"
                    : "INSTALL GAME BY NAME";

            if (key.Equals("StoreInstallAction", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "STEAM'DE BUL VE YÜKLE"
                    : "FIND AND INSTALL";

            if (key.Equals("StoreInstallActionEpic", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "EPIC'TE BUL VE YÜKLE"
                    : "FIND IN EPIC AND INSTALL";

            if (key.Equals("StoreGameNameHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Oyun adını yaz; resmi launcher arayıp yüklemeyi başlatsın."
                    : "Enter a game name and the official launcher will find it.";

            if (key.Equals("StoreGameNamePlaceholder", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Örn. Counter-Strike 2"
                    : "For example, Counter-Strike 2";

            if (key.Equals("StoreGameNameRequired", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Önce bir oyun adı yazmalısın."
                    : "Enter a game name first.";

            if (key.Equals("StoreBatchInstall", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "TOPLU KURULUM"
                    : "BATCH INSTALL";

            if (key.Equals("StoreBatchInstallHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Her satıra bir oyun yaz. İstersen Steam: veya Epic: ile platformu belirt."
                    : "Enter one game per line. Use Steam: or Epic: to choose the platform.";

            if (key.Equals("StoreBatchInstallPlaceholder", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Örn. Counter-Strike 2\nEpic: Fortnite\nSteam: Terraria"
                    : "For example, Counter-Strike 2\nEpic: Fortnite\nSteam: Terraria";

            if (key.Equals("StoreBatchInstallAction", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "SEÇİLENLERİ KUR"
                    : "INSTALL ALL";

            if (key.Equals("StoreBatchInstallEmpty", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "En az bir oyun adı yazmalısın."
                    : "Enter at least one game name.";

            if (key.Equals("StoreBatchInstallSummary", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Toplu kurulum tamamlandı."
                    : "Batch installation finished.";

            if (AutomationTexts.TryGetValue(key, out var automationText))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? automationText.Tr
                    : automationText.En;

            if (MovedTexts.TryGetValue(key, out var movedText))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? movedText.Tr
                    : movedText.En;

            if (TouchTexts.TryGetValue(key, out var touchText))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? touchText.Tr
                    : touchText.En;

            if (NiniteTexts.TryGetValue(key, out var niniteText))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? niniteText.Tr
                    : niniteText.En;

            if (key.Equals("HideContents", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "İÇERİKLERİ GİZLE"
                    : "HIDE CONTENTS";

            if (key.Equals("ShowContents", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "İÇERİKLERİ GÖSTER"
                    : "SHOW CONTENTS";

            if (key.Equals("ContentsHidden", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Bu uygulamanın içerikleri gizli"
                    : "This app's contents are hidden";

            if (key.Equals("SelectContents", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "İÇERİK SEÇ"
                    : "SELECT CONTENTS";

            if (key.Equals("ShowHiddenContents", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "GİZLİ İÇERİKLERİ GÖSTER"
                    : "SHOW HIDDEN CONTENTS";

            if (key.Equals("ContentSelectionTitle", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "GİZLENECEK İÇERİKLERİ SEÇ"
                    : "SELECT CONTENTS TO HIDE";

            if (key.Equals("ContentSelectionHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Gizlemek istediklerini işaretle. İşaretlenmeyenler görünür kalır."
                    : "Check what you want to hide. Unchecked items stay visible.";

            if (key.Equals("NoContentsToSelect", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Seçilecek içerik yok"
                    : "There is no content to select";

            if (key.Equals("BatchSelect", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "SEÇ"
                    : "SELECT";

            if (key.Equals("BatchSelectActive", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "SEÇİMİ KAPAT"
                    : "EXIT SELECTION";

            if (key.Equals("LaunchSelected", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "SEÇİLENLERİ BAŞLAT"
                    : "LAUNCH SELECTED";

            if (key.Equals("CancelSelection", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "İPTAL"
                    : "CANCEL";

            if (key.Equals("SelectedCount", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "{0} uygulama seçildi"
                    : "{0} apps selected";

            if (key.Equals("InterfaceOverview", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Vitrin görünümü"
                    : "Showcase view";

            if (key.Equals("InterfaceOverviewDescription", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Sağ tık, uygulamanın oyunlarını, bağlantılarını ve işlemlerini yana kaydırılan büyük kartlarla açar"
                    : "Right-click opens an app's games, links and actions as large cards you swipe sideways";

            if (key.Equals("SettingsMotion", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Animasyonlar"
                    : "Animations";

            if (key.Equals("MotionFast", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Hızlı"
                    : "Fast";

            if (key.Equals("MotionNormal", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Normal"
                    : "Normal";

            if (key.Equals("MotionSlow", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Yavaş"
                    : "Slow";

            if (key.Equals("MotionOff", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Kapalı"
                    : "Off";

            if (key.Equals("MotionDescription", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Kapalı seçeneği en akıcı performansı verir"
                    : "Off gives the smoothest performance";

            if (WindowFadeTexts.TryGet(_languageCode, key, out var windowFadeText))
                return windowFadeText;

            if (key.Equals("SettingsParticles", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Arka plan parçacıkları"
                    : "Background particles";

            if (key.Equals("ParticlesOn", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Açık"
                    : "On";

            if (key.Equals("ParticlesDescription", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Kapalıyken parçacıklar tamamen kaldırılır, işlemci ve bellek kullanımı düşer"
                    : "When off, the particles are removed entirely to save CPU and memory";

            if (key.Equals("InterfaceToggleHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Arayüzü değiştir"
                    : "Switch interface";

            if (key.Equals("InterfaceOverviewHeader", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "VİTRİN"
                    : "SHOWCASE";

            if (key.Equals("InstallLocationHeading", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "K-LAUNCHER NEREYE KURULU?"
                    : "WHERE IS K-LAUNCHER INSTALLED?";

            if (key.Equals("InstallLocationDescription", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "K-Launcher'ı nereden açarsan aç, kendini otomatik olarak bu klasöre kopyalar ve buradan çalışır. Ayarların da bu klasördeki Data klasöründe durur. Masaüstündeki K-Launcher kısayolu da buraya açılır."
                    : "Wherever you open K-Launcher from, it copies itself to this folder automatically and runs from here. Your settings are kept in the Data folder inside it, and the desktop shortcut opens this copy.";

            if (key.Equals("InstallLocationOpen", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Klasörü aç"
                    : "Open folder";

            if (key.Equals("ReleaseNotesTooltip", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Güncelleme notları"
                    : "Release notes";

            if (key.Equals("ReleaseNotesTitle", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "K-LAUNCHER 4.0  //  GÜNCELLEME NOTLARI"
                    : "K-LAUNCHER 4.0  //  RELEASE NOTES";

            // "## " starts a section heading; every other line is one bullet.
            if (key.Equals("ReleaseNotesText", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? ReleaseNotesTurkish
                    : ReleaseNotesEnglish;

            if (key.Equals("InterfaceSwipeHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "SAĞA VE SOLA KAYDIR"
                    : "SWIPE LEFT / RIGHT";

            if (key.Equals("InterfaceLaunchHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "SOL TIK BAŞLAT   /   SAĞ TIK MENÜ"
                    : "CLICK TO LAUNCH   /   RIGHT CLICK MENU";

            if (AppActionTranslations.TryGetValue(_languageCode, out var appActions) &&
                appActions.TryGetValue(key, out value))
                return value;

            if (key.Equals("AppFileFilter", StringComparison.OrdinalIgnoreCase) &&
                ExtendedTranslations.TryGetValue(_languageCode, "AppFilterApplications", out var appFilterApplications) &&
                ExtendedTranslations.TryGetValue(_languageCode, "AppFilterAllFiles", out var appFilterAllFiles))
            {
                return $"{appFilterApplications} (*.exe;*.lnk;*.appref-ms)|*.exe;*.lnk;*.appref-ms|" +
                       $"{appFilterAllFiles} (*.*)|*.*";
            }

            if (ExtendedTranslations.TryGetValue(_languageCode, key, out value))
                return value;

            if (ExtendedTranslationsRemaining.TryGetValue(_languageCode, key, out value))
                return value;

            if (key.Equals("SearchLanguages", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Dilleri ara"
                    : "Search languages";

            if (key.Equals("SearchHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Dil ara..."
                    : "Search...";

            if (key.Equals("SearchAppsHint", StringComparison.OrdinalIgnoreCase))
                return _languageCode.Equals("tr", StringComparison.OrdinalIgnoreCase)
                    ? "Uygulama ara..."
                    : "Search apps...";

            return Translations["en"].TryGetValue(key, out value) ? value : key;
        }
    }

    public string GetSupportedApplicationsText()
    {
        string text;
        if (AdditionalTranslations.TryGetValue(_languageCode, out var additional) &&
            additional.TryGetValue("InfoBody", out var body))
        {
            text = body;
        }
        else
        {
            text = $"{this["GameLaunchers"]}\n{this["GameLaunchersDesc"]}\n\n" +
                   $"{this["Browsers"]}\n{this["BrowsersDesc"]}\n\n" +
                   $"{this["DailyApps"]}\n{this["DailyAppsDesc"]}\n\n" +
                   $"{this["InstalledPwa"]}\n{this["InstalledPwaDesc"]}\n\n" +
                   $"{this["AddYourself"]}\n{this["AddYourselfDesc"]}";
        }

        if (LocalizedAppNames.TryGetValue(_languageCode, out var localizedNames))
        {
            foreach (var (original, localized) in localizedNames.OrderByDescending(pair => pair.Key.Length))
                text = text.Replace(original, localized, StringComparison.OrdinalIgnoreCase);
        }

        if (ExtendedLocalizedAppNames.TryGetMap(_languageCode, out var supplementalNames) &&
            supplementalNames is not null)
        {
            foreach (var (original, localized) in supplementalNames.OrderByDescending(pair => pair.Key.Length))
                text = text.Replace(original, localized, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    public void SetLanguage(string languageCode)
    {
        if (!IsLanguageSupported(languageCode) ||
            _languageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
            return;

        _languageCode = languageCode;
        OnPropertyChanged(nameof(LanguageCode));
        OnPropertyChanged("Item[]");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, _languageCode);
        }
        catch
        {
            // Language changes remain active for this run if settings cannot be saved.
        }
    }

    private static bool IsLanguageSupported(string languageCode) =>
        SupportedLanguages.Any(language =>
            language.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
