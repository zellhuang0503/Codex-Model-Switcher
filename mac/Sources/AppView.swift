import SwiftUI
import AppKit

// MARK: - Main Application State
public class AppState: ObservableObject {
    @Published public var status: SwitcherStatus = CodexConfigManager.shared.getStatus()
    @Published public var allProviders: [ProviderOption] = []
    @Published public var selectedProviderId: String = "deepseek"
    @Published public var selectedModelId: String = "deepseek-v4-flash"
    @Published public var selectedReasoningEffort: String = "high"

    @Published public var deepseekBalance: String? = nil
    @Published public var isFetchingBalance: Bool = false

    // Alerts and Sheets
    @Published public var alertMessage: String = ""
    @Published public var showAlert: Bool = false
    @Published public var alertTitle: String = ""

    @Published public var showDataFlowNotice: Bool = false
    @Published public var pendingSwitchAction: (() -> Void)? = nil

    @Published public var showKeyPrompt: Bool = false
    @Published public var keyPromptProviderId: String = ""
    @Published public var keyPromptInput: String = ""

    @Published public var showAddCustomProvider: Bool = false
    @Published public var newCustomId: String = ""
    @Published public var newCustomName: String = ""
    @Published public var newCustomBaseUrl: String = ""
    @Published public var newCustomModelId: String = ""
    @Published public var newCustomModelName: String = ""

    @Published public var isOperating: Bool = false
    @Published public var operationMessage: String = ""

    // Testing State
    @Published public var testProviderId: String = "deepseek"
    @Published public var testModelId: String = "deepseek-v4-flash"
    @Published public var isTesting: Bool = false
    @Published public var testResult: TestResult? = nil

    // Backups
    @Published public var backups: [BackupItem] = []

    public init() {
        self.status = CodexConfigManager.shared.getStatus()
        self.allProviders = ProviderCatalog.shared.getAllProviders()
        self.backups = CodexConfigManager.shared.listBackups()
        syncSelectionWithCurrentStatus()
    }

    public func syncSelectionWithCurrentStatus() {
        let curProvider = status.currentProvider
        let cleanProviderId = curProvider.hasPrefix(CodexConfigManager.managedPrefix)
            ? String(curProvider.dropFirst(CodexConfigManager.managedPrefix.count))
            : (curProvider == "openai" ? "openai" : curProvider)

        if let matchingProv = allProviders.first(where: { $0.id == cleanProviderId }) {
            selectedProviderId = matchingProv.id
            if let matchingModel = matchingProv.models.first(where: { $0.id == status.currentModel }) {
                selectedModelId = matchingModel.id
                if let eff = status.currentReasoningEffort, matchingModel.reasoningEfforts.contains(eff) {
                    selectedReasoningEffort = eff
                } else if !matchingModel.reasoningEfforts.isEmpty {
                    selectedReasoningEffort = matchingModel.reasoningEfforts.first ?? "high"
                } else {
                    selectedReasoningEffort = "-"
                }
            } else if let firstModel = matchingProv.models.first {
                selectedModelId = firstModel.id
                selectedReasoningEffort = firstModel.reasoningEfforts.first ?? "-"
            }
        } else if let firstProv = allProviders.first {
            selectedProviderId = firstProv.id
            selectedModelId = firstProv.models.first?.id ?? ""
            selectedReasoningEffort = firstProv.models.first?.reasoningEfforts.first ?? "-"
        }
    }

    public func refreshAll() {
        self.status = CodexConfigManager.shared.getStatus()
        self.allProviders = ProviderCatalog.shared.getAllProviders()
        self.backups = CodexConfigManager.shared.listBackups()
        syncSelectionWithCurrentStatus()
        fetchBalanceIfNeeded()
    }

    public func fetchBalanceIfNeeded() {
        if KeychainVault.exists(providerId: "deepseek") {
            isFetchingBalance = true
            Task { @MainActor in
                let bal = await ConnectionTester.shared.fetchDeepSeekBalance()
                self.deepseekBalance = bal
                self.isFetchingBalance = false
            }
        } else {
            self.deepseekBalance = nil
        }
    }

    public func onProviderChange(_ newProviderId: String) {
        selectedProviderId = newProviderId
        if let prov = allProviders.first(where: { $0.id == newProviderId }) {
            if let firstModel = prov.models.first {
                selectedModelId = firstModel.id
                if !firstModel.reasoningEfforts.isEmpty {
                    selectedReasoningEffort = firstModel.reasoningEfforts.contains(selectedReasoningEffort) ? selectedReasoningEffort : (firstModel.reasoningEfforts.first ?? "high")
                } else {
                    selectedReasoningEffort = "-"
                }
            }
        }
    }

