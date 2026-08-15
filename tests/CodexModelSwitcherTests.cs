using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CodexModelSwitcher.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "codex-switcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 暫存目錄清不掉不影響測試結果。
        }
    }
}

internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "需要 Windows 認證管理員，僅在 Windows 上執行。";
        }
    }
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public Uri? LastUri { get; private set; }

    public string? LastAuthorization { get; private set; }

    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }
}

internal static class TestProviders
{
    public static ProviderDefinition OpenAi() => new()
    {
        Id = "openai",
        DisplayName = "OpenAI（Codex 原生）",
        Enabled = true,
        RequiresApiKey = false,
        Protocol = "responses",
        Models =
        [
            new ModelDefinition { Id = "codex-native", DisplayName = "使用 Codex 原生模型選單", SupportsImages = true }
        ]
    };

    public static ProviderDefinition DeepSeek() => new()
    {
        Id = "deepseek",
        DisplayName = "DeepSeek",
        Enabled = true,
        BaseUrl = "https://api.deepseek.com",
        RequiresApiKey = true,
        Protocol = "responses",
        Models =
        [
            new ModelDefinition
            {
                Id = "deepseek-v4-flash",
                DisplayName = "DeepSeek V4 Flash",
                ContextWindow = 1_000_000,
                ReasoningEfforts = ["high", "xhigh"]
            },
            new ModelDefinition
            {
                Id = "deepseek-v4-pro",
                DisplayName = "DeepSeek V4 Pro",
                ContextWindow = 1_000_000,
                ReasoningEfforts = ["high", "xhigh"]
            }
        ]
    };

    public static ProviderDefinition MiniMax() => new()
    {
        Id = "minimax",
        DisplayName = "MiniMax",
        Enabled = true,
        BaseUrl = "https://api.minimax.io/v1",
        RequiresApiKey = true,
        Protocol = "responses",
        Models =
        [
            new ModelDefinition
            {
                Id = "MiniMax-M3",
                DisplayName = "MiniMax M3",
                ContextWindow = 1_000_000,
                SupportsImages = true,
                ReasoningEfforts = []
            },
            new ModelDefinition
            {
                Id = "MiniMax-M2.7",
                DisplayName = "MiniMax M2.7",
                ContextWindow = 204_800,
                ReasoningEfforts = []
            }
        ]
    };
}

public sealed class ProviderCatalogTests
{
    [Fact]
    public void Load_RepositoryCatalog_ListsAllThreeVerifiedProviders()
    {
        var result = ProviderCatalog.Load();

        Assert.Equal(3, result.Providers.Count);
        Assert.Contains(result.Providers, provider => provider.Id == "openai");
        var deepSeek = result.Providers.Single(provider => provider.Id == "deepseek");
        Assert.Equal(2, deepSeek.Models.Count);
        var miniMax = result.Providers.Single(provider => provider.Id == "minimax");
        Assert.Equal("responses", miniMax.Protocol);
        Assert.Equal("https://api.minimax.io/v1", miniMax.BaseUrl);
        Assert.Equal(new[] { "MiniMax-M3", "MiniMax-M2.7" }, miniMax.Models.Select(model => model.Id));
        Assert.Contains("已載入 3 個可用供應商", result.Notice);
    }

