import Foundation

public enum KeychainVault {
    private static let servicePrefix = "CodexModelSwitcher/provider/"
    public static let maxKeyLength = 1200

    public static func validateProviderId(_ providerId: String) -> Bool {
        guard !providerId.isEmpty, providerId.count <= 48 else { return false }
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-_"))
        return providerId.unicodeScalars.allSatisfy { allowed.contains($0) }
    }

    public static func validateSecret(_ secret: String) -> (isValid: Bool, message: String?) {
        let trimmed = secret.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty {
            return (false, "API Key 不能為空。")
        }
        if trimmed.count > maxKeyLength {
            return (false, "API Key 長度超過上限（\(maxKeyLength) 字元）。")
        }
        for scalar in trimmed.unicodeScalars {
            if CharacterSet.controlCharacters.contains(scalar) || scalar == "\"" || scalar == "\\" {
                return (false, "API Key 包含無效字元（不可包含引號、反斜線或控制字元）。")
            }
        }
        return (true, nil)
    }

    public static func exists(providerId: String) -> Bool {
        guard validateProviderId(providerId) else { return false }
        let service = "\(servicePrefix)\(providerId)"

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["find-generic-password", "-s", service, "-a", providerId]
        let outPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = Pipe()

        do {
            try process.run()
            _ = outPipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            return process.terminationStatus == 0
        } catch {
            return false
        }
    }

    public static func readKey(providerId: String) -> String? {
        guard validateProviderId(providerId) else { return nil }
        let service = "\(servicePrefix)\(providerId)"

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["find-generic-password", "-s", service, "-a", providerId, "-w"]
        let outPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = Pipe()

        do {
            try process.run()
            let data = outPipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            guard process.terminationStatus == 0 else { return nil }
            guard let str = String(data: data, encoding: .utf8) else { return nil }
            let trimmed = str.trimmingCharacters(in: .whitespacesAndNewlines)
            return trimmed.isEmpty ? nil : trimmed
        } catch {
            return nil
        }
    }

    public static func saveKey(providerId: String, secret: String) -> (success: Bool, message: String) {
        guard validateProviderId(providerId) else {
            return (false, "供應商識別碼格式不正確。")
        }
        let validation = validateSecret(secret)
        guard validation.isValid else {
            return (false, validation.message ?? "API Key 格式不正確。")
        }

        let cleanSecret = secret.trimmingCharacters(in: .whitespacesAndNewlines)
        let service = "\(servicePrefix)\(providerId)"

        // 透過 security -i 互動命令由標準輸入傳遞，金鑰不會出現在系統程序清單（ps）中
        let cmd = "add-generic-password -U -s \"\(service)\" -a \"\(providerId)\" -w \"\(cleanSecret)\"\n"
        guard let inputData = cmd.data(using: .utf8) else {
            return (false, "無法將金鑰編碼為 UTF-8。")
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["-i"]
        let inPipe = Pipe()
        let outPipe = Pipe()
        process.standardInput = inPipe
        process.standardOutput = outPipe
        process.standardError = Pipe()

        do {
            try process.run()
            inPipe.fileHandleForWriting.write(inputData)
            inPipe.fileHandleForWriting.closeFile()
            _ = outPipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()

            if process.terminationStatus == 0 {
                return (true, "已成功儲存金鑰至 macOS 鑰匙圈。")
            } else {
                return (false, "儲存至鑰匙圈失敗。")
            }
        } catch {
            return (false, "執行鑰匙圈儲存指令失敗：\(error.localizedDescription)")
        }
    }

    public static func deleteKey(providerId: String) -> (success: Bool, message: String) {
        guard validateProviderId(providerId) else {
            return (false, "供應商識別碼格式不正確。")
        }
        let service = "\(servicePrefix)\(providerId)"

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["delete-generic-password", "-s", service, "-a", providerId]
        let outPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = Pipe()

        do {
            try process.run()
            _ = outPipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            if process.terminationStatus == 0 {
                return (true, "已從鑰匙圈移除金鑰。")
            } else {
                return (false, "刪除金鑰失敗或金鑰不存在。")
            }
        } catch {
            return (false, "刪除金鑰失敗：\(error.localizedDescription)")
        }
    }
}
