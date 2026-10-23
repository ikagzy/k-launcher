using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace KLauncher.Services;

public enum StoreLauncherAction
{
    Select,
    Install,
    Uninstall
}

public sealed record StoreLauncherAutomationResult(
    bool Success,
    string Message,
    bool RequiresUserAction = false);

/// <summary>
/// Operates the already-installed official game launcher through its visible
/// Windows UI. No account credentials or private store API are used.
/// </summary>
public static class StoreLauncherAutomation
{
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const uint InputKeyboard = 1;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkEnter = 0x0D;
    private const ushort VkEscape = 0x1B;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    // Steam renders its current UI in steamwebhelper.exe. The bootstrap
    // process (steam.exe) may have no usable main window, so prefer the
    // visible web helper and keep the bootstrap as a fallback.
    private static readonly string[] SteamProcessNames = ["steamwebhelper", "steam"];

    // Epic has used both names across launcher versions. The main launcher
    // is preferred, with the web helper as a fallback for the UI window.
    private static readonly string[] EpicProcessNames = ["EpicGamesLauncher", "EpicWebHelper"];

    private static readonly string[] SearchTerms =
    [
        "search your library",
        "search library",
        "search games",
        "kütüphaneyi ada göre filtrele",
        "kütüphaneyi ara",
        "isme göre ara",
        "mağazada ara",
        "library",
        "kütüphane",
        "search",
        "ara",
        "find",
        "filter"
    ];

    public static Task<StoreLauncherAutomationResult> RunAsync(
        string platform,
        string gameName,
        string? launcherPath,
        StoreLauncherAction action,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null) =>
        Task.Run(
            () => Run(platform, gameName, launcherPath, action, cancellationToken, progress),
            cancellationToken);

