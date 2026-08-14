using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexModelSwitcher;

internal sealed class ProviderCatalogDocument
{
    public int SchemaVersion { get; set; }

    public string VerifiedAt { get; set; } = string.Empty;

    public List<ProviderDefinition> Providers { get; set; } = [];
}

internal sealed class ProviderDefinition
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? BaseUrl { get; set; }

    public bool RequiresApiKey { get; set; }

    public string Protocol { get; set; } = string.Empty;

    public string? UnavailableReason { get; set; }

    public List<ModelDefinition> Models { get; set; } = [];

    public override string ToString() => DisplayName;
}

internal sealed class ModelDefinition
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int? ContextWindow { get; set; }

    public bool SupportsImages { get; set; }

    public List<string> ReasoningEfforts { get; set; } = [];

    public override string ToString() => DisplayName;
}

internal sealed record ProviderCatalogLoadResult(
    IReadOnlyList<ProviderDefinition> Providers,
    string Notice);

internal static partial class ProviderCatalog
{
    private const int SupportedSchemaVersion = 1;
    private const int MaximumTextLength = 160;
    private const int MaximumProviderCount = 30;
    private const int MaximumModelsPerProvider = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false
    };

    public static ProviderCatalogLoadResult Load()
    {
        var catalogPath = FindCatalogPath();
        if (catalogPath is null)
        {
            return UseSafeCatalog("找不到供應商目錄，已使用 OpenAI 安全目錄。");
        }

        try
        {
            var json = File.ReadAllText(catalogPath);
            var document = JsonSerializer.Deserialize<ProviderCatalogDocument>(json, SerializerOptions);
            if (document is null || !TryValidate(document, out var enabledProviders, out var notice))
            {
                return UseSafeCatalog("供應商目錄格式不正確，已使用 OpenAI 安全目錄。");
            }

            return new ProviderCatalogLoadResult(enabledProviders, notice);
        }
        catch (JsonException)
        {
            return UseSafeCatalog("供應商目錄無法解析，已使用 OpenAI 安全目錄。");
        }
        catch (IOException)
        {
            return UseSafeCatalog("供應商目錄無法讀取，已使用 OpenAI 安全目錄。");
        }
        catch (UnauthorizedAccessException)
        {
            return UseSafeCatalog("供應商目錄沒有讀取權限，已使用 OpenAI 安全目錄。");
        }
    }

    private static string? FindCatalogPath()
    {
        var applicationCatalog = Path.Combine(AppContext.BaseDirectory, "providers.json");
        if (File.Exists(applicationCatalog))
        {
            return applicationCatalog;
        }

        var workingCatalog = Path.Combine(Environment.CurrentDirectory, "providers.json");
        if (File.Exists(workingCatalog))
        {
            return workingCatalog;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 6 && directory is not null; level++, directory = directory.Parent)
        {
            var repositoryCatalog = Path.Combine(directory.FullName, "providers.json");
            var gitDirectory = Path.Combine(directory.FullName, ".git");
            if (File.Exists(repositoryCatalog) && Directory.Exists(gitDirectory))
            {
                return repositoryCatalog;
            }
        }

        return null;
    }

    private static bool TryValidate(
        ProviderCatalogDocument document,
        out IReadOnlyList<ProviderDefinition> enabledProviders,
        out string notice)
    {
        enabledProviders = [];
        notice = string.Empty;

        if (document.SchemaVersion != SupportedSchemaVersion ||
            !DateOnly.TryParseExact(document.VerifiedAt, "yyyy-MM-dd", out _) ||
            document.Providers.Count is < 1 or > MaximumProviderCount)
        {
            return false;
        }

        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var availableProviders = new List<ProviderDefinition>();
        var unavailableProviders = new List<string>();

        foreach (var provider in document.Providers)
        {
            if (!IsSafeId(provider.Id) ||
                !IsSafeText(provider.DisplayName) ||
                !providerIds.Add(provider.Id))
            {
                return false;
            }

            if (!provider.Enabled)
            {
                if (!IsSafeText(provider.UnavailableReason))
                {
                    return false;
                }

                unavailableProviders.Add($"{provider.DisplayName}：{provider.UnavailableReason}");
                continue;
            }

            if (!string.Equals(provider.Protocol, "responses", StringComparison.Ordinal) ||
                provider.Models.Count is < 1 or > MaximumModelsPerProvider ||
                !HasValidBaseUrl(provider))
            {
                return false;
            }

            var modelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in provider.Models)
            {
                if (!IsSafeModelId(model.Id) ||
                    !IsSafeText(model.DisplayName) ||
                    !modelIds.Add(model.Id) ||
                    model.ContextWindow is <= 0)
                {
                    return false;
                }

                if (model.ReasoningEfforts.Any(effort =>
                        effort is not ("minimal" or "low" or "medium" or "high" or "xhigh")))
                {
                    return false;
                }
            }

            availableProviders.Add(provider);
        }

        if (availableProviders.Count == 0 ||
            !availableProviders.Any(provider =>
                string.Equals(provider.Id, "openai", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        enabledProviders = availableProviders;
        notice = unavailableProviders.Count == 0
            ? $"已載入 {availableProviders.Count} 個可用供應商。"
            : $"已載入 {availableProviders.Count} 個供應商；{string.Join("；", unavailableProviders)}";
        return true;
    }

    private static bool HasValidBaseUrl(ProviderDefinition provider)
    {
        if (string.Equals(provider.Id, "openai", StringComparison.OrdinalIgnoreCase) &&
            !provider.RequiresApiKey)
        {
            return string.IsNullOrWhiteSpace(provider.BaseUrl);
        }

        return Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool IsSafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumTextLength &&
        !value.Any(char.IsControl);

    private static bool IsSafeId(string value) =>
        value.Length <= 48 && ProviderIdPattern().IsMatch(value);

    private static bool IsSafeModelId(string value) =>
        value.Length <= 100 && ModelIdPattern().IsMatch(value);

    private static ProviderCatalogLoadResult UseSafeCatalog(string notice)
    {
        var openAi = new ProviderDefinition
        {
            Id = "openai",
            DisplayName = "OpenAI（Codex 原生）",
            Enabled = true,
            RequiresApiKey = false,
            Protocol = "responses",
            Models =
            [
                new ModelDefinition
                {
                    Id = "codex-native",
                    DisplayName = "使用 Codex 原生模型選單",
                    SupportsImages = true
                }
            ]
        };

        return new ProviderCatalogLoadResult([openAi], notice);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:/-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelIdPattern();
}