    public func onModelChange(_ newModelId: String) {
        selectedModelId = newModelId
        if let prov = allProviders.first(where: { $0.id == selectedProviderId }),
           let model = prov.models.first(where: { $0.id == newModelId }) {
            if !model.reasoningEfforts.isEmpty {
                if !model.reasoningEfforts.contains(selectedReasoningEffort) {
                    selectedReasoningEffort = model.reasoningEfforts.first ?? "high"
                }
            } else {
                selectedReasoningEffort = "-"
            }
        }
    }

    // Actions
    public func performSwitch() {
        guard let prov = allProviders.first(where: { $0.id == selectedProviderId }),
              let model = prov.models.first(where: { $0.id == selectedModelId }) else {
            showError(title: "選擇錯誤", message: "請先選擇有效的供應商與模型。")
            return
        }

        if prov.id == "openai" {
            performOpenAISwitch()
            return
        }

        // Check key
        if prov.requiresApiKey && !KeychainVault.exists(providerId: prov.id) {
            keyPromptProviderId = prov.id
            keyPromptInput = ""
            showKeyPrompt = true
            return
        }

        // Check data flow notice
        if !CodexConfigManager.shared.isNoticeAccepted() {
            showDataFlowNotice = true
            pendingSwitchAction = { [weak self] in
                self?.executeApplySwitch(provider: prov, model: model)
            }
            return
        }

        executeApplySwitch(provider: prov, model: model)
    }

    public func executeApplySwitch(provider: ProviderOption, model: ModelOption) {
        isOperating = true
        operationMessage = "正在套用模型與思考強度設定..."
        do {
            try CodexConfigManager.shared.applySwitch(
                provider: provider,
                model: model,
                reasoningEffort: selectedReasoningEffort == "-" ? nil : selectedReasoningEffort
            )
            refreshAll()
            isOperating = false
            let effortText = (selectedReasoningEffort != "-" && !selectedReasoningEffort.isEmpty) ? "（思考強度：\(selectedReasoningEffort)）" : ""
            showSuccess(title: "設定已套用", message: "已安全套用 \(provider.displayName) / \(model.displayName) \(effortText)。\n請重新開啟 Codex 以生效。")
        } catch {
            isOperating = false
            showError(title: "套用失敗", message: error.localizedDescription)
        }
    }

    public func performOpenAISwitch() {
        isOperating = true
        operationMessage = "正在切回 OpenAI 原生設定..."
        do {
            try CodexConfigManager.shared.applyOpenAI()
            refreshAll()
            isOperating = false
            showSuccess(title: "還原成功", message: "已切回 OpenAI 原生設定。現在可以重新開啟 Codex。")
        } catch {
            isOperating = false
            showError(title: "還原失敗", message: error.localizedDescription)
        }
    }

    public func performRestoreOriginal() {
        isOperating = true
        operationMessage = "正在恢復首次備份..."
        do {
            try CodexConfigManager.shared.restoreOriginalBackup()
            refreshAll()
            isOperating = false
            showSuccess(title: "恢復成功", message: "已恢復為首次切換前的原始 Codex 設定。請重新開啟 Codex。")
        } catch {
            isOperating = false
            showError(title: "恢復失敗", message: error.localizedDescription)
        }
    }

    public func performRestoreSnapshot(path: String) {
        isOperating = true
        operationMessage = "正在恢復備份快照..."
        do {
            try CodexConfigManager.shared.restoreSpecificBackup(at: path)
            refreshAll()
            isOperating = false
            showSuccess(title: "恢復成功", message: "已恢復選取的備份快照。請重新開啟 Codex。")
        } catch {
            isOperating = false
            showError(title: "恢復失敗", message: error.localizedDescription)
        }
    }

    public func saveKey(providerId: String, key: String) {
        let res = KeychainVault.saveKey(providerId: providerId, secret: key)
        if res.success {
            refreshAll()
            showSuccess(title: "金鑰已儲存", message: "已安全儲存 \(providerId) 的金鑰至 macOS 鑰匙圈。")
        } else {
            showError(title: "儲存失敗", message: res.message)
        }
    }

    public func deleteKey(providerId: String) {
        let res = KeychainVault.deleteKey(providerId: providerId)
        if res.success {
            refreshAll()
            showSuccess(title: "金鑰已刪除", message: "已從 macOS 鑰匙圈刪除 \(providerId) 的金鑰。")
        } else {
            showError(title: "刪除失敗", message: res.message)
        }
    }

