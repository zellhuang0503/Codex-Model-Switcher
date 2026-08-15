using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string ManagedProviderIdPrefix = "codex-switcher-";
    private const string MarkerPrefix = "# codex-model-switcher-saved-";
    private static readonly string[] ManagedRootKeys =
    [
        "model",
        "model_provider",
        "model_reasoning_effort",
        "preferred_auth_method",
        "forced_login_method",
        "model_catalog_json"
    ];

    // 舊版切換器只管理前三個鍵；讀到舊標記時自動升級。
    private static readonly string[] LegacyManagedRootKeys = ["model", "model_provider", "model_reasoning_effort"];
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

    public string CustomProvidersPath => Path.Combine(codexHome, "model-switcher", "custom-providers.json");

    public string ModelCatalogPath => Path.Combine(codexHome, "models.json");

    // 設定檔內寫入的值：預設 Codex 資料夾時採官方慣例的 ~ 寫法，
    // 讓桌面版分別在 Windows 與 WSL 兩種執行環境都能各自解析；
    // 自訂 CODEX_HOME 時退回絕對路徑。
    private string ModelCatalogConfigValue
    {
        get
        {
            var defaultHome = Path.GetFullPath(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
            return string.Equals(Path.GetFullPath(codexHome), defaultHome, StringComparison.OrdinalIgnoreCase)
                ? "~/.codex/models.json"
                : ModelCatalogPath;
        }
    }

    private string ModelCatalogOwnedFlagPath => Path.Combine(codexHome, "model-switcher", "models-owned-by-switcher.flag");

    private string ModelCatalogUserBackupPath => Path.Combine(codexHome, "model-switcher", "models-before-switcher.json");

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
        var providerDisplay = provider == "openai"
            ? "OpenAI（Codex 原生）"
            : IsManagedProviderId(provider)
                ? document.GetTableValue($"model_providers.{provider}", "name") ?? provider
                : provider;
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
        if (provider.Id != "openai")
        {
            ValidateManagedProvider(provider, model);
        }

        using var configLock = AcquireLock();
        var originalBytes = ReadRequiredConfig();
        var original = ReadDocument(originalBytes);
        if (provider.Id != "openai")
        {
            if (File.Exists(ModelCatalogPath) && !File.Exists(ModelCatalogOwnedFlagPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ModelCatalogUserBackupPath)!);
                File.Copy(ModelCatalogPath, ModelCatalogUserBackupPath, true);
            }

            CodexModelCatalog.Write(ModelCatalogPath, provider);
            Directory.CreateDirectory(Path.GetDirectoryName(ModelCatalogOwnedFlagPath)!);
            File.WriteAllText(ModelCatalogOwnedFlagPath, "models.json 由切換器產生與管理。");
        }

        var transformed = provider.Id != "openai"
            ? BuildManagedConfig(original, provider, model, switcherExecutablePath)
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
        RestoreModelCatalog();
    }

    private void RestoreModelCatalog()
    {
        if (File.Exists(ModelCatalogUserBackupPath))
        {
            File.Copy(ModelCatalogUserBackupPath, ModelCatalogPath, true);
            TryDelete(ModelCatalogOwnedFlagPath);
        }
        else if (File.Exists(ModelCatalogOwnedFlagPath))
        {
            TryDelete(ModelCatalogPath);
            TryDelete(ModelCatalogOwnedFlagPath);
        }
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

    private static void ValidateManagedProvider(ProviderDefinition provider, ModelDefinition model)
    {
        if (provider.Id.Length > 48 ||
            !ManagedSourceIdPattern().IsMatch(provider.Id) ||
            !provider.RequiresApiKey ||
            provider.Protocol != "responses" ||
            provider.Models.All(candidate => candidate.Id != model.Id) ||
            !Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("這個供應商尚未開放安全切換。");
        }
    }

    private ConfigDocument BuildManagedConfig(
        ConfigDocument document,
        ProviderDefinition provider,
        ModelDefinition model,
        string switcherExecutablePath)
    {
        var managedId = ToManagedProviderId(provider.Id);
        var lines = document.Lines.ToList();
        RemoveManagedTables(lines);
        var existingMarkers = ReadMarkers(lines);
        var currentProvider = document.GetRootValue("model_provider") ?? "openai";
        var switchingFromManaged = IsManagedProviderId(currentProvider);

        if (existingMarkers.Count == 0 && !switchingFromManaged)
        {
            foreach (var key in ManagedRootKeys)
            {
                existingMarkers[key] = document.GetRootLine(key);
            }
        }
        else if (IsLegacyMarkerSet(existingMarkers))
        {
            // 舊版標記缺少的鍵當時未被切換器改動，目前設定中的值即原始值。
            foreach (var key in ManagedRootKeys.Except(LegacyManagedRootKeys))
            {
                existingMarkers[key] = document.GetRootLine(key);
            }
        }
        else if (!IsCompleteMarkerSet(existingMarkers))
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
        rootLines.Add($"model_provider = {Quote(managedId)}{document.NewLine}");
        if (SelectReasoningEffort(model) is { } effort)
        {
            rootLines.Add($"model_reasoning_effort = {Quote(effort)}{document.NewLine}");
        }
        rootLines.Add($"preferred_auth_method = \"apikey\"{document.NewLine}");
        rootLines.Add($"forced_login_method = \"api\"{document.NewLine}");
        rootLines.Add($"model_catalog_json = {Quote(ModelCatalogConfigValue)}{document.NewLine}");
        InsertAtRootEnd(lines, rootLines, document.NewLine);

        EnsureTrailingNewLine(lines, document.NewLine);
        lines.Add(document.NewLine);
        lines.Add($"[model_providers.{managedId}]{document.NewLine}");
        lines.Add($"name = {Quote(provider.DisplayName)}{document.NewLine}");
        lines.Add($"base_url = {Quote(provider.BaseUrl!)}{document.NewLine}");
        lines.Add($"wire_api = \"responses\"{document.NewLine}");
        lines.Add(document.NewLine);
        lines.Add($"[model_providers.{managedId}.auth]{document.NewLine}");
        lines.Add($"command = {Quote(Path.GetFullPath(switcherExecutablePath))}{document.NewLine}");
        lines.Add($"args = [\"token\", {Quote(provider.Id)}]{document.NewLine}");
        // 不寫 refresh_interval_ms：設為 0 會停用主動取金鑰，桌面版串流重連不走 401 補跑路徑，
        // 會導致請求從未帶上金鑰；使用 Codex 預設值讓金鑰命令在連線前主動執行。
        lines.Add($"timeout_ms = 5000{document.NewLine}");
        return document with { Lines = lines };
    }

    private static string? SelectReasoningEffort(ModelDefinition model)
    {
        if (model.ReasoningEfforts.Count == 0)
        {
            return null;
        }

        return model.ReasoningEfforts.Contains("high") ? "high" : model.ReasoningEfforts[^1];
    }

    private ConfigDocument BuildOpenAiConfig(ConfigDocument document)
    {
        var currentProvider = document.GetRootValue("model_provider") ?? "openai";
        var markers = ReadMarkers(document.Lines);
        if (!IsManagedProviderId(currentProvider) && markers.Count == 0)
        {
            return document;
        }

        if (IsLegacyMarkerSet(markers))
        {
            // 舊版標記缺少的鍵當時未被切換器改動，目前設定中的值即原始值。
            foreach (var key in ManagedRootKeys.Except(LegacyManagedRootKeys))
            {
                markers[key] = document.GetRootLine(key);
            }
        }
        else if (!IsCompleteMarkerSet(markers))
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
        var managedTables = document.Lines
            .Select(ParseTableName)
            .OfType<string>()
            .Where(IsManagedTableName)
            .ToList();
        if (provider.Id != "openai")
        {
            var managedId = ToManagedProviderId(provider.Id);
            if (providerId != managedId ||
                document.GetRootValue("model") != model.Id ||
                document.GetRootValue("model_reasoning_effort") != SelectReasoningEffort(model) ||
                document.GetRootValue("preferred_auth_method") != "apikey" ||
                document.GetRootValue("forced_login_method") != "api" ||
                document.GetRootValue("model_catalog_json") != ModelCatalogConfigValue ||
                !File.Exists(ModelCatalogPath) ||
                !document.HasTable($"model_providers.{managedId}") ||
                !document.HasTable($"model_providers.{managedId}.auth") ||
                managedTables.Any(table =>
                    table != $"model_providers.{managedId}" &&
                    table != $"model_providers.{managedId}.auth"))
            {
                throw new InvalidOperationException("暫存設定驗證失敗，原設定未變更。");
            }
        }
        else if (IsManagedProviderId(providerId) ||
                 managedTables.Count > 0 ||
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
            if (table is null || !IsManagedTableName(table))
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

    private static bool IsLegacyMarkerSet(Dictionary<string, string?> markers) =>
        markers.Count == LegacyManagedRootKeys.Length && LegacyManagedRootKeys.All(markers.ContainsKey);

    private static bool IsCompleteMarkerSet(Dictionary<string, string?> markers) =>
        markers.Count == ManagedRootKeys.Length && ManagedRootKeys.All(markers.ContainsKey);

    private static string ToManagedProviderId(string providerId) => ManagedProviderIdPrefix + providerId;

    private static bool IsManagedProviderId(string value) =>
        value.StartsWith(ManagedProviderIdPrefix, StringComparison.Ordinal);

    private static bool IsManagedTableName(string table) =>
        table.StartsWith("model_providers." + ManagedProviderIdPrefix, StringComparison.Ordinal);

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

        public string? GetTableValue(string table, string key)
        {
            var index = 0;
            while (index < Lines.Count && ParseTableName(Lines[index]) != table)
            {
                index++;
            }

            for (index++; index < Lines.Count && ParseTableName(Lines[index]) is null; index++)
            {
                if (IsAssignmentFor(Lines[index], key))
                {
                    return ParseTomlStringAssignment(TrimLineEnding(Lines[index]), key);
                }
            }

            return null;
        }
    }

    [GeneratedRegex(".*?(?:\\r\\n|\\n|\\r|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();

    [GeneratedRegex("^\\s*(?:\\[\\[([^\\[\\]]+)\\]\\]|\\[([^\\[\\]]+)\\])\\s*(?:#.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TablePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedSourceIdPattern();
}

/// <summary>
/// 產生 Codex 的模型目錄檔（model_catalog_json 指向的 models.json）。
/// 欄位形狀依 DeepSeek 官方 Codex 整合文件公布的目錄內容，
/// 只包含目前供應商的模型，避免跨供應商誤用。
/// </summary>
internal static class CodexModelCatalog
{
    private static readonly JsonSerializerOptions CatalogWriteOptions = new() { WriteIndented = true };

    public static string BuildJson(ProviderDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.Models.Count == 0)
        {
            throw new InvalidOperationException("供應商沒有可用模型，無法產生模型目錄。");
        }

        var models = new JsonArray();
        var priority = 1;
        foreach (var model in provider.Models)
        {
            models.Add(BuildModelEntry(provider, model, priority++));
        }

        return new JsonObject { ["models"] = models }.ToJsonString(CatalogWriteOptions);
    }

    public static void Write(string catalogPath, ProviderDefinition provider)
    {
        var json = BuildJson(provider);
        using (var document = JsonDocument.Parse(json))
        {
            if (document.RootElement.GetProperty("models").GetArrayLength() == 0)
            {
                throw new InvalidOperationException("模型目錄內容為空，已停止切換。");
            }
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(catalogPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, json);
        try
        {
            File.Move(temporaryPath, catalogPath, true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // 暫存清理失敗不應掩蓋原始錯誤。
            }

            throw;
        }
    }

    private static JsonObject BuildModelEntry(ProviderDefinition provider, ModelDefinition model, int priority)
    {
        var levels = new JsonArray();
        foreach (var effort in model.ReasoningEfforts)
        {
            levels.Add(new JsonObject
            {
                ["effort"] = effort,
                ["description"] = $"Reasoning effort {effort}"
            });
        }

        var defaultLevel = model.ReasoningEfforts.Count == 0
            ? null
            : model.ReasoningEfforts.Contains("high")
                ? "high"
                : model.ReasoningEfforts[^1];

        var modalities = new JsonArray { "text" };
        if (model.SupportsImages)
        {
            modalities.Add("image");
        }

        return new JsonObject
        {
            ["slug"] = model.Id,
            ["display_name"] = model.DisplayName,
            ["description"] = $"{provider.DisplayName} model served through Codex Model Switcher.",
            ["default_reasoning_level"] = defaultLevel,
            ["supported_reasoning_levels"] = levels,
            ["shell_type"] = "shell_command",
            ["visibility"] = "list",
            ["supported_in_api"] = true,
            ["priority"] = priority,
            ["prefer_websockets"] = false,
            ["support_verbosity"] = true,
            ["default_verbosity"] = "low",
            ["apply_patch_tool_type"] = "freeform",
            ["web_search_tool_type"] = "text",
            ["input_modalities"] = modalities,
            ["supports_image_detail_original"] = false,
            ["truncation_policy"] = new JsonObject { ["mode"] = "tokens", ["limit"] = 10000 },
            ["supports_parallel_tool_calls"] = true,
            ["tool_mode"] = null,
            ["multi_agent_version"] = "v2",
            ["use_responses_lite"] = false,
            ["include_skills_usage_instructions"] = false,
            ["auto_review_model_override"] = null,
            ["context_window"] = model.ContextWindow,
            ["max_context_window"] = model.ContextWindow,
            ["effective_context_window_percent"] = 95,
            ["auto_compact_token_limit"] = null,
            ["comp_hash"] = "3000",
            ["reasoning_summary_format"] = "experimental",
            ["default_reasoning_summary"] = "none",
            ["minimal_client_version"] = "0.144.0",
            ["availability_nux"] = null,
            ["upgrade"] = null,
            ["experimental_supported_tools"] = new JsonArray(),
            ["supports_search_tool"] = false
        };
    }
}
