using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KLauncher.Services;

public sealed record WingetBatchItem(WingetAction Action, string PackageId);

/// <summary>
/// Runs a whole list of winget jobs in one elevated copy of the launcher, so
/// Windows asks for administrator approval once instead of once per app.
///
/// The two processes talk through a private folder in %TEMP%:
///   job.txt     written by the launcher, one "Action|PackageId" per line
///   status.txt  appended by the helper: start / line / done / end records
///   cancel      created by the launcher to stop the batch
///   skip-N      created by the launcher to skip item N
/// </summary>
public static class WingetBatchHost
{
    public const string HelperArgument = "--winget-batch";
    private const string FolderPrefix = "KLauncher-batch-";

    // ------------------------------------------------------------ Launcher side

    /// <summary>
    /// Starts the elevated helper and reports its progress. Returns false when
    /// the approval was declined or the helper could not start, so the caller
    /// can fall back to installing without elevation.
    /// </summary>
    public static async Task<bool> RunElevatedAsync(
        IReadOnlyList<WingetBatchItem> items,
        Action<int> started,
        Action<int, string> line,
        Action<int, WingetInstallResult> done,
        Func<int, bool> skipRequested,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), FolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllLines(
            Path.Combine(folder, "job.txt"),
            items.Select(item => $"{item.Action}|{item.PackageId}"));

        Process? helper;
        try
        {
            helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                ArgumentList = { HelperArgument, folder },
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Win32Exception)
        {
            // 1223: the user said no to the approval prompt.
            TryDeleteFolder(folder);
            return false;
        }

        if (helper is null)
        {
            TryDeleteFolder(folder);
            return false;
        }

        var statusPath = Path.Combine(folder, "status.txt");
        var reported = new HashSet<int>();
        var skipSignalled = new HashSet<int>();
        long offset = 0;
        var pending = string.Empty;
        var current = -1;
        var finished = false;
        var cancelSignalled = false;

        using (helper)
        {
            while (!finished)
            {
                if (cancellationToken.IsCancellationRequested && !cancelSignalled)
                {
                    cancelSignalled = true;
                    TryCreate(Path.Combine(folder, "cancel"));
                }

                if (current >= 0 && !skipSignalled.Contains(current) && skipRequested(current))
                {
                    skipSignalled.Add(current);
                    TryCreate(Path.Combine(folder, $"skip-{current}"));
                }

                var exited = helper.HasExited;
                (offset, pending) = ReadNewLines(statusPath, offset, pending, record =>
                {
                    var parts = record.Split('\t');
                    if (parts.Length < 2 && parts[0] != "end")
                        return;

                    switch (parts[0])
                    {
                        case "start" when int.TryParse(parts[1], out var index):
                            current = index;
                            started(index);
                            break;
                        case "line" when parts.Length >= 3 && int.TryParse(parts[1], out var index):
                            line(index, parts[2]);
                            break;
                        case "done" when parts.Length >= 4 && int.TryParse(parts[1], out var index) &&
                                         Enum.TryParse<WingetInstallStatus>(parts[2], out var status):
                            reported.Add(index);
                            done(index, new WingetInstallResult(status, parts[3]));
                            break;
                        case "end":
                            finished = true;
                            break;
                    }
                });

                if (exited)
                    break;

                await Task.Delay(250, CancellationToken.None);
            }
        }

        // The helper ended without reporting some items (crashed or was killed).
        for (var index = 0; index < items.Count; index++)
        {
            if (!reported.Contains(index))
                done(index, cancellationToken.IsCancellationRequested
                    ? new WingetInstallResult(WingetInstallStatus.Cancelled, "WingetMsgCancelled")
                    : new WingetInstallResult(WingetInstallStatus.Failed, "WingetMsgHelperClosed"));
        }

        TryDeleteFolder(folder);
        return true;
    }

    private static (long Offset, string Pending) ReadNewLines(
        string path,
        long offset,
        string pending,
        Action<string> handle)
    {
        try
        {
            if (!File.Exists(path))
                return (offset, pending);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= offset)
                return (offset, pending);

            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - offset];
            var read = stream.Read(buffer, 0, buffer.Length);
            offset += read;

            var text = pending + Encoding.UTF8.GetString(buffer, 0, read);
            var lastBreak = text.LastIndexOf('\n');
            if (lastBreak < 0)
                return (offset, text);

            foreach (var record in text[..lastBreak].Split('\n'))
            {
                var trimmed = record.TrimEnd('\r');
                if (trimmed.Length > 0)
                    handle(trimmed);
            }

            return (offset, text[(lastBreak + 1)..]);
        }
        catch (IOException)
        {
            return (offset, pending);
        }
    }

    // -------------------------------------------------------------- Helper side

    /// <summary>Entry point of the elevated copy. Never shows a window.</summary>
    public static async Task RunHelperAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!folder.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(folder).StartsWith(FolderPrefix, StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(folder, "job.txt")))
            return;

        var items = new List<WingetBatchItem>();
        foreach (var record in File.ReadAllLines(Path.Combine(folder, "job.txt")))
        {
            var parts = record.Split('|');
            if (parts.Length == 2 &&
                Enum.TryParse<WingetAction>(parts[0], out var action) &&
                WingetService.IsValidPackageId(parts[1]))
                items.Add(new WingetBatchItem(action, parts[1]));
            else
                items.Add(new WingetBatchItem(WingetAction.Install, string.Empty));
        }

        using var status = new StreamWriter(
            new FileStream(Path.Combine(folder, "status.txt"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
            new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        void Write(string record)
        {
            lock (status)
                status.WriteLine(record.Replace('\n', ' ').Replace('\r', ' '));
        }

        var cancelPath = Path.Combine(folder, "cancel");
        for (var index = 0; index < items.Count; index++)
        {
            if (File.Exists(cancelPath))
            {
                Write($"done\t{index}\t{WingetInstallStatus.Cancelled}\tNiniteSkipped");
                continue;
            }

            Write($"start\t{index}");
            using var itemCancellation = new CancellationTokenSource();
            var skipPath = Path.Combine(folder, $"skip-{index}");
            using var watcher = new Timer(_ =>
            {
                if (File.Exists(cancelPath) || File.Exists(skipPath))
                    try { itemCancellation.Cancel(); } catch (ObjectDisposedException) { }
            }, null, 300, 300);

            var item = items[index];
            var itemIndex = index;
            var progress = new SyncProgress(text => Write($"line\t{itemIndex}\t{text.Replace('\t', ' ')}"));
            var result = await WingetService.ExecuteAsync(item.Action, item.PackageId, progress, itemCancellation.Token);
            if (result.Status == WingetInstallStatus.Cancelled && File.Exists(skipPath) && !File.Exists(cancelPath))
                result = result with { Message = "NiniteSkippedByUser" };
            Write($"done\t{index}\t{result.Status}\t{result.Message.Replace('\t', ' ')}");
        }

        Write("end");
    }

    // Reports on the calling thread, so a line can never arrive after its done record.
    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private static void TryCreate(string path)
    {
        try { File.WriteAllText(path, string.Empty); }
        catch { }
    }

    private static void TryDeleteFolder(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch { }
    }
}