    public func runConnectionTest() {
        guard let prov = allProviders.first(where: { $0.id == testProviderId }),
              let model = prov.models.first(where: { $0.id == testModelId }) else {
            return
        }

        isTesting = true
        testResult = nil
        Task { @MainActor in
            let res = await ConnectionTester.shared.testConnection(
                providerId: prov.id,
                modelId: model.id,
                baseUrl: prov.baseUrl
            )
            self.testResult = res
            self.isTesting = false
        }
    }

    public func addCustomProvider() {
        let cleanId = newCustomId.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let cleanName = newCustomName.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleanUrl = newCustomBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleanModelId = newCustomModelId.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleanModelName = newCustomModelName.trimmingCharacters(in: .whitespacesAndNewlines)

        guard !cleanId.isEmpty, KeychainVault.validateProviderId(cleanId) else {
            showError(title: "格式錯誤", message: "供應商識別碼只能包含英數字與連字號（不可空白）。")
            return
        }
        guard !cleanName.isEmpty, !cleanUrl.isEmpty, !cleanModelId.isEmpty else {
            showError(title: "欄位未填", message: "請填寫所有必要欄位（名稱、Base URL、模型識別碼）。")
            return
        }

        let newModel = ModelOption(
            id: cleanModelId,
            displayName: cleanModelName.isEmpty ? cleanModelId : cleanModelName,
            supportsImages: false,
            reasoningEfforts: []
        )
        let newProvider = ProviderOption(
            id: cleanId,
            displayName: cleanName,
            baseUrl: cleanUrl,
            requiresApiKey: true,
            wireApi: "responses",
            models: [newModel],
            isCustom: true
        )

        do {
            try ProviderCatalog.shared.addCustomProvider(newProvider)
            showAddCustomProvider = false
            newCustomId = ""
            newCustomName = ""
            newCustomBaseUrl = ""
            newCustomModelId = ""
            newCustomModelName = ""
            refreshAll()
            showSuccess(title: "新增成功", message: "已成功新增自訂供應商【\(cleanName)】。")
        } catch {
            showError(title: "儲存失敗", message: error.localizedDescription)
        }
    }

    public func removeCustomProvider(id: String) {
        do {
            try ProviderCatalog.shared.removeCustomProvider(byId: id)
            _ = KeychainVault.deleteKey(providerId: id)
            refreshAll()
            showSuccess(title: "刪除成功", message: "已移除自訂供應商。")
        } catch {
            showError(title: "刪除失敗", message: error.localizedDescription)
        }
    }

    public func showSuccess(title: String, message: String) {
        alertTitle = title
        alertMessage = message
        showAlert = true
    }

    public func showError(title: String, message: String) {
        alertTitle = title
        alertMessage = message
        showAlert = true
    }
}

// MARK: - Root View
public struct AppView: View {
    @StateObject private var state = AppState()
    @State private var selectedTab: TabItem = .switcher

    enum TabItem: String, CaseIterable, Identifiable {
        case switcher = "模型切換"
        case keys = "金鑰管理"
        case testing = "連線測試"
        case backups = "備份與歷史"

        var id: String { rawValue }
        var iconName: String {
            switch self {
            case .switcher: return "arrow.triangle.swap"
            case .keys: return "key.fill"
            case .testing: return "network"
            case .backups: return "clock.arrow.circlepath"
            }
        }
    }

    public init() {}

    public var body: some View {
        VStack(spacing: 0) {
            // Header Bar
            headerView

            Divider()

            // Main Content Area with Segmented Control
            VStack(spacing: 16) {
                Picker("", selection: $selectedTab) {
                    ForEach(TabItem.allCases) { tab in
                        Label(tab.rawValue, systemImage: tab.iconName).tag(tab)
                    }
                }
                .pickerStyle(.segmented)
                .padding(.horizontal, 24)
                .padding(.top, 16)

                // Codex Running Warning Banner
                if state.status.isCodexRunning {
                    HStack(spacing: 10) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundColor(.orange)
                        Text("Codex 目前正在執行中。請先保存工作並關閉 Codex 再執行切換。")
                            .font(.system(size: 12.5, weight: .medium))
                            .foregroundColor(.primary)
                        Spacer()
                        Button("重新檢查") {
                            state.refreshAll()
                        }
                        .controlSize(.small)
                    }
                    .padding(10)
                    .background(Color.orange.opacity(0.12))
                    .cornerRadius(8)
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.orange.opacity(0.3), lineWidth: 1))
                    .padding(.horizontal, 24)
                }

