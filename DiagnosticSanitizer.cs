using System.Net;

namespace CodexModelSwitcher;

internal sealed record SafeDiagnostic(
    string Title,
    string UserMessage,
    string SummaryCode);

internal static class DiagnosticSanitizer
{
    public static SafeDiagnostic FromHttpStatus(HttpStatusCode statusCode)
    {
        return (int)statusCode switch
        {
            400 => Create("請求格式不相容", "供應商拒絕測試格式，請更新供應商目錄或切換器。", "invalid_format"),
            401 or 403 => Create("API Key 驗證失敗", "請重新設定這個供應商的 API Key。", "authentication_failed"),
            402 => Create("API 餘額不足", "請至供應商帳戶確認餘額，或改用其他供應商。", "insufficient_balance"),
            404 => Create("找不到 API 端點", "供應商網址或 Responses API 可能已變更。", "endpoint_not_found"),
            422 => Create("模型或參數無法使用", "所選模型可能已更名、停用或不接受目前測試格式。", "invalid_parameters"),
            429 => Create("目前受到限流", "請稍後再試，或改用其他供應商；程式不會自動重試。", "rate_limited"),
            500 => Create("供應商服務異常", "供應商目前發生伺服器錯誤，請稍後再試。", "server_error"),
            502 or 503 or 504 => Create("供應商暫時無法使用", "供應商可能過載或維護中，請稍後再試。", "service_unavailable"),
            _ => Create("連線測試未通過", $"供應商回傳 HTTP {(int)statusCode}，程式未保存回應內容。", $"http_{(int)statusCode}")
        };
    }

    public static SafeDiagnostic FromException(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => Create("連線逾時", "供應商在 30 秒內沒有完成回應，程式已停止等待且不會自動重試。", "timeout"),
            HttpRequestException => Create("無法建立安全連線", "請確認網路可用，或供應商服務是否暫時中斷。", "network_error"),
            System.Text.Json.JsonException => Create("回應格式無法辨識", "供應商有回應，但不是切換器預期的 Responses 格式。", "invalid_response"),
            InvalidOperationException => Create("測試條件不完整", "供應商、模型或 API Key 狀態不符合測試要求。", "invalid_state"),
            _ => Create("未預期的測試錯誤", "測試已安全停止；診斷摘要不包含金鑰、路徑或回應內容。", "unexpected_error")
        };
    }

    public static string BuildSummary(
        ProviderDefinition provider,
        ModelDefinition model,
        SafeDiagnostic diagnostic,
        HttpStatusCode? statusCode = null)
    {
        var status = statusCode is null ? "none" : ((int)statusCode.Value).ToString();
        return $"供應商={provider.Id}；模型={model.Id}；HTTP={status}；分類={diagnostic.SummaryCode}";
    }

    private static SafeDiagnostic Create(string title, string message, string code) => new(title, message, code);
}
