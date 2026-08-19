import Foundation

public struct TestResult {
    public var success: Bool
    public var httpStatus: Int
    public var durationMs: Int
    public var message: String
    public var diagnosticSummary: String
}

public struct DeepSeekBalanceResponse: Codable {
    public var is_available: Bool?
    public var balance_infos: [BalanceInfo]?

    public struct BalanceInfo: Codable {
        public var currency: String?
        public var total_balance: String?
        public var granted_balance: String?
        public var topped_up_balance: String?
    }
}

public final class ConnectionTester {
    public static let shared = ConnectionTester()

    public static func describeHttpStatus(_ status: Int) -> String {
        switch status {
        case 200:
            return "連線測試成功：金鑰、模型與 Responses API 均可正常使用。"
        case 400:
            return "請求格式不相容：供應商拒絕測試格式。"
        case 401, 403:
            return "API Key 驗證失敗：請重新設定這個供應商的 API Key。"
        case 402:
            return "API 餘額不足：請至供應商帳戶確認餘額。"
        case 404:
            return "找不到 API 端點：供應商網址或 Responses API 可能已變更。"
        case 422:
            return "模型或參數無法使用：所選模型可能已更名或停用。"
        case 429:
            return "目前受到限流：請稍後再試；程式不會自動重試。"
        case 500:
            return "供應商服務異常：請稍後再試。"
        case 502, 503, 504:
            return "供應商暫時無法使用：可能過載或維護中。"
        case 0:
            return "連線失敗或逾時（30 秒）：請確認網路可用；程式不會自動重試。"
        default:
            return "連線測試未通過：供應商回傳 HTTP \(status)，程式未保存回應內容。"
        }
    }

    public func testConnection(providerId: String, modelId: String, baseUrl: String) async -> TestResult {
        if providerId == "openai" {
            return TestResult(
                success: true,
                httpStatus: 200,
                durationMs: 0,
                message: "OpenAI 沿用 Codex 原生登入，不需要連線測試。",
                diagnosticSummary: "供應商=openai；模型=原生；狀態=無需測試"
            )
        }

        guard let secret = KeychainVault.readKey(providerId: providerId), !secret.isEmpty else {
            return TestResult(
                success: false,
                httpStatus: 401,
                durationMs: 0,
                message: "尚未設定 \(providerId) 的 API Key，請先設定金鑰。",
                diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；狀態=缺少金鑰"
            )
        }

        let cleanBaseUrl = baseUrl.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard let url = URL(string: "\(cleanBaseUrl)/responses") else {
            return TestResult(
                success: false,
                httpStatus: 400,
                durationMs: 0,
                message: "API 網址格式無效（\(baseUrl)）。",
                diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；狀態=網址無效"
            )
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 30.0
        request.setValue("Bearer \(secret)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        let payload: [String: Any] = [
            "model": modelId,
            "input": "Reply with exactly OK.",
            "max_output_tokens": 16,
            "stream": false
        ]

        guard let httpBody = try? JSONSerialization.data(withJSONObject: payload) else {
            return TestResult(
                success: false,
                httpStatus: 400,
                durationMs: 0,
                message: "無法建立測試請求封包。",
                diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；狀態=封包異常"
            )
        }
        request.httpBody = httpBody

        let startTime = DispatchTime.now()

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            let endTime = DispatchTime.now()
            let nanoTime = endTime.uptimeNanoseconds - startTime.uptimeNanoseconds
            let durationMs = Int(nanoTime / 1_000_000)

            guard let httpResponse = response as? HTTPURLResponse else {
                return TestResult(
                    success: false,
                    httpStatus: 0,
                    durationMs: durationMs,
                    message: "連線未收到有效的 HTTP 回應。",
                    diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；HTTP=0"
                )
            }

            let status = httpResponse.statusCode
            if status == 200 {
                // Verify Responses API format
                if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   let obj = json["object"] as? String, obj == "response" {
                    return TestResult(
                        success: true,
                        httpStatus: 200,
                        durationMs: durationMs,
                        message: "連線測試成功：金鑰、模型與 Responses API 均可正常使用（耗時 \(durationMs) ms）。",
                        diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；HTTP=200（耗時 \(durationMs) ms）"
                    )
                } else {
                    return TestResult(
                        success: false,
                        httpStatus: 200,
                        durationMs: durationMs,
                        message: "連線測試未完全通過：供應商回傳 HTTP 200，但內容非標準 Responses API 格式。",
                        diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；HTTP=200（非標準回應）"
                    )
                }
            } else {
                let desc = Self.describeHttpStatus(status)
                return TestResult(
                    success: false,
                    httpStatus: status,
                    durationMs: durationMs,
                    message: "\(desc)（耗時 \(durationMs) ms）",
                    diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；HTTP=\(status)"
                )
            }
        } catch {
            let endTime = DispatchTime.now()
            let nanoTime = endTime.uptimeNanoseconds - startTime.uptimeNanoseconds
            let durationMs = Int(nanoTime / 1_000_000)
            return TestResult(
                success: false,
                httpStatus: 0,
                durationMs: durationMs,
                message: "連線失敗或逾時（\(error.localizedDescription)）。",
                diagnosticSummary: "供應商=\(providerId)；模型=\(modelId)；狀態=連線異常"
            )
        }
    }

    public func fetchDeepSeekBalance() async -> String? {
        guard let secret = KeychainVault.readKey(providerId: "deepseek"), !secret.isEmpty else {
            return nil
        }
        guard let url = URL(string: "https://api.deepseek.com/user/balance") else { return nil }

        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.timeoutInterval = 5.0
        request.setValue("Bearer \(secret)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                return nil
            }
            let decoder = JSONDecoder()
            let balanceObj = try decoder.decode(DeepSeekBalanceResponse.self, from: data)
            if balanceObj.is_available == false {
                return "餘額不足或已停用"
            }
            if let infos = balanceObj.balance_infos, !infos.isEmpty {
                let formatted = infos.compactMap { info -> String? in
                    if let total = info.total_balance, let curr = info.currency {
                        return "\(total) \(curr)"
                    }
                    return nil
                }.joined(separator: ", ")
                return formatted.isEmpty ? nil : formatted
            }
            return nil
        } catch {
            return nil
        }
    }
}