                // Tab Content
                ScrollView(.vertical, showsIndicators: true) {
                    Group {
                        switch selectedTab {
                        case .switcher:
                            SwitcherTabView(state: state)
                        case .keys:
                            KeysTabView(state: state)
                        case .testing:
                            TestingTabView(state: state)
                        case .backups:
                            BackupsTabView(state: state)
                        }
                    }
                    .padding(.horizontal, 24)
                    .padding(.bottom, 24)
                }
            }
        }
        .frame(minWidth: 780, minHeight: 620)
        .background(Color(NSColor.windowBackgroundColor))
        .alert(state.alertTitle, isPresented: $state.showAlert) {
            Button("確定", role: .cancel) {}
        } message: {
            Text(state.alertMessage)
        }
        .sheet(isPresented: $state.showDataFlowNotice) {
            DataFlowNoticeSheet(state: state)
        }
        .sheet(isPresented: $state.showKeyPrompt) {
            KeyPromptSheet(state: state)
        }
        .sheet(isPresented: $state.showAddCustomProvider) {
            AddCustomProviderSheet(state: state)
        }
        .onAppear {
            state.fetchBalanceIfNeeded()
        }
    }

    private var headerView: some View {
        HStack(alignment: .center, spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Text("Codex 多模型切換器")
                        .font(.system(size: 19, weight: .bold))
                    Text("macOS 原生版")
                        .font(.system(size: 10.5, weight: .semibold))
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Color.accentColor.opacity(0.15))
                        .foregroundColor(.accentColor)
                        .cornerRadius(4)
                }
                Text("安全管理 Codex 的模型路線與 macOS 鑰匙圈金鑰")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
            }

            Spacer()

            Button(action: { state.refreshAll() }) {
                Label("重新整理", systemImage: "arrow.clockwise")
            }
            .controlSize(.regular)
        }
        .padding(.horizontal, 24)
        .padding(.vertical, 16)
        .background(Color(NSColor.controlBackgroundColor))
    }
}

// MARK: - Tab 1: Switcher Tab
struct SwitcherTabView: View {
    @ObservedObject var state: AppState

    var body: some View {
        VStack(spacing: 20) {
            // Current Active Status Card
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Label("目前使用中的設定", systemImage: "bolt.fill")
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundColor(.secondary)
                    Spacer()
                    if state.status.isManaged {
                        Text("切換器管理中")
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(.green)
                            .padding(.horizontal, 8)
                            .padding(.vertical, 3)
                            .background(Color.green.opacity(0.12))
                            .cornerRadius(6)
                    } else {
                        Text("OpenAI 原生")
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(.blue)
                            .padding(.horizontal, 8)
                            .padding(.vertical, 3)
                            .background(Color.blue.opacity(0.12))
                            .cornerRadius(6)
                    }
                }

                HStack(spacing: 24) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("供應商")
                            .font(.system(size: 11.5))
                            .foregroundColor(.secondary)
                        Text(CodexConfigManager.displayProviderName(for: state.status.currentProvider))
                            .font(.system(size: 15, weight: .bold))
                    }

                    Divider().frame(height: 32)

                    VStack(alignment: .leading, spacing: 4) {
                        Text("模型")
                            .font(.system(size: 11.5))
                            .foregroundColor(.secondary)
                        Text(state.status.currentModel)
                            .font(.system(size: 15, weight: .bold))
                    }

                    if let effort = state.status.currentReasoningEffort, !effort.isEmpty {
                        Divider().frame(height: 32)

                        VStack(alignment: .leading, spacing: 4) {
                            Text("思考強度")
                                .font(.system(size: 11.5))
                                .foregroundColor(.secondary)
                            Text(effort == "high" ? "high（高思考）" : (effort == "xhigh" ? "xhigh（極高）" : effort))
                                .font(.system(size: 15, weight: .bold))
                                .foregroundColor(.purple)
                        }
                    }

