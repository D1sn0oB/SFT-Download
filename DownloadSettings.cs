namespace SFTLauncher.Download;

public static class DownloadSettings
{
    public const int DefaultParallelDownloads = 8;
    public const int MinParallelDownloads = 1;
    public const int MaxParallelDownloads = 32;

    public static int ParallelDownloads
    {
        get
        {
            var value = Properties.Settings.Default.DownloadParallelDownloads;
            return value >= MinParallelDownloads && value <= MaxParallelDownloads
                ? value
                : DefaultParallelDownloads;
        }
    }

    public static void SaveParallelDownloads(int count)
    {
        var clamped = Math.Clamp(count, MinParallelDownloads, MaxParallelDownloads);
        Properties.Settings.Default.DownloadParallelDownloads = clamped;
        Properties.Settings.Default.Save();
    }

    public static string ActiveSourceName
    {
        get => Properties.Settings.Default.DownloadActiveSource ?? DownloadSources.Bmcl.Name;
    }

    public static void SaveActiveSource(DownloadSource source)
    {
        Properties.Settings.Default.DownloadActiveSource = source.Name;
        Properties.Settings.Default.Save();
        DownloadSourceProvider.Active = source;
    }

    public static void ApplySavedSettings()
    {
        var activeName = ActiveSourceName;
        DownloadSourceProvider.Active = DownloadSources.All
            .FirstOrDefault(s => string.Equals(s.Name, activeName, StringComparison.OrdinalIgnoreCase))
            ?? DownloadSources.Bmcl;
    }
}
