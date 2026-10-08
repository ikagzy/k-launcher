using System.IO;

namespace KLauncher.Services;

public static class AssetPath
{
    private static readonly Lazy<string> AssetsDirectory = new(FindAssetsDirectory);

    public static string Get(string relativePath) =>
        Path.Combine(AssetsDirectory.Value, relativePath);

    private static string FindAssetsDirectory()
    {
        var adjacentAssets = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (Directory.Exists(adjacentAssets))
            return adjacentAssets;

        var applicationName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(applicationName))
            throw new InvalidOperationException("Could not determine the application name for bundled assets.");

        var extractionRoot = Path.Combine(Path.GetTempPath(), ".net", applicationName);
        if (!Directory.Exists(extractionRoot))
            throw new DirectoryNotFoundException($"Bundled assets were not extracted to '{extractionRoot}'.");

        var extractedAssets = Directory.GetDirectories(extractionRoot)
            .Select(directory => Path.Combine(directory, "Assets"))
            .Where(Directory.Exists)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return extractedAssets
            ?? throw new DirectoryNotFoundException($"No extracted Assets directory was found in '{extractionRoot}'.");
    }
}