                    Spacer()
                }
            }
            .padding(18)
            .background(Color(NSColor.controlBackgroundColor))
            .cornerRadius(12)
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.gray.opacity(0.2), lineWidth: 1))

            // Switch Card
            VStack(alignment: .leading, spacing: 18) {
                Text("切換模型與思考強度")
                    .font(.system(size: 15, weight: .bold))

                VStack(alignment: .leading, spacing: 14) {
                    // Provider Picker
                    HStack(alignment: .center) {
                        Text("目標供應商：")
                            .font(.system(size: 13, weight: .medium))
                            .frame(width: 100, alignment: .leading)
                        Picker("", selection: Binding(
                            get: { state.selectedProviderId },
                            set: { newId in
                                state.onProviderChange(newId)
                            }
                        )) {
                            ForEach(state.allProviders) { prov in
                                Text(prov.displayName).tag(prov.id)
                            }
                        }
                        .pickerStyle(.menu)
                        .id("prov-picker-\(state.selectedProviderId)")
                    }

                    // Model Picker
                    if let curProv = state.allProviders.first(where: { $0.id == state.selectedProviderId }) {
                        HStack(alignment: .center) {
                            Text("目標模型：")
                                .font(.system(size: 13, weight: .medium))
                                .frame(width: 100, alignment: .leading)
                            Picker("", selection: Binding(
                                get: { state.selectedModelId },
                                set: { newModelId in
                                    state.onModelChange(newModelId)
                                }
                            )) {
                                ForEach(curProv.models) { model in
                                    Text(model.displayName).tag(model.id)
                                }
                            }
                            .pickerStyle(.menu)
                            .id("model-picker-\(state.selectedProviderId)-\(state.selectedModelId)")
                        }

                        // Reasoning Effort if applicable
                        if let curModel = curProv.models.first(where: { $0.id == state.selectedModelId }),
                           !curModel.reasoningEfforts.isEmpty {
                            HStack(alignment: .center) {
                                Text("思考強度：")
                                    .font(.system(size: 13, weight: .medium))
                                    .frame(width: 100, alignment: .leading)
                                Picker("", selection: $state.selectedReasoningEffort) {
                                    ForEach(curModel.reasoningEfforts, id: \.self) { effort in
                                        if effort == "high" {
                                            Text("high（高強度思考 · 預設）").tag("high")
                                        } else if effort == "xhigh" {
                                            Text("xhigh（極高強度思考）").tag("xhigh")
                                        } else {
                                            Text(effort).tag(effort)
                                        }
                                    }
                                }
                                .pickerStyle(.menu)
                                .id("effort-picker-\(state.selectedModelId)-\(state.selectedReasoningEffort)")
                            }
                        }

                        // Key Status for selected provider
                        if curProv.requiresApiKey {
                            HStack(spacing: 8) {
                                Text("金鑰狀態：")
                                    .font(.system(size: 12))
                                    .foregroundColor(.secondary)
                                    .frame(width: 100, alignment: .leading)
                                if KeychainVault.exists(providerId: curProv.id) {
                                    HStack(spacing: 4) {
                                        Image(systemName: "checkmark.seal.fill")
                                            .foregroundColor(.green)
                                        Text("已保存於 macOS 鑰匙圈")
                                            .font(.system(size: 12, weight: .medium))
                                            .foregroundColor(.green)
                                    }
                                } else {
                                    HStack(spacing: 4) {
                                        Image(systemName: "exclamationmark.circle.fill")
                                            .foregroundColor(.red)
                                        Text("尚未設定 API Key（切換時將提示輸入）")
                                            .font(.system(size: 12, weight: .medium))
                                            .foregroundColor(.red)
                                    }
                                }
                            }
                        }
                    }
                }

                Divider()

                // Action Buttons
                HStack(spacing: 14) {
                    Button(action: { state.performSwitch() }) {
                        Label("立即套用設定", systemImage: "checkmark.circle.fill")
                            .font(.system(size: 13.5, weight: .semibold))
                            .frame(minWidth: 140)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)

                    Button(action: { state.performOpenAISwitch() }) {
                        Label("切回 OpenAI 原生設定", systemImage: "arrow.uturn.backward")
                            .font(.system(size: 13, weight: .regular))
                    }
                    .controlSize(.large)

                    Spacer()
                }
            }
            .padding(20)
            .background(Color(NSColor.controlBackgroundColor))
            .cornerRadius(12)
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.gray.opacity(0.2), lineWidth: 1))
        }
    }
}

