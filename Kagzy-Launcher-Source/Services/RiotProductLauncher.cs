using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace KLauncher.Services;

/// <summary>
/// Requests a VALORANT launch through Riot Client's local product-launcher API.
/// The lockfile credential is used only to authenticate to the loopback API.
/// </summary>
public static class RiotProductLauncher
{
    private const string LaunchEndpoint = "/product-launcher/v1/products/valorant/patchlines/live";
    private static readonly TimeSpan ClientReadyTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GameStartTimeout = TimeSpan.FromSeconds(45);

    public static async Task<string?> LaunchValorantAsync(string riotServicesPath)
    {
        if (IsGameRunning())
            return null;

        if (string.IsNullOrWhiteSpace(riotServicesPath) || !File.Exists(riotServicesPath))
            return "RiotClientServices.exe bulunamadı. Riot Client kurulumunu kontrol et.";

        var lockfilePath = FindLockfilePath();
        var clientAlreadyRunning = IsRiotClientRunning();
        if (!clientAlreadyRunning)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = riotServicesPath,
                    Arguments = "--launch-product=valorant --launch-patchline=live",
                    WorkingDirectory = Path.GetDirectoryName(riotServicesPath) ?? string.Empty,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                return $"Riot Client başlatılamadı: {ex.Message}";
            }
        }

        var deadline = DateTime.UtcNow + ClientReadyTimeout;
        var unreadableLockfileAttempts = 0;
        string? lastLockfileError = null;

        while (DateTime.UtcNow < deadline)
        {
            var lockfile = TryReadLockfile(lockfilePath, out var lockfileError);
            if (lockfile is not null)
            {
                unreadableLockfileAttempts = 0;
                lastLockfileError = null;

                var launch = await TryLaunchProductAsync(lockfile);
                if (launch.Success)
                {
                    return await WaitForGameProcessAsync(GameStartTimeout)
                        ? null
                        : "Riot Client başlatma isteğini kabul etti, ancak VALORANT süreci açılmadı. Riot Client oturumunu ve güncelleme durumunu kontrol et. Riot'un 2026 akışında oyun sayfasındaki Play düğmesi gerekebilir.";
                }

                if (launch.Error is not null)
                    return launch.Error;
            }
            else if (lockfileError is not null)
            {
                lastLockfileError = lockfileError;
                unreadableLockfileAttempts++;
                if (unreadableLockfileAttempts >= 3)
                    return lockfileError;
            }

            await Task.Delay(500);
        }

        if (lastLockfileError is not null)
            return lastLockfileError;

        return IsRiotClientRunning()
            ? "Riot Client açık, ancak yerel başlatma servisi 20 saniye içinde hazır olmadı. Riot Client'ı yeniden başlatıp tekrar dene."
            : "Riot Client'ın yerel başlatma servisi zamanında hazır olmadı.";
    }

    private static string FindLockfilePath()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Riot Games", "Riot Client", "Config", "lockfile"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Riot Games", "Riot Client", "Config", "lockfile"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Riot Games", "Riot Client", "Config", "lockfile"),
            @"C:\Riot Games\Riot Client\Config\lockfile"
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static RiotLockfile? TryReadLockfile(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
            return null;

        try
        {
            var fields = File.ReadAllText(path).Trim().Split(':', 5);
            if (fields.Length != 5 ||
                !int.TryParse(fields[2], out var port) ||
                port is <= 0 or > 65535 ||
                string.IsNullOrWhiteSpace(fields[3]) ||
                (fields[4] != "http" && fields[4] != "https"))
            {
                error = "Riot Client lockfile biçimi beklenenden farklı. Riot Client'ı yeniden başlat.";
                return null;
            }

            return new RiotLockfile(port, fields[3], fields[4]);
        }
        catch (IOException)
        {
            error = "Riot Client lockfile'ı başka bir işlem tarafından kilitli; K-Launcher otomatik başlatma kimlik bilgisini okuyamadı.";
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            error = "K-Launcher Riot Client lockfile'ına erişemedi. Riot Client'ı ve K-Launcher'ı aynı Windows kullanıcısıyla yeniden başlatıp tekrar dene.";
            return null;
        }
    }

    private static async Task<LaunchResult> TryLaunchProductAsync(RiotLockfile lockfile)
    {
        using var handler = new HttpClientHandler();
        if (lockfile.Scheme == "https")
        {
            // Riot uses a self-signed certificate for its local API. Accept it
            // only when the request is addressed to the loopback interface.
            handler.ServerCertificateCustomValidationCallback =
                (request, _, _, _) => request?.RequestUri?.IsLoopback == true;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{lockfile.Password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

        var address = new UriBuilder(lockfile.Scheme, IPAddress.Loopback.ToString(), lockfile.Port, LaunchEndpoint).Uri;
        try
        {
            using var response = await client.PostAsync(address, content: null);
            if (response.IsSuccessStatusCode)
                return new LaunchResult(true, null);

            var riotCode = response.Headers.TryGetValues("x-riot-error-code", out var values)
                ? string.Join(", ", values)
                : null;
            var codeText = string.IsNullOrWhiteSpace(riotCode) ? string.Empty : $" Riot hata kodu: {riotCode}.";

            return new LaunchResult(
                false,
                $"Riot Client VALORANT başlatma isteğini reddetti (HTTP {(int)response.StatusCode}).{codeText} " +
                "Riot Client'ta oturum açıldığını ve oyunun güncel olduğunu kontrol et.");
        }
        catch (HttpRequestException)
        {
            return new LaunchResult(false, null);
        }
        catch (TaskCanceledException)
        {
            return new LaunchResult(false, null);
        }
    }

    private static async Task<bool> WaitForGameProcessAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsGameRunning())
                return true;

            await Task.Delay(500);
        }

        return IsGameRunning();
    }

    private static bool IsRiotClientRunning()
    {
        var processes = Process.GetProcessesByName("RiotClientServices");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static bool IsGameRunning()
    {
        var bootstrapProcesses = Process.GetProcessesByName("VALORANT");
        var gameProcesses = Process.GetProcessesByName("VALORANT-Win64-Shipping");
        try
        {
            return bootstrapProcesses.Length > 0 || gameProcesses.Length > 0;
        }
        finally
        {
            foreach (var process in bootstrapProcesses)
                process.Dispose();
            foreach (var process in gameProcesses)
                process.Dispose();
        }
    }

    private sealed record RiotLockfile(int Port, string Password, string Scheme);
    private sealed record LaunchResult(bool Success, string? Error);
}