    private static StoreLauncherAutomationResult Run(
        string platform,
        string gameName,
        string? launcherPath,
        StoreLauncherAction action,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return new(false, LocalizationService.Text("AutoNameEmpty"));

        // Fast path: resolve the game id and open the launcher's own install
        // URI. This skips the slow search-box/UI-tree walk entirely.
        if (action == StoreLauncherAction.Install)
        {
            var direct = StoreInstallService
                .TryInstallAsync(platform, gameName, cancellationToken, progress)
                .GetAwaiter()
                .GetResult();
            if (direct is not null)
                return direct;

            progress?.Report(LocalizationService.Text("AutoSearchingUi", platform));
            InstallLog.Write($"{platform}: \"{gameName}\" için arayüz yöntemi kullanılıyor.");
        }

        var processNames = platform.Equals("Epic", StringComparison.OrdinalIgnoreCase)
            ? EpicProcessNames
            : SteamProcessNames;

        var process = FindRunningProcess(processNames);
        if (process is null)
        {
            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                return new(
                    false,
                    LocalizationService.Text("AutoLauncherMissing", platform),
                    RequiresUserAction: true);
            }

            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = launcherPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                return new(false, LocalizationService.Text("AutoOpenFailed", platform, ex.Message));
            }
        }

        if (process is null)
        {
            return new(false, LocalizationService.Text("AutoStartFailed", platform));
        }

        using (process)
        {
            var window = WaitForMainWindow(process, processNames, cancellationToken);
            if (window is null)
            {
                return new(
                    false,
                    LocalizationService.Text("AutoWindowMissing", platform),
                    RequiresUserAction: true);
            }

            var readyWindow = FindSearchableWindow(process, processNames);
            if (readyWindow is not null)
                window = readyWindow;

            ActivateWindow(window.Current.NativeWindowHandle);

            if (!FocusSearchBox(window, gameName, cancellationToken))
            {
                // Steam can expose several steamwebhelper windows during
                // startup. The first one may be a hidden/social helper with
                // no search field, so inspect the other visible launcher
                // windows before reporting a UI mismatch.
                var alternativeWindow = FindSearchableWindow(process, processNames);
                if (alternativeWindow is not null)
                {
                    window = alternativeWindow;
                    ActivateWindow(window.Current.NativeWindowHandle);
                }

                if (alternativeWindow is not null &&
                    FocusSearchBox(window, gameName, cancellationToken))
                {
                    // Continue with the same search/result flow below.
                }
                else
                {
                    return new(
                        false,
                        LocalizationService.Text("AutoSearchBoxMissing", platform),
                        RequiresUserAction: true);
                }
            }

            // Embedded Steam/Epic browser windows can accept the value but
            // ignore a synthetic Enter key. Library views filter live, so do
            // not press Enter there; only submit when a real search button
            // exists.
            var searchRoot = TryRefreshRoot(window) ?? window;
            if (!HasLiveLibrarySearch(searchRoot) &&
                !TryInvokeButton(searchRoot, new[] { "Ara", "Search" }))
                SendVirtualKey(VkEnter);
            Thread.Sleep(120);
            cancellationToken.ThrowIfCancellationRequested();

            if (action == StoreLauncherAction.Select)
            {
                if (WaitForGameResultAndSelect(window, gameName, cancellationToken))
                    return new(true, LocalizationService.Text("AutoSelected", gameName, platform));

                return new(
                    false,
                    LocalizationService.Text("AutoNoResult", gameName, platform),
                    RequiresUserAction: true);
            }

            var buttonNames = action == StoreLauncherAction.Install
                ? new[] { "Install", "Yükle", "Kur", "Download" }
                : new[] { "Uninstall", "Kaldır", "Remove" };

            // The store UI may navigate to a details page after the result is
            // selected. Wait for the actual action button rather than using a
            // single fixed delay; Steam commonly needs a few hundred ms here.
            if (!WaitForGameResultAndSelect(window, gameName, cancellationToken) ||
                !WaitForActionButtonAndInvoke(window, buttonNames, cancellationToken))
            {
                return new(
                    false,
                    LocalizationService.Text(action == StoreLauncherAction.Uninstall ? "AutoUninstallButtonMissing" : "AutoInstallButtonMissing", gameName, platform),
                    RequiresUserAction: true);
            }

            // The library button only opens the launcher's confirmation
            // window ("Yükle / İptal"). Previously the flow stopped here and
            // reported success, so Steam never actually started downloading.
            if (action == StoreLauncherAction.Install &&
                !StoreInstallService.ConfirmLauncherInstallDialog(platform, cancellationToken))
            {
                return new(
                    false,
                    LocalizationService.Text("AutoConfirmFailed", gameName, platform),
                    RequiresUserAction: true);
            }

            return new(true, LocalizationService.Text(action == StoreLauncherAction.Uninstall ? "AutoUninstallStarted" : "AutoInstallStarted", gameName));
        }
    }


    private static AutomationElement? WaitForMainWindow(
        Process process,
        string[] processNames,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    return AutomationElement.FromHandle(process.MainWindowHandle);
            }
            catch
            {
                // The launcher can restart its bootstrap process during startup.
            }

            foreach (var handle in GetTopLevelWindowHandles(process.Id))
            {
                try { return AutomationElement.FromHandle(handle); }
                catch { }
            }

            foreach (var processName in processNames)
            {
                foreach (var candidate in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (candidate.MainWindowHandle != IntPtr.Zero)
                            return AutomationElement.FromHandle(candidate.MainWindowHandle);

                        foreach (var handle in GetTopLevelWindowHandles(candidate.Id))
                        {
                            try { return AutomationElement.FromHandle(handle); }
                            catch { }
                        }
                    }
                    catch { }
                    finally { candidate.Dispose(); }
                }
            }

            // Chromium-based launchers can expose a UIA top-level window even
            // when EnumWindows/MainWindowHandle does not report a usable HWND.
            // Read the desktop UIA children as a final fallback.
            var automationWindow = FindTopLevelAutomationWindow(processNames);
            if (automationWindow is not null)
                return automationWindow;

            Thread.Sleep(250);
        }

        return null;
    }

    private static Process? FindRunningProcess(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var process = Process.GetProcessesByName(name)
                    .FirstOrDefault(candidate =>
                    {
                        try { return !candidate.HasExited; }
                        catch { return false; }
                    });
                if (process is not null)
                    return process;
            }
            catch { }
        }

        return null;
    }

    private static bool FocusSearchBox(
        AutomationElement root,
        string gameName,
        CancellationToken cancellationToken)
    {
        // The official launchers render their UI asynchronously. On a cold
        // start the window exists before the web UI and its search field do,
        // so looking only once produces the misleading "launcher differs"
        // error. Keep refreshing the UIA tree until the real field appears.
        for (var attempt = 0; attempt < 48; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentRoot = TryRefreshRoot(root) ?? root;
            // Steam Library: "İsme göre ara" / "Search your library".
            // Epic Library: "Kütüphaneyi ada göre filtrele".
            // Steam Store variants: "Mağazada ara" / "Search".
            var edits = FindDescendants(currentRoot, ControlType.Edit)
                .Concat(FindDescendants(currentRoot, ControlType.ComboBox))
                .Where(IsUsableElement)
                .ToArray();

            var edit = edits
                .OrderByDescending(candidate => SearchFieldScore(SafeName(candidate), SearchTerms))
                .FirstOrDefault(candidate => SearchFieldScore(SafeName(candidate), SearchTerms) > 0);

            if (edit is not null && TrySetSearchValue(edit, gameName))
                return true;

            Thread.Sleep(250);
        }

        return false;
    }

    private static AutomationElement? FindSearchableWindow(
        Process process,
        string[] processNames)
    {
        foreach (var candidate in EnumerateLauncherWindows(process, processNames))
        {
            if (HasSearchBox(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<AutomationElement> EnumerateLauncherWindows(
        Process process,
        string[] processNames)
    {
        var processIds = new HashSet<int>();
        try
        {
            if (!process.HasExited)
                processIds.Add(process.Id);
        }
        catch { }

        foreach (var processName in processNames)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var candidateProcess in candidates)
            {
                try
                {
                    if (!candidateProcess.HasExited)
                        processIds.Add(candidateProcess.Id);
                }
                catch { }
                finally { candidateProcess.Dispose(); }
            }
        }

        var seenHandles = new HashSet<IntPtr>();
        foreach (var processId in processIds)
        {
            foreach (var handle in GetTopLevelWindowHandles(processId))
            {
                if (!seenHandles.Add(handle))
                    continue;

                AutomationElement? window = null;
                try { window = AutomationElement.FromHandle(handle); }
                catch { }

                if (window is not null)
                    yield return window;
            }
        }
    }

    private static IReadOnlyList<IntPtr> GetTopLevelWindowHandles(int processId)
    {
        var handles = new List<IntPtr>();
        EnumWindows((windowHandle, _) =>
        {
            try
            {
                GetWindowThreadProcessId(windowHandle, out var ownerProcessId);
                if (ownerProcessId == processId)
                    handles.Add(windowHandle);
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        return handles;
    }

    private static AutomationElement? FindTopLevelAutomationWindow(
        IEnumerable<string> processNames)
    {
        var processIds = new HashSet<int>();
        foreach (var processName in processNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (!process.HasExited)
                            processIds.Add(process.Id);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }
        }

        try
        {
            var windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);

            foreach (AutomationElement window in windows)
            {
                try
                {
                    if (processIds.Contains(window.Current.ProcessId))
                        return window;
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private static bool HasSearchBox(AutomationElement root)
    {
        var edits = FindDescendants(root, ControlType.Edit)
            .Concat(FindDescendants(root, ControlType.ComboBox));
        return edits.Any(candidate =>
            IsUsableElement(candidate) &&
            SearchFieldScore(SafeName(candidate), SearchTerms) > 0);
    }

    private static bool HasLiveLibrarySearch(AutomationElement root)
    {
        var edits = FindDescendants(root, ControlType.Edit)
            .Concat(FindDescendants(root, ControlType.ComboBox));
        return edits.Any(candidate =>
        {
            var name = SafeName(candidate);
            return IsUsableElement(candidate) &&
                   (name.Contains("library", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("kütüphane", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("isme göre ara", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("filtrele", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static bool TrySelectGameResult(AutomationElement root, string gameName)
    {
        var descendants = FindDescendantsOfTypes(
                root,
                ControlType.Button,
                ControlType.Hyperlink,
                ControlType.ListItem,
                ControlType.DataItem,
                ControlType.MenuItem,
                ControlType.Text)
            .Where(candidate => IsUsableElement(candidate))
            .ToArray();

        var normalizedName = gameName.Trim();
        // Steam Library exposes games as ListItem/DataItem elements named
        // exactly like the game. Epic Library exposes them as Button elements
        // whose name is usually "Game Name - Yükle/Oyna".
        var clickableTypes = new[]
        {
            ControlType.Button,
            ControlType.Hyperlink,
            ControlType.ListItem,
            ControlType.DataItem,
            ControlType.MenuItem
        };

        var clickable = descendants
            .Where(candidate => clickableTypes.Contains(candidate.Current.ControlType))
            .ToArray();

        var exact = clickable.FirstOrDefault(candidate =>
            string.Equals(SafeName(candidate).Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));

        exact ??= clickable.FirstOrDefault(candidate =>
            string.Equals(
                NormalizeGameTitle(SafeName(candidate)),
                NormalizeGameTitle(normalizedName),
                StringComparison.OrdinalIgnoreCase));

        exact ??= clickable.FirstOrDefault(candidate =>
            IsGameResultName(SafeName(candidate), normalizedName));

        // Some clients put the exact title in a text child and expose only
        // its parent card as clickable. Walk up from that text node as a
        // fallback instead of relying on screen coordinates.
        if (exact is null)
        {
            var title = descendants.FirstOrDefault(candidate =>
                string.Equals(SafeName(candidate).Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
            exact = FindClickableAncestor(title, root, clickableTypes);
        }

        // Store result links usually append price, edition and library state
        // to the visible name (for example "Counter-Strike $7.19 ...").
        // Match the clickable hyperlink by its leading game name when there
        // is no text element with the exact name.
        exact ??= FindDescendants(root, ControlType.Hyperlink)
            .Where(candidate => !candidate.Current.IsOffscreen && candidate.Current.IsEnabled)
            .OrderBy(candidate => SafeName(candidate).Length)
            .FirstOrDefault(candidate =>
                SafeName(candidate).Trim().StartsWith(normalizedName + " ", StringComparison.OrdinalIgnoreCase));

        if (exact is null)
            return false;

        // Embedded Chromium links can report InvokePattern successfully
        // without navigating. A real click on the visible result is the
        // reliable path; accessibility patterns remain the fallback.
        if (TryClickElement(exact))
            return true;

        try
        {
            if (exact.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionObject) &&
                selectionObject is SelectionItemPattern selection)
            {
                selection.Select();
                return true;
            }

            if (exact.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) &&
                invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }
        }
        catch { }

        return false;
    }

    private static bool IsUsableElement(AutomationElement element)
    {
        try
        {
            return !element.Current.IsOffscreen && element.Current.IsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static int SearchFieldScore(string name, IReadOnlyCollection<string> searchTerms)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        var normalized = name.Trim();
        var score = 0;
        foreach (var term in searchTerms)
        {
            if (!normalized.Contains(term, StringComparison.OrdinalIgnoreCase))
                continue;

            score = Math.Max(score, term.Length);
            if (normalized.Equals(term, StringComparison.OrdinalIgnoreCase))
                score += 100;
        }

        return score;
    }

    private static bool TrySetSearchValue(AutomationElement edit, string gameName)
    {
        try
        {
            edit.SetFocus();
            if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject) &&
                patternObject is ValuePattern valuePattern &&
                !valuePattern.Current.IsReadOnly)
            {
                // Both Steam's "İsme göre ara" and Epic's
                // "Kütüphaneyi ada göre filtrele" expose ValuePattern. This
                // avoids locale-sensitive keyboard injection for normal text.
                valuePattern.SetValue(gameName);
                return true;
            }
        }
        catch { }

        if (!TryClickElement(edit))
            return false;

        SendControlKey('A');
        SendUnicodeText(gameName);
        return true;
    }

    private static bool IsGameResultName(string value, string gameName)
    {
        var name = value.Trim();
        if (name.StartsWith(gameName + " -", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(gameName + " ", StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedName = NormalizeGameTitle(name);
        var normalizedGameName = NormalizeGameTitle(gameName);
        return normalizedName.StartsWith(normalizedGameName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGameTitle(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
                buffer.Append(character);
        }

        return buffer.ToString();
    }

    private static AutomationElement? FindClickableAncestor(
        AutomationElement? element,
        AutomationElement root,
        IReadOnlyCollection<ControlType> clickableTypes)
    {
        if (element is null)
            return null;

        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = element;
            for (var depth = 0; depth < 6; depth++)
            {
                if (clickableTypes.Contains(current.Current.ControlType) && IsUsableElement(current))
                    return current;

                if (current.Equals(root))
                    break;

                current = walker.GetParent(current);
                if (current is null)
                    break;
            }
        }
        catch { }

        return null;
    }

    private static bool WaitForGameResultAndSelect(
        AutomationElement root,
        string gameName,
        CancellationToken cancellationToken)
    {
        // Store clients can take several seconds to populate the result list,
        // especially on a cold start or while the account library is loading.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentRoot = TryRefreshRoot(root) ?? root;
            if (TrySelectGameResult(currentRoot, gameName))
                return true;

            Thread.Sleep(160);
        }

        return false;
    }

    private static bool WaitForActionButtonAndInvoke(
        AutomationElement root,
        IEnumerable<string> names,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentRoot = TryRefreshRoot(root) ?? root;
            if (TryInvokeButton(currentRoot, names))
                return true;

            Thread.Sleep(120);
        }

        return false;
    }

    private static bool TryInvokeButton(AutomationElement root, IEnumerable<string> names)
    {
        var candidates = FindDescendants(root, ControlType.Button)
            .Where(button => !button.Current.IsOffscreen && button.Current.IsEnabled)
            .ToArray();

        var nameList = names.ToArray();
        var named = candidates
            .Select(candidate => (Element: candidate, Name: SafeName(candidate).Trim()))
            .ToArray();

        // Prefer an exact label for ANY of the names before falling back to a
        // partial match. Otherwise Epic library cards such as
        // "Other Game - Yükle" could be clicked instead of the real button.
        // For Turkish Steam, "Arama terimini temizle" must not win over "Ara".
        var ordered = nameList
            .Select(name => named.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)).Element)
            .Concat(nameList.Select(name => named.FirstOrDefault(candidate =>
                !candidate.Name.Contains(" - ", StringComparison.Ordinal) &&
                candidate.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).Element))
            .Where(element => element is not null);

        foreach (var button in ordered)
        {
            if (button is null)
                continue;

            try
            {
                if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject) &&
                    invokeObject is InvokePattern invoke)
                {
                    invoke.Invoke();
                    return true;
                }
            }
            catch { }

            if (TryClickElement(button))
                return true;
        }

        return false;
    }

    private static AutomationElement? TryRefreshRoot(AutomationElement root)
    {
        try
        {
            var handle = root.Current.NativeWindowHandle;
            return handle == 0 ? null : AutomationElement.FromHandle(handle);
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement[] FindDescendants(AutomationElement root, ControlType? controlType)
    {
        try
        {
            var condition = controlType is null
                ? Condition.TrueCondition
                : new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);
            return root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>().ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static AutomationElement[] FindDescendantsOfTypes(
        AutomationElement root,
        params ControlType[] controlTypes)
    {
        try
        {
            Condition condition = controlTypes.Length == 1
                ? new PropertyCondition(AutomationElement.ControlTypeProperty, controlTypes[0])
                : new OrCondition(controlTypes
                    .Select(type => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, type))
                    .ToArray());
            return root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>().ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try { return element.Current.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool TryClickElement(AutomationElement element)
    {
        try
        {
            if (!element.TryGetClickablePoint(out var point))
                return false;

            SetCursorPos((int)point.X, (int)point.Y);
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ActivateWindow(int handle)
    {
        if (handle == 0)
            return;

        var window = new IntPtr(handle);
        ShowWindowAsync(window, SwRestore);
        ShowWindowAsync(window, SwShow);
        SetForegroundWindow(window);
        Thread.Sleep(150);
    }

    private static void SendControlKey(char key)
    {
        SendVirtualKey(VkControl, keyDown: true);
        SendVirtualKey((ushort)char.ToUpperInvariant(key));
        SendVirtualKey(VkControl, keyDown: false);
    }

    private static void SendVirtualKey(ushort key, bool keyDown = true)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Data = new InputData
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    ScanCode = 0,
                    Flags = keyDown ? 0u : KeyEventKeyUp,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private static void SendUnicodeText(string text)
    {
        foreach (var character in text)
        {
            var down = new Input
            {
                Type = InputKeyboard,
                Data = new InputData
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = 0,
                        ScanCode = character,
                        Flags = KeyEventUnicode,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            };
            var up = down;
            up.Data.Keyboard.Flags = KeyEventUnicode | KeyEventKeyUp;
            SendInput(1, [down, up], Marshal.SizeOf<Input>());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
}