// MARK: - Tab 2: Keys Tab
struct KeysTabView: View {
    @ObservedObject var state: AppState
    @State private var editingProviderId: String? = nil
    @State private var inputSecret: String = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text("API Key 安全管理")
                        .font(.system(size: 15, weight: .bold))
                    Text("所有金鑰均儲存於 macOS 系統鑰匙圈，不寫入設定檔，不隨專案外流。")
                        .font(.system(size: 12))
                        .foregroundColor(.secondary)
                }
                Spacer()
                Button(action: { state.showAddCustomProvider = true }) {
                    Label("新增自訂供應商", systemImage: "plus.circle")
                }
                .controlSize(.regular)
            }

            VStack(spacing: 12) {
                ForEach(state.allProviders.filter { $0.requiresApiKey }) { prov in
                    let hasKey = KeychainVault.exists(providerId: prov.id)
                    VStack(alignment: .leading, spacing: 10) {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                HStack(spacing: 6) {
                                    Text(prov.displayName)
                                        .font(.system(size: 14, weight: .bold))
                                    if prov.isCustom {
                                        Text("自訂")
                                            .font(.system(size: 10, weight: .medium))
                                            .padding(.horizontal, 5)
                                            .padding(.vertical, 1)
                                            .background(Color.purple.opacity(0.15))
                                            .foregroundColor(.purple)
                                            .cornerRadius(4)
                                    }
                                }
                                Text("端點: \(prov.baseUrl)")
                                    .font(.system(size: 11))
                                    .foregroundColor(.secondary)
                            }

                            Spacer()

                            // Key Status & Action Buttons
                            HStack(spacing: 10) {
                                if hasKey {
                                    HStack(spacing: 4) {
                                        Image(systemName: "key.fill")
                                            .foregroundColor(.green)
                                        Text("已保存")
                                            .font(.system(size: 12, weight: .medium))
                                            .foregroundColor(.green)
                                    }

                                    Button("更換金鑰") {
                                        state.keyPromptProviderId = prov.id
                                        state.keyPromptInput = ""
                                        state.showKeyPrompt = true
                                    }
                                    .controlSize(.small)

                                    Button("刪除金鑰") {
                                        state.deleteKey(providerId: prov.id)
                                    }
                                    .controlSize(.small)
                                    .foregroundColor(.red)
                                } else {
                                    HStack(spacing: 4) {
                                        Image(systemName: "xmark.circle")
                                            .foregroundColor(.secondary)
                                        Text("未設定")
                                            .font(.system(size: 12))
                                            .foregroundColor(.secondary)
                                    }

                                    Button("設定金鑰") {
                                        state.keyPromptProviderId = prov.id
                                        state.keyPromptInput = ""
                                        state.showKeyPrompt = true
                                    }
                                    .buttonStyle(.borderedProminent)
                                    .controlSize(.small)
                                }

                                if prov.isCustom {
                                    Button(role: .destructive, action: { state.removeCustomProvider(id: prov.id) }) {
                                        Image(systemName: "trash")
                                    }
                                    .controlSize(.small)
                                }
                            }
                        }

                        // DeepSeek Balance Information
                        if prov.id == "deepseek" && hasKey {
                            HStack(spacing: 8) {
                                Image(systemName: "dollarsign.circle.fill")
                                    .foregroundColor(.orange)
                                if state.isFetchingBalance {
                                    ProgressView().controlSize(.small)
                                    Text("正在查詢帳戶餘額...")
                                        .font(.system(size: 12))
                                        .foregroundColor(.secondary)
                                } else if let bal = state.deepseekBalance {
                                    Text("帳戶餘額：\(bal)")
                                        .font(.system(size: 12, weight: .semibold))
                                        .foregroundColor(.primary)
                                } else {
                                    Text("餘額查詢需連網或至 platform.deepseek.com 查詢")
                                        .font(.system(size: 11.5))
                                        .foregroundColor(.secondary)
                                }
                                Spacer()
                                Button("重新查詢") {
                                    state.fetchBalanceIfNeeded()
                                }
                                .controlSize(.mini)
                            }
                            .padding(8)
                            .background(Color.orange.opacity(0.08))
                            .cornerRadius(6)
                        }
                    }
                    .padding(14)
                    .background(Color(NSColor.controlBackgroundColor))
                    .cornerRadius(10)
                    .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.gray.opacity(0.18), lineWidth: 1))
                }
            }
        }
    }
}