    [Fact]
    public void Load_ValidCatalogWithDisabledProvider_ListsEnabledAndExplainsDisabled()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 1,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "openai",
                  "displayName": "OpenAI（Codex 原生）",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": [] }
                  ]
                },
                {
                  "id": "acme",
                  "displayName": "Acme Responses",
                  "enabled": true,
                  "baseUrl": "https://api.acme.example",
                  "requiresApiKey": true,
                  "protocol": "responses",
                  "models": [
                    { "id": "acme-large", "displayName": "Acme Large", "contextWindow": 200000, "supportsImages": false, "reasoningEfforts": ["high"] }
                  ]
                },
                {
                  "id": "paused",
                  "displayName": "Paused Provider",
                  "enabled": false,
                  "unavailableReason": "尚未通過驗證",
                  "models": []
                }
              ]
            }
            """);

        Assert.Equal(2, result.Providers.Count);
        Assert.Contains(result.Providers, provider => provider.Id == "acme");
        Assert.Contains("已載入 2 個供應商", result.Notice);
        Assert.Contains("尚未通過驗證", result.Notice);
    }

    [Fact]
    public void Load_BrokenJson_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("{ 這不是合法的 JSON");

        AssertSafeCatalog(result, "無法解析");
    }

    [Fact]
    public void Load_CatalogWithoutOpenAi_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 1,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "acme",
                  "displayName": "Acme Responses",
                  "enabled": true,
                  "baseUrl": "https://api.acme.example",
                  "requiresApiKey": true,
                  "protocol": "responses",
                  "models": [
                    { "id": "acme-large", "displayName": "Acme Large", "contextWindow": 200000, "supportsImages": false, "reasoningEfforts": [] }
                  ]
                }
              ]
            }
            """);

        AssertSafeCatalog(result, "格式不正確");
    }

    [Fact]
    public void Load_UnsupportedSchemaVersion_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 2,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "openai",
                  "displayName": "OpenAI（Codex 原生）",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": [] }
                  ]
                }
              ]
            }
            """);

        AssertSafeCatalog(result, "格式不正確");
    }

    [Fact]
    public void Load_NonHttpsBaseUrl_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 1,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "openai",
                  "displayName": "OpenAI（Codex 原生）",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": [] }
                  ]
                },
                {
                  "id": "acme",
                  "displayName": "Acme Responses",
                  "enabled": true,
                  "baseUrl": "http://api.acme.example",
                  "requiresApiKey": true,
                  "protocol": "responses",
                  "models": [
                    { "id": "acme-large", "displayName": "Acme Large", "contextWindow": 200000, "supportsImages": false, "reasoningEfforts": [] }
                  ]
                }
              ]
            }
            """);

        AssertSafeCatalog(result, "格式不正確");
    }

    [Fact]
    public void Load_DuplicateProviderIds_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 1,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "openai",
                  "displayName": "OpenAI（Codex 原生）",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": [] }
                  ]
                },
                {
                  "id": "openai",
                  "displayName": "OpenAI 副本",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": [] }
                  ]
                }
              ]
            }
            """);

        AssertSafeCatalog(result, "格式不正確");
    }

    [Fact]
    public void Load_ControlCharactersInDisplayName_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 1,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "openai",
                  "displayName": "Open\u0001AI",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": [] }
                  ]
                }
              ]
            }
            """);

        AssertSafeCatalog(result, "格式不正確");
    }

    [Fact]
    public void Load_UnknownReasoningEffort_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 1,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "openai",
                  "displayName": "OpenAI（Codex 原生）",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": ["turbo"] }
                  ]
                }
              ]
            }
            """);

        AssertSafeCatalog(result, "格式不正確");
    }

    [Fact]
    public void Load_DisabledProviderWithoutReason_FallsBackToSafeCatalog()
    {
        var result = LoadCatalogText("""
            {
              "schemaVersion": 1,
              "verifiedAt": "2026-08-15",
              "providers": [
                {
                  "id": "openai",
                  "displayName": "OpenAI（Codex 原生）",
                  "enabled": true,
                  "requiresApiKey": false,
                  "protocol": "responses",
                  "models": [
                    { "id": "codex-native", "displayName": "使用 Codex 原生模型選單", "supportsImages": true, "reasoningEfforts": [] }
                  ]
                },
                {
                  "id": "paused",
                  "displayName": "Paused Provider",
                  "enabled": false,
                  "models": []
                }
              ]
            }
            """);

        AssertSafeCatalog(result, "格式不正確");
    }

    private static ProviderCatalogLoadResult LoadCatalogText(string json)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "providers.json"), json);
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(temp.Root);
        try
        {
            return ProviderCatalog.Load();
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    private static void AssertSafeCatalog(ProviderCatalogLoadResult result, string expectedNoticeFragment)
    {
        var provider = Assert.Single(result.Providers);
        Assert.Equal("openai", provider.Id);
        Assert.False(provider.RequiresApiKey);
        var model = Assert.Single(provider.Models);
        Assert.Equal("codex-native", model.Id);
        Assert.Contains(expectedNoticeFragment, result.Notice);
    }
}

public sealed class CodexConfigManagerTests
{
    private const string SampleConfig = """
        # 黃老闆的既有設定
        model = "gpt-5-codex"
        model_provider = "openai"
        model_reasoning_effort = "medium"
        approval_policy = "on-request"

        [mcp_servers.docs]
        command = "docs-server"
        args = ["--port", "8123"]

        [projects."C:\\work\\demo"]
        trust_level = "trusted"

        """;

    [Fact]
    public void ReadCurrent_WithoutConfig_AsksUserToStartCodexFirst()
    {
        using var temp = new TempDirectory();
        var manager = CodexConfigManager.ForTest(temp.Root);

        var current = manager.ReadCurrent();

        Assert.False(current.ConfigExists);
        Assert.False(manager.ConfigExists);
        Assert.Contains("先啟動一次 Codex", current.Notice);
    }

    [Fact]
    public void ApplySelection_ToDeepSeek_RewritesModelKeysAndPreservesEverythingElse()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        var configPath = ConfigPath(temp);

        var result = manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));

        Assert.Equal("DeepSeek", result.ProviderDisplayName);
        Assert.Equal("DeepSeek V4 Flash", result.ModelDisplayName);
        var text = File.ReadAllText(configPath);
        Assert.Contains("model = \"deepseek-v4-flash\"", text);
        Assert.Contains("model_provider = \"codex-switcher-deepseek\"", text);
        Assert.Contains("model_reasoning_effort = \"high\"", text);
        Assert.Contains("[model_providers.codex-switcher-deepseek]", text);
        Assert.Contains("base_url = \"https://api.deepseek.com\"", text);
        Assert.Contains("wire_api = \"responses\"", text);
        Assert.Contains("[model_providers.codex-switcher-deepseek.auth]", text);
        Assert.Contains($"command = {JsonSerializer.Serialize(Path.GetFullPath(FakeSwitcherPath(temp)))}", text);
        Assert.Contains("args = [\"token\", \"deepseek\"]", text);

        Assert.Contains("# 黃老闆的既有設定", text);
        Assert.Contains("approval_policy = \"on-request\"", text);
        Assert.Contains("[mcp_servers.docs]", text);
        Assert.Contains("command = \"docs-server\"", text);
        Assert.Contains("args = [\"--port\", \"8123\"]", text);
        Assert.Contains("""[projects."C:\\work\\demo"]""", text);
        Assert.Contains("trust_level = \"trusted\"", text);
    }

    [Fact]
    public void ApplySelection_ToDeepSeek_SavesOriginalRootLinesAsMarkers()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);

        manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));

        var text = File.ReadAllText(ConfigPath(temp));
        Assert.Contains(Marker("model", "model = \"gpt-5-codex\""), text);
        Assert.Contains(Marker("model_provider", "model_provider = \"openai\""), text);
        Assert.Contains(Marker("model_reasoning_effort", "model_reasoning_effort = \"medium\""), text);
    }

    [Fact]
    public void ApplySelection_BackToOpenAi_RestoresOriginalModelLines()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));

        manager.ApplySelection(TestProviders.OpenAi(), TestProviders.OpenAi().Models[0], FakeSwitcherPath(temp));

        var text = File.ReadAllText(ConfigPath(temp));
        Assert.Contains("model = \"gpt-5-codex\"", text);
        Assert.Contains("model_provider = \"openai\"", text);
        Assert.Contains("model_reasoning_effort = \"medium\"", text);
        Assert.DoesNotContain("codex-switcher-deepseek", text);
        Assert.DoesNotContain("# codex-model-switcher-saved-", text);
        Assert.Contains("[mcp_servers.docs]", text);
        Assert.Contains("""[projects."C:\\work\\demo"]""", text);

        var current = manager.ReadCurrent();
        Assert.Equal("openai", current.ProviderId);
        Assert.Equal("gpt-5-codex", current.ModelId);
    }

    [Fact]
    public void ApplySelection_OpenAiWhenAlreadyOpenAi_DoesNotTouchFileOrCreateBackups()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        var before = File.ReadAllBytes(ConfigPath(temp));

        manager.ApplySelection(TestProviders.OpenAi(), TestProviders.OpenAi().Models[0], FakeSwitcherPath(temp));

        Assert.Equal(before, File.ReadAllBytes(ConfigPath(temp)));
        Assert.False(Directory.Exists(BackupsDirectory(temp)));
        Assert.False(manager.CanRestoreOriginal);
    }

    [Fact]
    public void ApplySelection_ProviderNotSafeForSwitching_Throws()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        var before = File.ReadAllBytes(ConfigPath(temp));
        var chatOnly = new ProviderDefinition
        {
            Id = "chat-only",
            DisplayName = "Chat Only",
            Enabled = true,
            BaseUrl = "https://api.chat.example",
            RequiresApiKey = true,
            Protocol = "chat_completions",
            Models = [new ModelDefinition { Id = "chat-1", DisplayName = "Chat 1" }]
        };
        var insecure = TestProviders.DeepSeek();
        insecure.BaseUrl = "http://api.deepseek.com";
        var foreignModel = new ModelDefinition { Id = "not-in-catalog", DisplayName = "目錄外模型" };

        var chatException = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(chatOnly, chatOnly.Models[0], FakeSwitcherPath(temp)));
        var insecureException = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(insecure, insecure.Models[0], FakeSwitcherPath(temp)));
        var foreignException = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), foreignModel, FakeSwitcherPath(temp)));

        Assert.Contains("尚未開放", chatException.Message);
        Assert.Contains("尚未開放", insecureException.Message);
        Assert.Contains("尚未開放", foreignException.Message);
        Assert.Equal(before, File.ReadAllBytes(ConfigPath(temp)));
    }

    [Fact]
    public void ApplySelection_ToMiniMax_WritesManagedConfigWithoutReasoningEffort()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);

        var result = manager.ApplySelection(TestProviders.MiniMax(), M3(), FakeSwitcherPath(temp));

        Assert.Equal("MiniMax", result.ProviderDisplayName);
        Assert.Equal("MiniMax M3", result.ModelDisplayName);
        var text = File.ReadAllText(ConfigPath(temp));
        Assert.Contains("model = \"MiniMax-M3\"", text);
        Assert.Contains("model_provider = \"codex-switcher-minimax\"", text);
        Assert.DoesNotContain("model_reasoning_effort =", text);
        Assert.Contains(Marker("model_reasoning_effort", "model_reasoning_effort = \"medium\""), text);
        Assert.Contains("[model_providers.codex-switcher-minimax]", text);
        Assert.Contains("name = \"MiniMax\"", text);
        Assert.Contains("base_url = \"https://api.minimax.io/v1\"", text);
        Assert.Contains("wire_api = \"responses\"", text);
        Assert.Contains("[model_providers.codex-switcher-minimax.auth]", text);
        Assert.Contains("args = [\"token\", \"minimax\"]", text);
        Assert.Contains("[mcp_servers.docs]", text);
    }

    [Fact]
    public void ApplySelection_DeepSeekToMiniMax_SwitchesDirectlyWithoutLosingOriginal()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));

        manager.ApplySelection(TestProviders.MiniMax(), M3(), FakeSwitcherPath(temp));

        var text = File.ReadAllText(ConfigPath(temp));
        Assert.Contains("model_provider = \"codex-switcher-minimax\"", text);
        Assert.DoesNotContain("codex-switcher-deepseek", text);
        Assert.Contains(Marker("model", "model = \"gpt-5-codex\""), text);

        manager.ApplySelection(TestProviders.OpenAi(), TestProviders.OpenAi().Models[0], FakeSwitcherPath(temp));
        var restored = File.ReadAllText(ConfigPath(temp));
        Assert.Contains("model = \"gpt-5-codex\"", restored);
        Assert.Contains("model_provider = \"openai\"", restored);
        Assert.Contains("model_reasoning_effort = \"medium\"", restored);
        Assert.DoesNotContain("codex-switcher-", restored);
        Assert.DoesNotContain("# codex-model-switcher-saved-", restored);
        Assert.Contains("[mcp_servers.docs]", restored);

        var current = manager.ReadCurrent();
        Assert.Equal("openai", current.ProviderId);
        Assert.Equal("gpt-5-codex", current.ModelId);
    }

    [Fact]
    public void ReadCurrent_AfterMiniMaxSwitch_ShowsDisplayNameFromConfig()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        manager.ApplySelection(TestProviders.MiniMax(), M3(), FakeSwitcherPath(temp));

        var current = manager.ReadCurrent();

        Assert.True(current.ConfigExists);
        Assert.Equal("codex-switcher-minimax", current.ProviderId);
        Assert.Equal("MiniMax", current.ProviderDisplayName);
        Assert.Equal("MiniMax-M3", current.ModelId);
    }

    [Fact]
    public void ApplySelection_WithNonUtf8Config_StopsAndKeepsFile()
    {
        using var temp = new TempDirectory();
        byte[] invalid = [0xC3, 0x28, 0x0A];
        File.WriteAllBytes(ConfigPath(temp), invalid);
        var manager = CodexConfigManager.ForTest(temp.Root);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp)));

        Assert.Contains("UTF-8", exception.Message);
        Assert.Equal(invalid, File.ReadAllBytes(ConfigPath(temp)));
    }

    [Fact]
    public void ApplySelection_WithDuplicateModelKey_StopsAndKeepsFile()
    {
        using var temp = new TempDirectory();
        const string config = "model = \"a\"\nmodel = \"b\"\n";
        var manager = CreateManager(temp.Root, config);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp)));

        Assert.Contains("model", exception.Message);
        Assert.Equal(config, File.ReadAllText(ConfigPath(temp)));
    }

    [Fact]
    public void ApplySelection_WithNonStringModelValue_StopsAndKeepsFile()
    {
        using var temp = new TempDirectory();
        const string config = "model = 3\n";
        var manager = CreateManager(temp.Root, config);

        Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp)));

        Assert.Equal(config, File.ReadAllText(ConfigPath(temp)));
    }

    [Fact]
    public void ApplySelection_ManagedConfigWithoutMarkers_StopsInBothDirections()
    {
        using var temp = new TempDirectory();
        const string managedConfig = """
            model = "deepseek-v4-flash"
            model_provider = "codex-switcher-deepseek"
            model_reasoning_effort = "high"

            """;
        var manager = CreateManager(temp.Root, managedConfig);

        var toOpenAi = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.OpenAi(), TestProviders.OpenAi().Models[0], FakeSwitcherPath(temp)));
        var toDeepSeek = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp)));

        Assert.Contains("標記", toOpenAi.Message);
        Assert.Contains("標記", toDeepSeek.Message);
        Assert.Equal(managedConfig, File.ReadAllText(ConfigPath(temp)));
    }

    [Fact]
    public void ApplySelection_WithCorruptedMarker_Stops()
    {
        using var temp = new TempDirectory();
        var config = SampleConfig + "# codex-model-switcher-saved-model: !!!\n";
        var manager = CreateManager(temp.Root, config);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp)));

        Assert.Contains("損壞", exception.Message);
        Assert.Equal(config, File.ReadAllText(ConfigPath(temp)));
    }

    [Fact]
    public void ApplySelection_WithDuplicatedMarkers_Stops()
    {
        using var temp = new TempDirectory();
        var marker = Marker("model", "model = \"old\"");
        var config = SampleConfig + marker + "\n" + marker + "\n";
        var manager = CreateManager(temp.Root, config);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp)));

        Assert.Contains("重複", exception.Message);
        Assert.Equal(config, File.ReadAllText(ConfigPath(temp)));
    }

    [Fact]
    public void ApplySelection_WhenAnotherSwitchHoldsTheLock_Throws()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        var lockPath = Path.Combine(temp.Root, "model-switcher", "config.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllText(lockPath, string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp)));

        Assert.Contains("另一個切換作業", exception.Message);
    }

    [Fact]
    public void ApplySelection_PreservesUtf8Bom()
    {
        using var temp = new TempDirectory();
        var withBom = Encoding.UTF8.Preamble.ToArray()
            .Concat(Encoding.UTF8.GetBytes(SampleConfig))
            .ToArray();
        File.WriteAllBytes(ConfigPath(temp), withBom);
        var manager = CodexConfigManager.ForTest(temp.Root);

        manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));

        var bytes = File.ReadAllBytes(ConfigPath(temp));
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Fact]
    public void ApplySelection_PreservesCrlfNewlines()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root, SampleConfig.Replace("\n", "\r\n"));

        manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));

        var text = File.ReadAllText(ConfigPath(temp));
        Assert.Contains("model_provider = \"codex-switcher-deepseek\"\r\n", text);
        Assert.DoesNotContain('\n', text.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void ApplySelection_ManySwitches_KeepsOriginalAndFiveRecentBackups()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        var originalBytes = File.ReadAllBytes(ConfigPath(temp));
        Assert.Null(manager.OriginalBackupTime);

        for (var round = 0; round < 7; round++)
        {
            var model = round % 2 == 0 ? Flash() : Pro();
            manager.ApplySelection(TestProviders.DeepSeek(), model, FakeSwitcherPath(temp));
        }

        Assert.True(manager.CanRestoreOriginal);
        Assert.NotNull(manager.OriginalBackupTime);
        Assert.Equal(originalBytes, File.ReadAllBytes(Path.Combine(BackupsDirectory(temp), "original-config.toml")));
        Assert.Equal(5, Directory.GetFiles(BackupsDirectory(temp), "switch-*.toml").Length);
    }

    [Fact]
    public void RestoreOriginal_BringsBackExactOriginalBytes()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        var originalBytes = File.ReadAllBytes(ConfigPath(temp));
        manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));
        Assert.NotEqual(originalBytes, File.ReadAllBytes(ConfigPath(temp)));

        manager.RestoreOriginal();

        Assert.Equal(originalBytes, File.ReadAllBytes(ConfigPath(temp)));
        Assert.True(Directory.GetFiles(BackupsDirectory(temp), "switch-*.toml").Length >= 2);
    }

    [Fact]
    public void RestoreOriginal_WithoutBackup_Throws()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);

        var exception = Assert.Throws<InvalidOperationException>(manager.RestoreOriginal);

        Assert.Contains("找不到首次切換前", exception.Message);
    }

    [Fact]
    public void ReadCurrent_AfterDeepSeekSwitch_ShowsManagedProviderAndModel()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp.Root);
        manager.ApplySelection(TestProviders.DeepSeek(), Flash(), FakeSwitcherPath(temp));

        var current = manager.ReadCurrent();

        Assert.True(current.ConfigExists);
        Assert.Equal("codex-switcher-deepseek", current.ProviderId);
        Assert.Equal("DeepSeek", current.ProviderDisplayName);
        Assert.Equal("deepseek-v4-flash", current.ModelId);
    }

    private static CodexConfigManager CreateManager(string home, string configText = SampleConfig)
    {
        File.WriteAllText(Path.Combine(home, "config.toml"), configText);
        return CodexConfigManager.ForTest(home);
    }

    private static ModelDefinition Flash() => TestProviders.DeepSeek().Models[0];

    private static ModelDefinition Pro() => TestProviders.DeepSeek().Models[1];

    private static ModelDefinition M3() => TestProviders.MiniMax().Models[0];

    private static string ConfigPath(TempDirectory temp) => Path.Combine(temp.Root, "config.toml");

    private static string BackupsDirectory(TempDirectory temp) => Path.Combine(temp.Root, "model-switcher", "backups");

    private static string FakeSwitcherPath(TempDirectory temp) => Path.Combine(temp.Root, "CodexModelSwitcher.exe");

    private static string Marker(string key, string originalLine) =>
        $"# codex-model-switcher-saved-{key}: {Convert.ToBase64String(Encoding.UTF8.GetBytes(originalLine))}";
}

public sealed class BackupManagerTests
{
    [Fact]
    public void SaveBeforeSwitch_KeepsTheFirstOriginalForever()
    {
        using var temp = new TempDirectory();
        var backupManager = new BackupManager(temp.Root);

        backupManager.SaveBeforeSwitch("第一版"u8.ToArray());
        backupManager.SaveBeforeSwitch("第二版"u8.ToArray());

        Assert.Equal("第一版"u8.ToArray(), backupManager.ReadOriginal());
        Assert.Equal(2, Directory.GetFiles(BackupsDirectory(temp), "switch-*.toml").Length);
    }

    [Fact]
    public void SaveBeforeRestore_DoesNotCreateOriginalBackup()
    {
        using var temp = new TempDirectory();
        var backupManager = new BackupManager(temp.Root);

        backupManager.SaveBeforeRestore("目前設定"u8.ToArray());

        Assert.False(backupManager.HasOriginalBackup);
        Assert.Single(Directory.GetFiles(BackupsDirectory(temp), "switch-*.toml"));
    }

    [Fact]
    public void ReadOriginal_WithoutBackup_Throws()
    {
        using var temp = new TempDirectory();
        var backupManager = new BackupManager(temp.Root);

        Assert.Throws<InvalidOperationException>(() => backupManager.ReadOriginal());
    }

    private static string BackupsDirectory(TempDirectory temp) => Path.Combine(temp.Root, "model-switcher", "backups");
}

public sealed class CredentialVaultValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Save_WithEmptySecret_Throws(string secret)
    {
        Assert.Throws<ArgumentException>(() => CredentialVault.Save("deepseek", secret));
    }

    [Fact]
    public void Save_WithOverlongSecret_Throws()
    {
        Assert.Throws<ArgumentException>(() => CredentialVault.Save("deepseek", new string('a', 1201)));
    }

    [Fact]
    public void Save_WithControlCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => CredentialVault.Save("deepseek", "abc\ndef"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("slash/id")]
    [InlineData("中文識別碼")]
    public void AllOperations_WithInvalidProviderId_Throw(string providerId)
    {
        Assert.Throws<ArgumentException>(() => CredentialVault.Exists(providerId));
        Assert.Throws<ArgumentException>(() => CredentialVault.Save(providerId, "secret"));
        Assert.Throws<ArgumentException>(() => CredentialVault.Read(providerId));
        Assert.Throws<ArgumentException>(() => CredentialVault.Delete(providerId));
    }

    [Fact]
    public void ProviderId_LongerThan48Characters_Throws()
    {
        Assert.Throws<ArgumentException>(() => CredentialVault.Exists(new string('a', 49)));
    }
}

public sealed class CredentialVaultWindowsTests
{
    [WindowsOnlyFact]
    public void SaveReadDelete_RoundTripsThroughWindowsCredentialManager()
    {
        var providerId = "test-vault-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            Assert.False(CredentialVault.Exists(providerId));

            CredentialVault.Save(providerId, "sk-test-secret-123");
            Assert.True(CredentialVault.Exists(providerId));
            Assert.Equal("sk-test-secret-123", CredentialVault.Read(providerId));

            CredentialVault.Save(providerId, "sk-test-secret-456");
            Assert.Equal("sk-test-secret-456", CredentialVault.Read(providerId));

            Assert.True(CredentialVault.Delete(providerId));
            Assert.False(CredentialVault.Exists(providerId));
            Assert.Throws<InvalidOperationException>(() => CredentialVault.Read(providerId));
            Assert.False(CredentialVault.Delete(providerId));
        }
        finally
        {
            try
            {
                CredentialVault.Delete(providerId);
            }
            catch (Win32Exception)
            {
                // 清理失敗不影響測試結論。
            }
        }
    }
}

public sealed class ConnectionTesterTests
{
    private const string ApiKey = "sk-unit-test-key";

    [Fact]
    public async Task TestAsync_WithValidResponse_ReportsSuccessAndTokenUsage()
    {
        var (result, handler) = await RunAsync(_ => JsonResponse(HttpStatusCode.OK,
            """{"object":"response","usage":{"input_tokens":12,"output_tokens":5}}"""));

        Assert.True(result.Succeeded);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(5, result.OutputTokens);
        Assert.Equal(new Uri("https://api.deepseek.com/responses"), handler.LastUri);
        Assert.Equal("Bearer " + ApiKey, handler.LastAuthorization);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("deepseek-v4-flash", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("Reply with exactly OK.", body.RootElement.GetProperty("input").GetString());
        Assert.Equal(16, body.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task TestAsync_KeepsBaseUrlPathWhenBuildingEndpoint()
    {
        var provider = TestProviders.DeepSeek();
        provider.BaseUrl = "https://gateway.example/v1";

        var (result, handler) = await RunAsync(
            _ => JsonResponse(HttpStatusCode.OK, """{"object":"response"}"""),
            provider);

        Assert.True(result.Succeeded);
        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
        Assert.Equal(new Uri("https://gateway.example/v1/responses"), handler.LastUri);
    }

    [Fact]
    public async Task TestAsync_WithUnauthorizedResponse_ReportsAuthenticationFailureWithoutKey()
    {
        var (result, _) = await RunAsync(_ => JsonResponse(HttpStatusCode.Unauthorized,
            """{"error":{"message":"bad key"}}"""));

        Assert.False(result.Succeeded);
        Assert.Equal("API Key 驗證失敗", result.Title);
        Assert.Contains("authentication_failed", result.DiagnosticSummary);
        Assert.Contains("HTTP=401", result.DiagnosticSummary);
        Assert.DoesNotContain(ApiKey, result.DiagnosticSummary);
        Assert.DoesNotContain(ApiKey, result.UserMessage);
    }

    [Theory]
    [InlineData(402, "insufficient_balance")]
    [InlineData(429, "rate_limited")]
    [InlineData(503, "service_unavailable")]
    public async Task TestAsync_WithFailureStatus_MapsToSafeCategory(int status, string category)
    {
        var (result, _) = await RunAsync(_ => new HttpResponseMessage((HttpStatusCode)status));

        Assert.False(result.Succeeded);
        Assert.Contains(category, result.DiagnosticSummary);
    }

    [Fact]
    public async Task TestAsync_WithNonJsonBody_ReportsInvalidResponse()
    {
        var (result, _) = await RunAsync(_ => JsonResponse(HttpStatusCode.OK, "這不是 JSON"));

        Assert.False(result.Succeeded);
        Assert.Equal("回應格式無法辨識", result.Title);
        Assert.Contains("invalid_response", result.DiagnosticSummary);
    }

    [Fact]
    public async Task TestAsync_WithWrongObjectType_ReportsInvalidResponse()
    {
        var (result, _) = await RunAsync(_ => JsonResponse(HttpStatusCode.OK,
            """{"object":"chat.completion"}"""));

        Assert.False(result.Succeeded);
        Assert.Contains("invalid_response", result.DiagnosticSummary);
    }

    [Fact]
    public async Task TestAsync_WithNetworkFailure_ReportsNetworkError()
    {
        var (result, _) = await RunAsync(_ => throw new HttpRequestException("boom"));

        Assert.False(result.Succeeded);
        Assert.Equal("無法建立安全連線", result.Title);
        Assert.Contains("network_error", result.DiagnosticSummary);
    }

    [Fact]
    public async Task TestAsync_WithCancellation_ReportsTimeout()
    {
        var (result, _) = await RunAsync(_ => throw new TaskCanceledException());

        Assert.False(result.Succeeded);
        Assert.Contains("timeout", result.DiagnosticSummary);
    }

    [Fact]
    public async Task TestAsync_WithOpenAiProvider_RefusesToSendAnything()
    {
        var (result, handler) = await RunAsync(
            _ => JsonResponse(HttpStatusCode.OK, """{"object":"response"}"""),
            TestProviders.OpenAi());

        Assert.False(result.Succeeded);
        Assert.Contains("invalid_state", result.DiagnosticSummary);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task TestAsync_WithModelOutsideProvider_RefusesToSendAnything()
    {
        var (result, handler) = await RunAsync(
            _ => JsonResponse(HttpStatusCode.OK, """{"object":"response"}"""),
            TestProviders.DeepSeek(),
            new ModelDefinition { Id = "other-model", DisplayName = "其他模型" });

        Assert.False(result.Succeeded);
        Assert.Contains("invalid_state", result.DiagnosticSummary);
        Assert.Null(handler.LastUri);
    }

    private static async Task<(ConnectionTestResult Result, StubHttpHandler Handler)> RunAsync(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        ProviderDefinition? provider = null,
        ModelDefinition? model = null)
    {
        var handler = new StubHttpHandler(respond);
        using var client = new HttpClient(handler);
        using var tester = new ConnectionTester(client);
        var chosenProvider = provider ?? TestProviders.DeepSeek();
        var chosenModel = model ?? chosenProvider.Models.FirstOrDefault() ?? new ModelDefinition
        {
            Id = "deepseek-v4-flash",
            DisplayName = "DeepSeek V4 Flash"
        };
        var result = await tester.TestAsync(chosenProvider, chosenModel, ApiKey);
        return (result, handler);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

public sealed class DiagnosticSanitizerTests
{
    [Theory]
    [InlineData(400, "invalid_format")]
    [InlineData(401, "authentication_failed")]
    [InlineData(403, "authentication_failed")]
    [InlineData(402, "insufficient_balance")]
    [InlineData(404, "endpoint_not_found")]
    [InlineData(422, "invalid_parameters")]
    [InlineData(429, "rate_limited")]
    [InlineData(500, "server_error")]
    [InlineData(502, "service_unavailable")]
    [InlineData(503, "service_unavailable")]
    [InlineData(504, "service_unavailable")]
    [InlineData(418, "http_418")]
    public void FromHttpStatus_MapsToExpectedCategory(int status, string category)
    {
        var diagnostic = DiagnosticSanitizer.FromHttpStatus((HttpStatusCode)status);

        Assert.Equal(category, diagnostic.SummaryCode);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Title));
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.UserMessage));
    }

    [Fact]
    public void FromException_MapsKnownExceptionTypes()
    {
        Assert.Equal("timeout", DiagnosticSanitizer.FromException(new OperationCanceledException()).SummaryCode);
        Assert.Equal("network_error", DiagnosticSanitizer.FromException(new HttpRequestException()).SummaryCode);
        Assert.Equal("invalid_response", DiagnosticSanitizer.FromException(new JsonException()).SummaryCode);
        Assert.Equal("invalid_state", DiagnosticSanitizer.FromException(new InvalidOperationException()).SummaryCode);
        Assert.Equal("unexpected_error", DiagnosticSanitizer.FromException(new NotSupportedException()).SummaryCode);
    }

    [Fact]
    public void BuildSummary_WithStatus_FormatsStableSummary()
    {
        var provider = TestProviders.DeepSeek();
        var diagnostic = DiagnosticSanitizer.FromHttpStatus(HttpStatusCode.Unauthorized);

        var summary = DiagnosticSanitizer.BuildSummary(provider, provider.Models[0], diagnostic, HttpStatusCode.Unauthorized);

        Assert.Equal("供應商=deepseek；模型=deepseek-v4-flash；HTTP=401；分類=authentication_failed", summary);
    }

    [Fact]
    public void BuildSummary_WithoutStatus_UsesNone()
    {
        var provider = TestProviders.DeepSeek();
        var diagnostic = DiagnosticSanitizer.FromException(new InvalidOperationException());

        var summary = DiagnosticSanitizer.BuildSummary(provider, provider.Models[0], diagnostic);

        Assert.Equal("供應商=deepseek；模型=deepseek-v4-flash；HTTP=none；分類=invalid_state", summary);
    }
}
