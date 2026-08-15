using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodexModelSwitcher;

internal sealed record ConnectionTestResult(
    bool Succeeded,
    string Title,
    string UserMessage,
    string DiagnosticSummary,
    int? InputTokens = null,
    int? OutputTokens = null);

internal sealed class ConnectionTester : IDisposable
{
    private const int MaximumResponseBytes = 64 * 1024;
    private const string VerificationToolName = "check_switcher";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;

    public ConnectionTester()
        : this(CreateProductionClient(), true)
    {
    }

    internal ConnectionTester(HttpClient httpClient)
        : this(httpClient, false)
    {
    }

    private ConnectionTester(HttpClient httpClient, bool ownsClient)
    {
        this.httpClient = httpClient;
        this.ownsClient = ownsClient;
        this.httpClient.Timeout = RequestTimeout;
        this.httpClient.MaxResponseContentBufferSize = MaximumResponseBytes;
    }

    public Task<ConnectionTestResult> TestAsync(
        ProviderDefinition provider,
        ModelDefinition model,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = model.Id,
            input = "Reply with exactly OK.",
            max_output_tokens = 16,
            stream = false
        };
        var success = new SafeDiagnostic("連線測試成功", "金鑰、模型與 Responses API 均可正常使用。", "success");
        return ExecuteAsync(provider, model, apiKey, payload, static _ => null, success, cancellationToken);
    }

    public Task<ConnectionTestResult> TestToolCallAsync(
        ProviderDefinition provider,
        ModelDefinition model,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = model.Id,
            input = "Call the check_switcher tool with status set to \"ok\".",
            tools = new object[]
            {
                new
                {
                    type = "function",
                    name = VerificationToolName,
                    description = "Report switcher connectivity status.",
                    parameters = new
                    {
                        type = "object",
                        properties = new { status = new { type = "string" } },
                        required = new[] { "status" }
                    }
                }
            },
            max_output_tokens = 128,
            stream = false
        };
        var success = new SafeDiagnostic("工具呼叫測試成功", "供應商已正確執行 Responses API 工具呼叫。", "success");
        return ExecuteAsync(
            provider,
            model,
            apiKey,
            payload,
            static root => HasFunctionCall(root, VerificationToolName)
                ? null
                : new SafeDiagnostic(
                    "模型未執行工具呼叫",
                    "供應商有回應，但沒有呼叫測試工具，無法確認工具呼叫相容性。",
                    "tool_call_missing"),
            success,
            cancellationToken);
    }

    private async Task<ConnectionTestResult> ExecuteAsync(
        ProviderDefinition provider,
        ModelDefinition model,
        string apiKey,
        object payload,
        Func<JsonElement, SafeDiagnostic?> validateBody,
        SafeDiagnostic successDiagnostic,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = BuildEndpoint(provider);
            ValidateInputs(provider, model, apiKey);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(payload, payload.GetType());

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var diagnostic = DiagnosticSanitizer.FromHttpStatus(response.StatusCode);
                return new ConnectionTestResult(
                    false,
                    diagnostic.Title,
                    diagnostic.UserMessage,
                    DiagnosticSanitizer.BuildSummary(provider, model, diagnostic, response.StatusCode));
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                responseStream,
                new JsonDocumentOptions { MaxDepth = 32 },
                cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Responses API root must be an object.");
            }

            if (!document.RootElement.TryGetProperty("object", out var objectType) ||
                objectType.ValueKind != JsonValueKind.String ||
                objectType.GetString() != "response")
            {
                throw new JsonException("Responses API object type is invalid.");
            }

            var inputTokens = ReadUsage(document.RootElement, "input_tokens");
            var outputTokens = ReadUsage(document.RootElement, "output_tokens");
            var bodyFailure = validateBody(document.RootElement);
            if (bodyFailure is not null)
            {
                return new ConnectionTestResult(
                    false,
                    bodyFailure.Title,
                    bodyFailure.UserMessage,
                    DiagnosticSanitizer.BuildSummary(provider, model, bodyFailure, response.StatusCode),
                    inputTokens,
                    outputTokens);
            }

            return new ConnectionTestResult(
                true,
                successDiagnostic.Title,
                successDiagnostic.UserMessage,
                DiagnosticSanitizer.BuildSummary(provider, model, successDiagnostic, response.StatusCode),
                inputTokens,
                outputTokens);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException or InvalidOperationException or ArgumentException)
        {
            var diagnostic = DiagnosticSanitizer.FromException(exception);
            return new ConnectionTestResult(
                false,
                diagnostic.Title,
                diagnostic.UserMessage,
                DiagnosticSanitizer.BuildSummary(provider, model, diagnostic));
        }
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }

    private static HttpClient CreateProductionClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        return new HttpClient(handler, true);
    }

    private static Uri BuildEndpoint(ProviderDefinition provider)
    {
        if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("供應商網址不符合安全要求。");
        }

        var normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + '/', UriKind.Absolute);
        return new Uri(normalized, "responses");
    }

    private static void ValidateInputs(ProviderDefinition provider, ModelDefinition model, string apiKey)
    {
        if (!provider.Enabled ||
            provider.Id == "openai" ||
            !provider.RequiresApiKey ||
            provider.Protocol != "responses" ||
            !provider.Models.Any(candidate => candidate.Id == model.Id) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("目前選擇無法執行第三方供應商連線測試。");
        }
    }

    private static bool HasFunctionCall(JsonElement root, string toolName)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() == "function_call" &&
                item.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String &&
                name.GetString() == toolName)
            {
                return true;
            }
        }

        return false;
    }

    private static int? ReadUsage(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty(propertyName, out var tokens) ||
            !tokens.TryGetInt32(out var value) ||
            value < 0)
        {
            return null;
        }

        return value;
    }
}