// MARK: - Tab 3: Testing Tab
struct TestingTabView: View {
    @ObservedObject var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Responses API 連線測試")
                    .font(.system(size: 15, weight: .bold))
                Text("連線測試會發送極少量固定測試文字（上限 16 個輸出 token），以驗證金鑰、模型與 Responses API 協定相容性。")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
            }

            VStack(alignment: .leading, spacing: 14) {
                HStack(alignment: .center) {
                    Text("測試供應商：")
                        .font(.system(size: 13, weight: .medium))
                        .frame(width: 90, alignment: .leading)
                    Picker("", selection: $state.testProviderId) {
                        ForEach(state.allProviders.filter { $0.requiresApiKey }) { prov in
                            Text(prov.displayName).tag(prov.id)
                        }
                    }
                    .pickerStyle(.menu)
                    .onChange(of: state.testProviderId) { _, newPid in
                        if let p = state.allProviders.first(where: { $0.id == newPid }), let m = p.models.first {
                            state.testModelId = m.id
                        }
                    }
                }

                if let curProv = state.allProviders.first(where: { $0.id == state.testProviderId }) {
                    HStack(alignment: .center) {
                        Text("測試模型：")
                            .font(.system(size: 13, weight: .medium))
                            .frame(width: 90, alignment: .leading)
                        Picker("", selection: $state.testModelId) {
                            ForEach(curProv.models) { model in
                                Text(model.displayName).tag(model.id)
                            }
                        }
                        .pickerStyle(.menu)
                    }
                }

                Button(action: { state.runConnectionTest() }) {
                    if state.isTesting {
                        HStack(spacing: 6) {
                            ProgressView().controlSize(.small)
                            Text("連線測試中...")
                        }
                        .frame(minWidth: 120)
                    } else {
                        Label("開始連線測試", systemImage: "play.fill")
                            .frame(minWidth: 120)
                    }
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.regular)
                .disabled(state.isTesting)
            }
            .padding(18)
            .background(Color(NSColor.controlBackgroundColor))
            .cornerRadius(10)
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.gray.opacity(0.18), lineWidth: 1))

            // Result Card
            if let result = state.testResult {
                VStack(alignment: .leading, spacing: 12) {
                    HStack(spacing: 8) {
                        Image(systemName: result.success ? "checkmark.circle.fill" : "xmark.circle.fill")
                            .font(.system(size: 20))
                            .foregroundColor(result.success ? .green : .red)
                        Text(result.success ? "測試成功" : "測試未通過")
                            .font(.system(size: 15, weight: .bold))
                            .foregroundColor(result.success ? .green : .red)
                        Spacer()
                        Text("HTTP \(result.httpStatus) · \(result.durationMs) ms")
                            .font(.system(size: 12, design: .monospaced))
                            .foregroundColor(.secondary)
                    }

                    Text(result.message)
                        .font(.system(size: 13))

                    Divider()

                    Text("診斷摘要：\(result.diagnosticSummary)")
                        .font(.system(size: 11.5, design: .monospaced))
                        .foregroundColor(.secondary)
                }
                .padding(16)
                .background(result.success ? Color.green.opacity(0.08) : Color.red.opacity(0.08))
                .cornerRadius(10)
                .overlay(RoundedRectangle(cornerRadius: 10).stroke(result.success ? Color.green.opacity(0.3) : Color.red.opacity(0.3), lineWidth: 1))
            }
        }
    }
}

// MARK: - Tab 4: Backups Tab
struct BackupsTabView: View {
    @ObservedObject var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 4) {
                Text("設定檔備份與還原")
                    .font(.system(size: 15, weight: .bold))
                Text("每次切換前系統會自動保留備份。首次切換前的原始設定將永久保留，並保留最近 5 份切換快照。")
                    .font(.system(size: 12))
                    .foregroundColor(.secondary)
            }

            // Quick Actions
            HStack(spacing: 12) {
                Button(action: { state.performRestoreOriginal() }) {
                    Label("恢復首次原始備份", systemImage: "arrow.counterclockwise")
                }
                .controlSize(.regular)
                .disabled(!state.status.originalBackupExists)

                Button(action: {
                    NSWorkspace.shared.open(CodexConfigManager.shared.backupDir)
                }) {
                    Label("在 Finder 開啟備份資料夾", systemImage: "folder")
                }
                .controlSize(.regular)

                Button(action: {
                    NSWorkspace.shared.open(CodexConfigManager.shared.codexHome)
                }) {
                    Label("在 Finder 開啟 ~/.codex", systemImage: "house")
                }
                .controlSize(.regular)
            }

            // Backup List
            VStack(alignment: .leading, spacing: 10) {
                Text("備份檔案清單（\(state.backups.count) 份）")
                    .font(.system(size: 13, weight: .semibold))

                if state.backups.isEmpty {
                    Text("尚未有備份檔案（切換時將自動建立）。")
                        .font(.system(size: 12))
                        .foregroundColor(.secondary)
                        .padding(.vertical, 12)
                } else {
                    VStack(spacing: 8) {
                        ForEach(state.backups) { item in
                            HStack {
                                Image(systemName: item.isOriginal ? "star.fill" : "doc.text")
                                    .foregroundColor(item.isOriginal ? .yellow : .secondary)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(item.isOriginal ? "\(item.filename)（首次原始備份）" : item.filename)
                                        .font(.system(size: 13, weight: item.isOriginal ? .bold : .regular))
                                    Text(item.date.formatted(date: .numeric, time: .standard))
                                        .font(.system(size: 11))
                                        .foregroundColor(.secondary)
                                }
                                Spacer()
                                Button("還原此快照") {
                                    state.performRestoreSnapshot(path: item.path)
                                }
                                .controlSize(.small)
                            }
                            .padding(10)
                            .background(Color(NSColor.controlBackgroundColor))
                            .cornerRadius(8)
                        }
                    }
                }
            }
        }
    }
}

