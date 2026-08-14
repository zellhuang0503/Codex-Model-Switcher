using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexModelSwitcher;

internal sealed record CurrentCodexModel(
    bool ConfigExists,
    string ProviderId,
    string ProviderDisplayName,
    string ModelId,
    string ModelDisplayName,
    string Notice);

internal sealed record ConfigSwitchResult(string ProviderDisplayName, string ModelDisplayName);

internal sealed partial class CodexConfigManager
{
    private const int MaximumConfigBytes = 5 * 1024 * 1024;
    private const string ManagedProviderId = "codex-switcher-deepseek";
    private const string MarkerPrefix = "# codex-model-switcher-saved-";
    private static readonly string[] ManagedRootKeys = ["model", "model_provider", "model_reasoning_effort"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string codexHome;
    private readonly string configPath;
    private readonly string lockPath;
    private readonly BackupManager backupManager;

    private CodexConfigManager(string codexHome)
    {
        this.codexHome = Path.GetFullPath(codexHome);
        configPath = Path.Combine(this.codexHome, "config.toml");
        lockPath = Path.Combine(this.codexHome, "model-switcher", "config.lock");
        backupManager = new BackupManager(this.codexHome);
    }

    public static CodexConfigManager ForCurrentUser()
    {
        var configuredHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var home = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configuredHome;
        return new CodexConfigManager(home);
    }

    internal static CodexConfigManager ForTest(string codexHome) => new(codexHome);

    public bool ConfigExists => File.Exists(configPath);

    public bool CanRestoreOriginal => backupManager.HasOriginalBackup;

    public DateTime? OriginalBackupTime => backupManager.OriginalBackupTime;

    public CurrentCodexModel ReadCurrent()
    {
        if (!ConfigExists)
        {
            return new CurrentCodexModel(
                false,
                string.Empty,
                "—",
                string.Empty,
                "—",
                "找不到 Codex 使用者設定，請先啟動一次 Codex。");
        }

        var document = ReadDocument(File.ReadAllBytes(configPath));
        var provider = document.GetRootValue("model_provider") ?? "openai";
        var model = document.GetRootValue("model") ?? string.Empty;
        var providerDisplay = provider switch
        {
            "openai" => "OpenAI（Codex 原生）",
            ManagedProviderId => "DeepSeek",
            _ => provider
        };
        var modelDisplay = string.IsNullOrWhiteSpace(model) ? "使用 Codex 原生預設" : model;
        return new CurrentCodexModel(true, provider, providerDisplay, model, modelDisplay, "Codex 設定已安全讀取。");
    }

    public ConfigSwitchResult ApplySelection(
        ProviderDefinition provider,
        ModelDefinition model,
        string switcherExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        if (provider.Id is not ("openai" or "deepseek"))
        {
            throw new InvalidOperationException("這個供應商尚未開放安全切換。");
        }

        using var configLock = AcquireLock();
        var originalBytes = ReadRequiredConfig();
        var original = ReadDocument(originalBytes);
        var transformed = provider.Id == "deepseek"
            ? BuildDeepSeekConfig(original, provider, model, switcherExecutablePath)
            : BuildOpenAiConfig(original);
        var outputBytes = Encode(transformed.Text, original.HasUtf8Bom);
        ValidateExpectedSelection(outputBytes, provider, model);

        if (originalBytes.AsSpan().SequenceEqual(outputBytes))
        {
            return new ConfigSwitchResult(provider.DisplayName, model.DisplayName);
        }

        backupManager.SaveBeforeSwitch(originalBytes);
        ReplaceConfigAtomically(outputBytes);
        ValidateExpectedSelection(File.ReadAllBytes(configPath), provider, model);
        return new ConfigSwitchResult(provider.DisplayName, model.DisplayName);
    }

    public void RestoreOriginal()
    {
        using var configLock = AcquireLock();
        var currentBytes = ReadRequiredConfig();
        var originalBytes = backupManager.ReadOriginal();
        _ = ReadDocument(originalBytes);
        backupManager.SaveBeforeRestore(currentBytes);
        ReplaceConfigAtomically(originalBytes);
        _ = ReadDocument(File.ReadAllBytes(configPath));
    }

    public static bool IsCodexRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase) ||
                        process.ProcessName.Equals("OpenAI.Codex", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 程序在列舉期間結束，繼續檢查其他程序。
                }
            }
        }

        return false;
    }

    private ConfigDocument BuildDeepSeekConfig(
        ConfigDocument document,
        ProviderDefinition provider,
        ModelDefinition model,
        string switcherExecutablePath)
    {
        if (!provider.RequiresApiKey ||
            !Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("DeepSeek 供應商資料不完整，已停止切換。");
        }

        var lines = document.Lines.ToList();
        RemoveManagedTables(lines);
        var existingMarkers = ReadMarkers(lines);
        var currentProvider = document.GetRootValue("model_provider") ?? "openai";
        var switchingFromManaged = currentProvider == ManagedProviderId;

        if (existingMarkers.Count == 0 && !switchingFromManaged)
        {
            foreach (var key in ManagedRootKeys)
            {
                existingMarkers[key] = document.GetRootLine(key);
            }
        }
        else if (existingMarkers.Count != ManagedRootKeys.Length)
        {
            throw new InvalidOperationException("找不到完整的 OpenAI 原設定標記，已停止切換。");
        }

        RemoveManagedRootLinesAndMarkers(lines);
        var rootLines = new List<string>();
        foreach (var key in ManagedRootKeys)
        {
            existingMarkers.TryGetValue(key, out var savedLine);
            rootLines.Add(BuildMarker(key, savedLine, document.NewLine));
        }
        rootLines.Add($"model = {Quote(model.Id)}{document.NewLine}");
        rootLines.Add($"model_provider = {Quote(ManagedProviderId)}{document.NewLine}");
        rootLines.Add($"model_reasoning_effort = \"high\"{document.NewLine}");
        InsertAtRootEnd(lines, rootLines, document.NewLine);

        EnsureTrailingNewLine(lines, document.NewLine);
        lines.Add(document.NewLine);
        lines.Add($"[model_providers.{ManagedProviderId}]{document.NewLine}");
        lines.Add($"name = {Quote(provider.DisplayName)}{document.NewLine}");
        lines.Add($"base_url = {Quote(provider.BaseUrl!)}{document.NewLine}");
        lines.Add($"wire_api = \"responses\"{document.NewLine}");
        lines.Add(document.NewLine);
        lines.Add($"[model_providers.{ManagedProviderId}.auth]{document.NewLine}");
        lines.Add($"command = {Quote(Path.GetFullPath(switcherExecutablePath))}{document.NewLine}");
        lines.Add($"args = [\"token\", \"deepseek\"]{document.NewLine}");
        lines.Add($"timeout_ms = 5000{document.NewLine}");
        lines.Add($"refresh_interval_ms = 0{document.NewLine}");
        return document with { Lines = lines };
    }

    private ConfigDocument BuildOpenAiConfig(ConfigDocument document)
    {
        var currentProvider = document.GetRootValue("model_provider") ?? "openai";
        var markers = ReadMarkers(document.Lines);
        if (currentProvider != ManagedProviderId && markers.Count == 0)
        {
            return document;
        }

        if (markers.Count != ManagedRootKeys.Length)
        {
            throw new InvalidOperationException("原始 OpenAI 模型設定標記不完整，已停止切換。");
        }

        var lines = document.Lines.ToList();
        RemoveManagedTables(lines);
        RemoveManagedRootLinesAndMarkers(lines);
        var restoredLines = ManagedRootKeys
            .Select(key => markers[key])
            .Where(line => line is not null)
            .Select(line => line! + document.NewLine)
            .ToList();
        InsertAtRootEnd(lines, restoredLines, document.NewLine);
        return document with { Lines = lines };
    }

    private void ValidateExpectedSelection(byte[] bytes, ProviderDefinition provider, ModelDefinition model)
    {
        var document = ReadDocument(bytes);
        var providerId = document.GetRootValue("model_provider") ?? "openai";
        if (provider.Id == "deepseek")
        {
            if (providerId != ManagedProviderId ||
                document.GetRootValue("model") != model.Id ||
                document.GetRootValue("model_reasoning_effort") != "high" ||
                !document.HasTable($"model_providers.{ManagedProviderId}") ||
                !document.HasTable($"model_providers.{ManagedProviderId}.auth"))
            {
                throw new InvalidOperationException("暫存設定驗證失敗，原設定未變更。");
            }
        }
        else if (providerId == ManagedProviderId ||
                 document.HasTable($"model_providers.{ManagedProviderId}") ||
                 document.Lines.Any(line => line.TrimStart().StartsWith(MarkerPrefix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("OpenAI 原設定還原驗證失敗。");
        }
    }

    private byte[] ReadRequiredConfig()
    {
        if (!ConfigExists)
        {
            throw new FileNotFoundException("找不到 Codex 使用者設定，請先啟動一次 Codex。", configPath);
        }

        return File.ReadAllBytes(configPath);
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        try
        {
            return new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            throw new InvalidOperationException("另一個切換作業正在進行，請稍後再試。");
        }
    }

    private void ReplaceConfigAtomically(byte[] outputBytes)
    {
        var directory = Path.GetDirectoryName(configPath)!;
        var temporaryPath = Path.Combine(directory, $"config.switch-{Guid.NewGuid():N}.tmp");
        var rollbackPath = Path.Combine(directory, $"config.rollback-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(outputBytes);
                stream.Flush(true);
            }

            File.Replace(temporaryPath, configPath, rollbackPath, true);
            if (!File.ReadAllBytes(configPath).AsSpan().SequenceEqual(outputBytes))
            {
                File.Replace(rollbackPath, configPath, null, true);
                throw new IOException("正式設定重新讀取不一致，已恢復原設定。");
            }
        }
        catch
        {
            if (File.Exists(rollbackPath) && File.Exists(configPath))
            {
                try
                {
                    File.Replace(rollbackPath, configPath, null, true);
                }
                catch
                {
                    // 保留 rollback 檔供人工復原，不掩蓋原始錯誤。
                }
            }

            throw;
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(rollbackPath);
        }
    }

    private static ConfigDocument ReadDocument(byte[] bytes)
    {
        if (bytes.Length > MaximumConfigBytes)
        {
            throw new InvalidOperationException("Codex 設定檔過大，為安全起見不進行切換。");
        }

        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var offset = hasBom ? Encoding.UTF8.Preamble.Length : 0;
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException("Codex 設定不是有效的 UTF-8 文字，已停止切換。");
        }

        if (text.IndexOf('\0') >= 0)
        {
            throw new InvalidOperationException("Codex 設定包含無法處理的字元，已停止切換。");
        }

        var newLine = text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : text.Contains('\n')
                ? "\n"
                : text.Contains('\r')
                    ? "\r"
                    : Environment.NewLine;
        var lines = SplitLines(text);
        ValidateManagedRootKeys(lines);
        return new ConfigDocument(lines, newLine, hasBom);
    }

    private static List<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        return LinePattern().Matches(text).Select(match => match.Value).Where(line => line.Length > 0).ToList();
    }

    private static void ValidateManagedRootKeys(IReadOnlyList<string> lines)
    {
        var rootEnd = FindRootEnd(lines);
        foreach (var key in ManagedRootKeys)
        {
            var matches = lines.Take(rootEnd).Where(line => IsAssignmentFor(line, key)).ToList();
            if (matches.Count > 1 || (matches.Count == 1 && ParseTomlStringAssignment(matches[0], key) is null))
            {
                throw new InvalidOperationException($"Codex 設定中的 {key} 格式無法安全辨識，已停止切換。");
            }
        }
    }

    private static Dictionary<string, string?> ReadMarkers(IReadOnlyList<string> lines)
    {
        var markers = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var trimmed = TrimLineEnding(line).Trim();
            foreach (var key in ManagedRootKeys)
            {
                var prefix = $"{MarkerPrefix}{key}: ";
                if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!markers.TryAdd(key, DecodeMarker(trimmed[prefix.Length..])))
                {
                    throw new InvalidOperationException("Codex 切換器的原設定標記重複，已停止切換。");
                }
            }
        }

        return markers;
    }

    private static string BuildMarker(string key, string? originalLine, string newLine)
    {
        var encoded = originalLine is null
            ? "-"
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(originalLine));
        return $"{MarkerPrefix}{key}: {encoded}{newLine}";
    }

    private static string? DecodeMarker(string encoded)
    {
        if (encoded == "-")
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(Convert.FromBase64String(encoded));
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw new InvalidOperationException("Codex 切換器的原設定標記已損壞，已停止切換。");
        }
    }

    private static void RemoveManagedRootLinesAndMarkers(List<string> lines)
    {
        var rootEnd = FindRootEnd(lines);
        for (var index = rootEnd - 1; index >= 0; index--)
        {
            var trimmed = TrimLineEnding(lines[index]).TrimStart();
            if (ManagedRootKeys.Any(key => IsAssignmentFor(lines[index], key)) ||
                trimmed.StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                lines.RemoveAt(index);
            }
        }
    }

    private static void RemoveManagedTables(List<string> lines)
    {
        var index = 0;
        while (index < lines.Count)
        {
            var table = ParseTableName(lines[index]);
            if (table is null ||
                (table != $"model_providers.{ManagedProviderId}" && table != $"model_providers.{ManagedProviderId}.auth"))
            {
                index++;
                continue;
            }

            var end = index + 1;
            while (end < lines.Count && ParseTableName(lines[end]) is null)
            {
                end++;
            }

            lines.RemoveRange(index, end - index);
        }
    }

    private static void InsertAtRootEnd(List<string> lines, IReadOnlyList<string> inserted, string newLine)
    {
        if (inserted.Count == 0)
        {
            return;
        }

        var rootEnd = FindRootEnd(lines);
        if (rootEnd > 0 && !HasLineEnding(lines[rootEnd - 1]))
        {
            lines[rootEnd - 1] += newLine;
        }

        if (rootEnd > 0 && !string.IsNullOrWhiteSpace(TrimLineEnding(lines[rootEnd - 1])))
        {
            inserted = [newLine, .. inserted];
        }

        lines.InsertRange(rootEnd, inserted);
    }

    private static int FindRootEnd(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (ParseTableName(lines[index]) is not null)
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static string? ParseTableName(string line)
    {
        var match = TablePattern().Match(TrimLineEnding(line));
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return name.Trim();
    }

    private static bool IsAssignmentFor(string line, string key) =>
        Regex.IsMatch(TrimLineEnding(line), $"^\\s*{Regex.Escape(key)}\\s*=", RegexOptions.CultureInvariant);

    private static string? ParseTomlStringAssignment(string line, string key)
    {
        var match = Regex.Match(
            TrimLineEnding(line),
            $"^\\s*{Regex.Escape(key)}\\s*=\\s*(?<value>\"(?:\\\\.|[^\"\\\\])*\"|'[^']*')\\s*(?:#.*)?$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value;
        if (value.StartsWith('\''))
        {
            return value[1..^1];
        }

        try
        {
            return JsonSerializer.Deserialize<string>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);

    private static byte[] Encode(string text, bool includeBom)
    {
        var body = StrictUtf8.GetBytes(text);
        if (!includeBom)
        {
            return body;
        }

        return [.. Encoding.UTF8.Preamble, .. body];
    }

    private static void EnsureTrailingNewLine(List<string> lines, string newLine)
    {
        if (lines.Count == 0)
        {
            return;
        }

        if (!HasLineEnding(lines[^1]))
        {
            lines[^1] += newLine;
        }
    }

    private static bool HasLineEnding(string line) => line.EndsWith('\n') || line.EndsWith('\r');

    private static string TrimLineEnding(string line) => line.TrimEnd('\r', '\n');

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 暫存清理失敗不應覆蓋主要操作結果。
        }
    }

    private sealed record ConfigDocument(IReadOnlyList<string> Lines, string NewLine, bool HasUtf8Bom)
    {
        public string Text => string.Concat(Lines);

        public string? GetRootValue(string key)
        {
            var line = GetRootLine(key);
            return line is null ? null : ParseTomlStringAssignment(line, key);
        }

        public string? GetRootLine(string key)
        {
            var rootEnd = FindRootEnd(Lines);
            return Lines.Take(rootEnd)
                .Select(TrimLineEnding)
                .SingleOrDefault(line => IsAssignmentFor(line, key));
        }

        public bool HasTable(string name) => Lines.Any(line => ParseTableName(line) == name);
    }

    [GeneratedRegex(".*?(?:\\r\\n|\\n|\\r|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();

    [GeneratedRegex("^\\s*(?:\\[\\[([^\\[\\]]+)\\]\\]|\\[([^\\[\\]]+)\\])\\s*(?:#.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TablePattern();
}
