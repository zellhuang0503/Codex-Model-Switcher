using System.Text;

namespace CodexModelSwitcher;

internal sealed class BackupManager
{
    private const int RecentBackupLimit = 5;
    private readonly string backupDirectory;

    public BackupManager(string codexHome)
    {
        backupDirectory = Path.Combine(codexHome, "model-switcher", "backups");
        OriginalBackupPath = Path.Combine(backupDirectory, "original-config.toml");
    }

    public string OriginalBackupPath { get; }

    public bool HasOriginalBackup => File.Exists(OriginalBackupPath);

    public DateTime? OriginalBackupTime => HasOriginalBackup
        ? File.GetLastWriteTime(OriginalBackupPath)
        : null;

    public void SaveBeforeSwitch(byte[] configBytes)
    {
        ArgumentNullException.ThrowIfNull(configBytes);
        Directory.CreateDirectory(backupDirectory);
        SaveOriginalOnce(configBytes);
        SaveRecent(configBytes);
        PruneRecentBackups();
    }

    public void SaveBeforeRestore(byte[] configBytes)
    {
        ArgumentNullException.ThrowIfNull(configBytes);
        Directory.CreateDirectory(backupDirectory);
        SaveRecent(configBytes);
        PruneRecentBackups();
    }

    public byte[] ReadOriginal()
    {
        if (!HasOriginalBackup)
        {
            throw new InvalidOperationException("找不到首次切換前的原始設定備份。");
        }

        return File.ReadAllBytes(OriginalBackupPath);
    }

    private void SaveOriginalOnce(byte[] configBytes)
    {
        if (HasOriginalBackup)
        {
            return;
        }

        try
        {
            using var stream = new FileStream(
                OriginalBackupPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough);
            stream.Write(configBytes);
            stream.Flush(true);
        }
        catch (IOException) when (HasOriginalBackup)
        {
            // 另一個同時啟動的切換器已先建立原始備份，保留先建立的版本。
        }
    }

    private void SaveRecent(byte[] configBytes)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var path = Path.Combine(backupDirectory, $"switch-{timestamp}-{Guid.NewGuid():N}.toml");
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        stream.Write(configBytes);
        stream.Flush(true);
    }

    private void PruneRecentBackups()
    {
        var recent = new DirectoryInfo(backupDirectory)
            .EnumerateFiles("switch-*.toml", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var oldBackup in recent.Skip(RecentBackupLimit))
        {
            oldBackup.Delete();
        }
    }
}
