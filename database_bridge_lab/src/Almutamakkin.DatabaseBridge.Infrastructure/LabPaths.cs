namespace Almutamakkin.DatabaseBridge.Infrastructure;

public static class LabPaths
{
    public const string AppFolderName = "Almutamakkin";
    public const string LabFolderName = "DatabaseBridgeLab";

    public static string LocalAppDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName,
            LabFolderName);

    public static string AppSettingsFilePath =>
        Path.Combine(LocalAppDataRoot, "appsettings.json");

    public static string DatabaseProfilesFilePath =>
        Path.Combine(LocalAppDataRoot, "database-profiles.json");

    public static string SnapshotFingerprintsFilePath =>
        Path.Combine(LocalAppDataRoot, "snapshot-fingerprints.json");

    /// <summary>
    /// Phase 0/1 change-stream foundation: local revision + watermark per
    /// (system, domain), written by DomainWatchService. Separate from
    /// snapshot-fingerprints.json because it never triggers a full publish.
    /// </summary>
    public static string ChangeCursorsFilePath =>
        Path.Combine(LocalAppDataRoot, "change-cursors.json");

    public static string LogsDirectory =>
        Path.Combine(LocalAppDataRoot, "logs");

    public static string LabDataLogsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "lab-data", "logs");

    public static string EnsureLocalAppDataRoot()
    {
        Directory.CreateDirectory(LocalAppDataRoot);
        return LocalAppDataRoot;
    }

    /// <summary>
    /// Removes all persisted pairing, connection-profile, and synchronization
    /// state before a new bridge process starts.  The bridge deliberately
    /// starts each process as a fresh operator session: it must be paired and
    /// a database connection must be selected again.
    /// </summary>
    public static void ResetPersistedSessionState()
    {
        EnsureLocalAppDataRoot();

        DeleteIfPresent(AppSettingsFilePath);
        DeleteIfPresent(DatabaseProfilesFilePath);
        DeleteIfPresent(SnapshotFingerprintsFilePath);
        DeleteIfPresent(ChangeCursorsFilePath);

        foreach (var backup in Directory.EnumerateFiles(
                     LocalAppDataRoot,
                     "change-cursors.corrupt-*.json",
                     SearchOption.TopDirectoryOnly))
        {
            DeleteIfPresent(backup);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string EnsureLogsDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
        return LogsDirectory;
    }

    public static IReadOnlyList<string> GetLogDirectories()
    {
        EnsureLogsDirectory();

        var directories = new List<string> { LogsDirectory };

        if (Directory.Exists(LabDataLogsDirectory))
        {
            directories.Add(LabDataLogsDirectory);
        }

        return directories;
    }

    public static string GetDailyLogFileName(DateTime? utcNow = null)
    {
        var timestamp = utcNow ?? DateTime.UtcNow;
        return $"bridge-{timestamp:yyyyMMdd}.log";
    }
}
