import Foundation
import AppKit

public struct SwitcherStatus {
    public var configExists: Bool
    public var configPath: String
    public var currentProvider: String
    public var currentModel: String
    public var currentReasoningEffort: String?
    public var isManaged: Bool
    public var originalBackupExists: Bool
    public var originalBackupDate: Date?
    public var recentBackupsCount: Int
    public var isCodexRunning: Bool
}

public struct BackupItem: Identifiable {
    public var id: String { path }
    public var filename: String
    public var path: String
    public var date: Date
    public var isOriginal: Bool
}

public final class CodexConfigManager {
    public static let shared = CodexConfigManager()

    public let codexHome: URL
    public let configPath: URL
    public let switcherDir: URL
    public let backupDir: URL
    public let originalBackup: URL
    public let noticeFlag: URL
    public let lockDir: URL

    public static let managedPrefix = "codex-switcher-"
    public static let markerPrefix = "# codex-model-switcher-saved-"
    public static let managedKeys = [
        "model",
        "model_provider",
        "model_reasoning_effort",
        "preferred_auth_method",
        "forced_login_method",
        "model_catalog_json"
    ]
    public static let recentBackupLimit = 5
    public static let maxConfigBytes = 5 * 1024 * 1024 // 5MB

    public init() {
        let envCodexHome = ProcessInfo.processInfo.environment["CODEX_HOME"]
        let home = envCodexHome != nil && !envCodexHome!.isEmpty
            ? URL(fileURLWithPath: envCodexHome!)
            : FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".codex")
        self.codexHome = home
        self.configPath = home.appendingPathComponent("config.toml")
        self.switcherDir = home.appendingPathComponent("model-switcher")
        self.backupDir = switcherDir.appendingPathComponent("backups")
        self.originalBackup = backupDir.appendingPathComponent("original-config.toml")
        self.noticeFlag = switcherDir.appendingPathComponent("data-flow-notice-accepted.txt")
        self.lockDir = switcherDir.appendingPathComponent("config.lock.d")
    }

    public static func isManagedProvider(_ provider: String) -> Bool {
        return provider.hasPrefix(managedPrefix)
    }

    public static func displayProviderName(for rawProvider: String) -> String {
        if isManagedProvider(rawProvider) {
            let pid = String(rawProvider.dropFirst(managedPrefix.count))
            if let found = ProviderCatalog.shared.findProvider(byId: pid) {
                return "\(found.displayName)（切換器管理）"
            }
            return "\(pid)（切換器管理）"
        }
        if rawProvider.isEmpty || rawProvider == "openai" {
            return "OpenAI（Codex 原生）"
        }
        return rawProvider
    }

    // MARK: - Process Detection
    public func isCodexRunning() -> Bool {
        let runningApps = NSWorkspace.shared.runningApplications
        for app in runningApps {
            let name = (app.localizedName ?? "").lowercased()
            let bundleId = (app.bundleIdentifier ?? "").lowercased()
            if name == "codex" || name == "openai.codex" || bundleId.contains("openai.codex") {
                return true
            }
        }
        return false
    }

    // MARK: - Status
    public func getStatus() -> SwitcherStatus {
        let exists = FileManager.default.fileExists(atPath: configPath.path)
        guard exists else {
            return SwitcherStatus(
                configExists: false,
                configPath: configPath.path,
                currentProvider: "尚未建立設定檔",
                currentModel: "—",
                isManaged: false,
                originalBackupExists: false,
                originalBackupDate: nil,
                recentBackupsCount: 0,
                isCodexRunning: isCodexRunning()
            )
        }

        let content = (try? String(contentsOf: configPath, encoding: .utf8)) ?? ""
        let rootProvider = getRootValue(from: content, key: "model_provider") ?? "openai"
        let rootModel = getRootValue(from: content, key: "model") ?? "（Codex 原生選單）"
        let rootReasoningEffort = getRootValue(from: content, key: "model_reasoning_effort")
        let isManaged = Self.isManagedProvider(rootProvider)

        let origExists = FileManager.default.fileExists(atPath: originalBackup.path)
        var origDate: Date? = nil
        if origExists {
            if let attrs = try? FileManager.default.attributesOfItem(atPath: originalBackup.path),
               let modDate = attrs[.modificationDate] as? Date {
                origDate = modDate
            }
        }

        let recentCount = listBackups().filter { !$0.isOriginal }.count

        return SwitcherStatus(
            configExists: true,
            configPath: configPath.path,
            currentProvider: rootProvider,
            currentModel: rootModel,
            currentReasoningEffort: rootReasoningEffort,
            isManaged: isManaged,
            originalBackupExists: origExists,
            originalBackupDate: origDate,
            recentBackupsCount: recentCount,
            isCodexRunning: isCodexRunning()
        )
    }

    // MARK: - Notice Flag
    public func isNoticeAccepted() -> Bool {
        return FileManager.default.fileExists(atPath: noticeFlag.path)
    }

    public func acceptNotice() throws {
        try FileManager.default.createDirectory(at: switcherDir, withIntermediateDirectories: true)
        let dateStr = ISO8601DateFormatter().string(from: Date())
        let text = "已於 \(dateStr) 顯示並同意資料流提醒。\n"
        try text.write(to: noticeFlag, atomically: true, encoding: .utf8)
    }

    // MARK: - Lock Handling
    public func acquireLock() -> Bool {
        try? FileManager.default.createDirectory(at: switcherDir, withIntermediateDirectories: true)
        do {
            try FileManager.default.createDirectory(at: lockDir, withIntermediateDirectories: false)
            return true
        } catch {
            return false
        }
    }

    public func releaseLock() {
        try? FileManager.default.removeItem(at: lockDir)
    }

    // MARK: - Backup Management
    public func backupBeforeWrite() throws {
        try FileManager.default.createDirectory(at: backupDir, withIntermediateDirectories: true)
        guard FileManager.default.fileExists(atPath: configPath.path) else { return }

        if !FileManager.default.fileExists(atPath: originalBackup.path) {
            try FileManager.default.copyItem(at: configPath, to: originalBackup)
        }

        let formatter = DateFormatter()
        formatter.dateFormat = "yyyyMMdd-HHmmss"
        let timestamp = formatter.string(from: Date())
        let pid = ProcessInfo.processInfo.processIdentifier
        let snapshotName = "switch-\(timestamp)-\(pid).toml"
        let snapshotPath = backupDir.appendingPathComponent(snapshotName)
        try FileManager.default.copyItem(at: configPath, to: snapshotPath)

        // Prune older switch-*.toml backups
        let items = (try? FileManager.default.contentsOfDirectory(at: backupDir, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
        let snapshots = items.filter { $0.lastPathComponent.hasPrefix("switch-") && $0.pathExtension == "toml" }
            .sorted { url1, url2 in
                let d1 = (try? url1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? Date.distantPast
                let d2 = (try? url2.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? Date.distantPast
                return d1 > d2
            }

        if snapshots.count > Self.recentBackupLimit {
            for old in snapshots.dropFirst(Self.recentBackupLimit) {
                try? FileManager.default.removeItem(at: old)
            }
        }
    }

    public func listBackups() -> [BackupItem] {
        guard FileManager.default.fileExists(atPath: backupDir.path) else { return [] }
        var result: [BackupItem] = []

        if FileManager.default.fileExists(atPath: originalBackup.path),
           let attrs = try? FileManager.default.attributesOfItem(atPath: originalBackup.path),
           let date = attrs[.modificationDate] as? Date {
            result.append(BackupItem(
                filename: "original-config.toml",
                path: originalBackup.path,
                date: date,
                isOriginal: true
            ))
        }

        let items = (try? FileManager.default.contentsOfDirectory(at: backupDir, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
        let snapshots = items.filter { $0.lastPathComponent.hasPrefix("switch-") && $0.pathExtension == "toml" }
            .sorted { url1, url2 in
                let d1 = (try? url1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? Date.distantPast
                let d2 = (try? url2.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? Date.distantPast
                return d1 > d2
            }

        for item in snapshots {
            let date = (try? item.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? Date()
            result.append(BackupItem(
                filename: item.lastPathComponent,
                path: item.path,
                date: date,
                isOriginal: false
            ))
        }
        return result
    }

    // MARK: - TOML Parsing Helpers
    public func getRootValue(from content: String, key: String) -> String? {
        let lines = content.components(separatedBy: .newlines)
        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("[") {
                // Entered table section; root keys must be before tables
                break
            }
            if trimmed.hasPrefix(key) {
                let rest = trimmed.dropFirst(key.count).trimmingCharacters(in: .whitespaces)
                if rest.hasPrefix("=") {
                    var valPart = String(rest.dropFirst("=".count)).trimmingCharacters(in: .whitespaces)
                    if valPart.hasPrefix("\"") {
                        valPart = String(valPart.dropFirst(1))
                        if let endQuote = valPart.firstIndex(of: "\"") {
                            return String(valPart[..<endQuote])
                        }
                    }
                }
            }
        }
        return nil
    }

    public func getRootLine(from content: String, key: String) -> String? {
        let lines = content.components(separatedBy: .newlines)
        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("[") {
                break
            }
            if trimmed.hasPrefix(key) {
                let rest = trimmed.dropFirst(key.count).trimmingCharacters(in: .whitespaces)
                if rest.hasPrefix("=") {
                    return line
                }
            }
        }
        return nil
    }

    public func getMarkerValue(from content: String, key: String) -> String? {
        let pfx = "\(Self.markerPrefix)\(key): "
        let lines = content.components(separatedBy: .newlines)
        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix(pfx) {
                return String(trimmed.dropFirst(pfx.count)).trimmingCharacters(in: .whitespaces)
            }
        }
        return nil
    }

    // MARK: - Rewriting Engine
    private func rewriteConfigContent(originalContent: String, insertLines: [String]) -> String {
        let lines = originalContent.components(separatedBy: .newlines)
        var resultLines: [String] = []
        var inserted = false
        var skippingTable = false
        var heldEmptyLines = 0

        let managedKeysSet = Set(Self.managedKeys)

        func isManagedRootKeyLine(_ line: String) -> Bool {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            for k in managedKeysSet {
                if trimmed.hasPrefix(k) {
                    let rest = trimmed.dropFirst(k.count).trimmingCharacters(in: .whitespaces)
                    if rest.hasPrefix("=") {
                        return true
                    }
                }
            }
            return false
        }

        func emitInsert() {
            resultLines.append(contentsOf: insertLines)
            inserted = true
        }

        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespaces)

            if trimmed.hasPrefix("[") {
                if !inserted {
                    emitInsert()
                }
                if trimmed.hasPrefix("[model_providers.\(Self.managedPrefix)") {
                    heldEmptyLines = 0
                    skippingTable = true
                    continue
                }
                skippingTable = false
                while heldEmptyLines > 0 {
                    resultLines.append("")
                    heldEmptyLines -= 1
                }
                resultLines.append(line)
                continue
            }

            if skippingTable {
                continue
            }

            if inserted && trimmed.isEmpty {
                heldEmptyLines += 1
                continue
            }

            if inserted {
                while heldEmptyLines > 0 {
                    resultLines.append("")
                    heldEmptyLines -= 1
                }
                resultLines.append(line)
                continue
            }

            if !inserted {
                if trimmed.hasPrefix(Self.markerPrefix) {
                    continue
                }
                if isManagedRootKeyLine(line) {
                    continue
                }
                resultLines.append(line)
                continue
            }

            resultLines.append(line)
        }

        if !inserted {
            emitInsert()
        }

        return resultLines.joined(separator: "\n")
    }

    private func buildMarkers(from content: String) throws -> [String] {
        let currentProvider = getRootValue(from: content, key: "model_provider") ?? "openai"
        let existingModelMarker = getMarkerValue(from: content, key: "model")

        var markerLines: [String] = []

        if let _ = existingModelMarker {
            // Markers exist: preserve them exactly
            for key in Self.managedKeys {
                guard let val = getMarkerValue(from: content, key: key) else {
                    throw NSError(domain: "CodexModelSwitcher", code: 1, userInfo: [NSLocalizedDescriptionKey: "找不到完整的 OpenAI 原設定標記，已停止切換。"])
                }
                markerLines.append("\(Self.markerPrefix)\(key): \(val)")
            }
        } else if Self.isManagedProvider(currentProvider) {
            throw NSError(domain: "CodexModelSwitcher", code: 2, userInfo: [NSLocalizedDescriptionKey: "設定已由切換器管理但缺少標記，已停止切換。請先「恢復原始設定」。"])
        } else {
            // First time switch: encode existing root lines
            for key in Self.managedKeys {
                if let origLine = getRootLine(from: content, key: key) {
                    let b64 = origLine.data(using: .utf8)?.base64EncodedString() ?? ""
                    markerLines.append("\(Self.markerPrefix)\(key): \(b64)")
                } else {
                    markerLines.append("\(Self.markerPrefix)\(key): -")
                }
            }
        }
        return markerLines
    }

    public func getExecutablePath() -> String {
        if let bundleExec = Bundle.main.executablePath, FileManager.default.fileExists(atPath: bundleExec) {
            return bundleExec
        }
        return CommandLine.arguments[0]
    }

    // MARK: - Switch to 3rd Party Provider
    public func applySwitch(provider: ProviderOption, model: ModelOption, reasoningEffort: String?) throws {
        guard acquireLock() else {
            throw NSError(domain: "CodexModelSwitcher", code: 10, userInfo: [NSLocalizedDescriptionKey: "另一個切換器正在修改設定，請稍後再試。"])
        }
        defer { releaseLock() }

        guard FileManager.default.fileExists(atPath: configPath.path) else {
            throw NSError(domain: "CodexModelSwitcher", code: 11, userInfo: [NSLocalizedDescriptionKey: "找不到 Codex 設定檔（\(configPath.path)）。請先啟動一次 Codex。"])
        }

        let originalContent = try String(contentsOf: configPath, encoding: .utf8)
        let markers = try buildMarkers(from: originalContent)

        let managedId = "\(Self.managedPrefix)\(provider.id)"
        var insertLines = markers
        insertLines.append("model = \"\(model.id)\"")
        insertLines.append("model_provider = \"\(managedId)\"")

        // Reasoning Effort
        var effectiveEffort: String? = nil
        if let effort = reasoningEffort, !effort.isEmpty && effort != "-" {
            effectiveEffort = effort
        } else if !model.reasoningEfforts.isEmpty {
            effectiveEffort = model.reasoningEfforts.contains("high") ? "high" : model.reasoningEfforts.first
        }
        if let eff = effectiveEffort {
            insertLines.append("model_reasoning_effort = \"\(eff)\"")
        }

        insertLines.append("preferred_auth_method = \"apikey\"")
        insertLines.append("forced_login_method = \"api\"")

        var rewritten = rewriteConfigContent(originalContent: originalContent, insertLines: insertLines)

        let execPath = getExecutablePath()
        let authBlock = """

[model_providers.\(managedId)]
name = "\(provider.displayName)"
base_url = "\(provider.baseUrl)"
wire_api = "\(provider.wireApi)"

[model_providers.\(managedId).auth]
command = "\(execPath)"
args = ["token", "\(provider.id)"]
timeout_ms = 5000
"""
        rewritten.append(contentsOf: authBlock + "\n")

        // Pre-write validation
        let checkProvider = getRootValue(from: rewritten, key: "model_provider")
        let checkModel = getRootValue(from: rewritten, key: "model")
        guard checkProvider == managedId && checkModel == model.id else {
            throw NSError(domain: "CodexModelSwitcher", code: 12, userInfo: [NSLocalizedDescriptionKey: "切換後設定驗證失敗，原設定未被修改。"])
        }

        try backupBeforeWrite()

        let tmpUrl = codexHome.appendingPathComponent("config.toml.tmp.\(UUID().uuidString)")
        try rewritten.write(to: tmpUrl, atomically: true, encoding: .utf8)
        _ = try? FileManager.default.removeItem(at: configPath)
        try FileManager.default.moveItem(at: tmpUrl, to: configPath)
    }

    // MARK: - Switch to OpenAI Native
    public func applyOpenAI() throws {
        guard acquireLock() else {
            throw NSError(domain: "CodexModelSwitcher", code: 10, userInfo: [NSLocalizedDescriptionKey: "另一個切換器正在修改設定，請稍後再試。"])
        }
        defer { releaseLock() }

        guard FileManager.default.fileExists(atPath: configPath.path) else {
            throw NSError(domain: "CodexModelSwitcher", code: 11, userInfo: [NSLocalizedDescriptionKey: "找不到 Codex 設定檔。"])
        }

        let originalContent = try String(contentsOf: configPath, encoding: .utf8)
        let currentProvider = getRootValue(from: originalContent, key: "model_provider") ?? "openai"
        let existingModelMarker = getMarkerValue(from: originalContent, key: "model")

        if existingModelMarker == nil && !Self.isManagedProvider(currentProvider) {
            // Already native OpenAI
            return
        }

        var decodedLines: [String] = []
        for key in Self.managedKeys {
            guard let val = getMarkerValue(from: originalContent, key: key) else {
                throw NSError(domain: "CodexModelSwitcher", code: 20, userInfo: [NSLocalizedDescriptionKey: "原始 OpenAI 模型設定標記不完整，可改用「恢復原始設定」。"])
            }
            if val != "-" {
                guard let data = Data(base64Encoded: val), let decoded = String(data: data, encoding: .utf8) else {
                    throw NSError(domain: "CodexModelSwitcher", code: 21, userInfo: [NSLocalizedDescriptionKey: "標記內容無法解碼，可改用「恢復原始設定」。"])
                }
                decodedLines.append(decoded)
            }
        }

        let rewritten = rewriteConfigContent(originalContent: originalContent, insertLines: decodedLines)
        let restoredProvider = getRootValue(from: rewritten, key: "model_provider") ?? "openai"
        if Self.isManagedProvider(restoredProvider) {
            throw NSError(domain: "CodexModelSwitcher", code: 22, userInfo: [NSLocalizedDescriptionKey: "還原後設定驗證失敗，原設定未被修改。"])
        }

        try backupBeforeWrite()

        let tmpUrl = codexHome.appendingPathComponent("config.toml.tmp.\(UUID().uuidString)")
        try rewritten.write(to: tmpUrl, atomically: true, encoding: .utf8)
        _ = try? FileManager.default.removeItem(at: configPath)
        try FileManager.default.moveItem(at: tmpUrl, to: configPath)
    }

    // MARK: - Restore from Backup
    public func restoreOriginalBackup() throws {
        guard acquireLock() else {
            throw NSError(domain: "CodexModelSwitcher", code: 10, userInfo: [NSLocalizedDescriptionKey: "另一個切換器正在修改設定，請稍後再試。"])
        }
        defer { releaseLock() }

        guard FileManager.default.fileExists(atPath: originalBackup.path) else {
            throw NSError(domain: "CodexModelSwitcher", code: 30, userInfo: [NSLocalizedDescriptionKey: "找不到首次切換前的原始設定備份（尚未執行過切換）。"])
        }

        try backupBeforeWrite()
        try? FileManager.default.removeItem(at: configPath)
        try FileManager.default.copyItem(at: originalBackup, to: configPath)
    }

    public func restoreSpecificBackup(at path: String) throws {
        guard acquireLock() else {
            throw NSError(domain: "CodexModelSwitcher", code: 10, userInfo: [NSLocalizedDescriptionKey: "另一個切換器正在修改設定，請稍後再試。"])
        }
        defer { releaseLock() }

        let backupUrl = URL(fileURLWithPath: path)
        guard FileManager.default.fileExists(atPath: backupUrl.path) else {
            throw NSError(domain: "CodexModelSwitcher", code: 31, userInfo: [NSLocalizedDescriptionKey: "指定的備份檔案不存在。"])
        }

        try backupBeforeWrite()
        try? FileManager.default.removeItem(at: configPath)
        try FileManager.default.copyItem(at: backupUrl, to: configPath)
    }
}