// MARK: - Sheet Modals
struct DataFlowNoticeSheet: View {
    @ObservedObject var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Image(systemName: "shield.lefthalf.filled")
                    .font(.system(size: 24))
                    .foregroundColor(.accentColor)
                Text("第一次啟用第三方供應商的提醒")
                    .font(.system(size: 16, weight: .bold))
            }

            Divider()

            Text("切換之後，Codex 為了完成您交付的任務，會把您輸入的提示與相關專案內容片段，傳送給目前選擇的模型供應商處理。\n\n• 請不要讓第三方模型處理未經授權的個人隱私或機密專案。\n• 切換器本體不會讀取、儲存或上傳您的任何專案檔案內容。")
                .font(.system(size: 13))
                .lineSpacing(4)
                .foregroundColor(.primary)

            Spacer()

            HStack {
                Button("取消") {
                    state.showDataFlowNotice = false
                    state.pendingSwitchAction = nil
                }
                .keyboardShortcut(.cancelAction)

                Spacer()

                Button("我瞭解並同意繼續") {
                    try? CodexConfigManager.shared.acceptNotice()
                    state.showDataFlowNotice = false
                    state.pendingSwitchAction?()
                    state.pendingSwitchAction = nil
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 480, height: 300)
    }
}

struct KeyPromptSheet: View {
    @ObservedObject var state: AppState
    @State private var secret: String = ""
    @State private var isSecured: Bool = true

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("設定 \(state.keyPromptProviderId) 的 API Key")
                .font(.system(size: 16, weight: .bold))

            Text("金鑰將直接加密儲存於 macOS 系統鑰匙圈（Keychain），不寫入 config.toml。")
                .font(.system(size: 12))
                .foregroundColor(.secondary)

            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    if isSecured {
                        SecureField("請在此貼上 API Key", text: $secret)
                            .textFieldStyle(.roundedBorder)
                    } else {
                        TextField("請在此貼上 API Key", text: $secret)
                            .textFieldStyle(.roundedBorder)
                    }
                    Button(action: { isSecured.toggle() }) {
                        Image(systemName: isSecured ? "eye.slash" : "eye")
                    }
                }
            }

            Spacer()

            HStack {
                Button("取消") {
                    state.showKeyPrompt = false
                }
                .keyboardShortcut(.cancelAction)

                Spacer()

                Button("儲存金鑰") {
                    state.saveKey(providerId: state.keyPromptProviderId, key: secret)
                    state.showKeyPrompt = false
                }
                .buttonStyle(.borderedProminent)
                .disabled(secret.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 460, height: 230)
    }
}

struct AddCustomProviderSheet: View {
    @ObservedObject var state: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("新增自訂供應商（OpenAI Responses API 相容）")
                .font(.system(size: 16, weight: .bold))

            VStack(alignment: .leading, spacing: 10) {
                VStack(alignment: .leading, spacing: 4) {
                    Text("供應商識別碼 (ID，如 siliconflow)：")
                        .font(.system(size: 12, weight: .medium))
                    TextField("例如：siliconflow", text: $state.newCustomId)
                        .textFieldStyle(.roundedBorder)
                }

                VStack(alignment: .leading, spacing: 4) {
                    Text("顯示名稱：")
                        .font(.system(size: 12, weight: .medium))
                    TextField("例如：SiliconFlow 矽基流動", text: $state.newCustomName)
                        .textFieldStyle(.roundedBorder)
                }

                VStack(alignment: .leading, spacing: 4) {
                    Text("Base URL 端點（支援 /responses）：")
                        .font(.system(size: 12, weight: .medium))
                    TextField("例如：https://api.siliconflow.cn/v1", text: $state.newCustomBaseUrl)
                        .textFieldStyle(.roundedBorder)
                }

                HStack(spacing: 12) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("模型 ID：")
                            .font(.system(size: 12, weight: .medium))
                        TextField("例如：deepseek-ai/DeepSeek-V3", text: $state.newCustomModelId)
                            .textFieldStyle(.roundedBorder)
                    }

                    VStack(alignment: .leading, spacing: 4) {
                        Text("模型顯示名稱：")
                            .font(.system(size: 12, weight: .medium))
                        TextField("例如：DeepSeek V3", text: $state.newCustomModelName)
                            .textFieldStyle(.roundedBorder)
                    }
                }
            }

            Spacer()

            HStack {
                Button("取消") {
                    state.showAddCustomProvider = false
                }
                .keyboardShortcut(.cancelAction)

                Spacer()

                Button("新增並儲存") {
                    state.addCustomProvider()
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 500, height: 380)
    }
}
