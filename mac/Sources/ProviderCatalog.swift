import Foundation

public struct ModelOption: Identifiable, Hashable, Codable {
    public var id: String
    public var displayName: String
    public var contextWindow: Int?
    public var supportsImages: Bool
    public var reasoningEfforts: [String]

    public init(id: String, displayName: String, contextWindow: Int? = nil, supportsImages: Bool = false, reasoningEfforts: [String] = []) {
        self.id = id
        self.displayName = displayName
        self.contextWindow = contextWindow
        self.supportsImages = supportsImages
        self.reasoningEfforts = reasoningEfforts
    }
}

public struct ProviderOption: Identifiable, Hashable, Codable {
    public var id: String
    public var displayName: String
    public var baseUrl: String
    public var requiresApiKey: Bool
    public var wireApi: String
    public var models: [ModelOption]
    public var isCustom: Bool

    public init(id: String, displayName: String, baseUrl: String = "", requiresApiKey: Bool = true, wireApi: String = "responses", models: [ModelOption] = [], isCustom: Bool = false) {
        self.id = id
        self.displayName = displayName
        self.baseUrl = baseUrl
        self.requiresApiKey = requiresApiKey
        self.wireApi = wireApi
        self.models = models
        self.isCustom = isCustom
    }
}

public final class ProviderCatalog {
    public static let shared = ProviderCatalog()

    private let customProvidersFile: URL

    public static let builtinProviders: [ProviderOption] = [
        ProviderOption(
            id: "openai",
            displayName: "OpenAI（Codex 原生）",
            baseUrl: "-",
            requiresApiKey: false,
            wireApi: "responses",
            models: [
                ModelOption(id: "codex-native", displayName: "使用 Codex 原生模型選單", supportsImages: true, reasoningEfforts: [])
            ],
            isCustom: false
        ),
        ProviderOption(
            id: "deepseek",
            displayName: "DeepSeek",
            baseUrl: "https://api.deepseek.com",
            requiresApiKey: true,
            wireApi: "responses",
            models: [
                ModelOption(id: "deepseek-v4-flash", displayName: "DeepSeek V4 Flash", contextWindow: 1000000, supportsImages: false, reasoningEfforts: ["high", "xhigh"]),
                ModelOption(id: "deepseek-v4-pro", displayName: "DeepSeek V4 Pro", contextWindow: 1000000, supportsImages: false, reasoningEfforts: ["high", "xhigh"])
            ],
            isCustom: false
        ),
        ProviderOption(
            id: "minimax",
            displayName: "MiniMax",
            baseUrl: "https://api.minimax.io/v1",
            requiresApiKey: true,
            wireApi: "responses",
            models: [
                ModelOption(id: "MiniMax-M3", displayName: "MiniMax M3", contextWindow: 1000000, supportsImages: true, reasoningEfforts: []),
                ModelOption(id: "MiniMax-M2.7", displayName: "MiniMax M2.7", contextWindow: 204800, supportsImages: false, reasoningEfforts: [])
            ],
            isCustom: false
        )
    ]

    private init() {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let switcherDir = home.appendingPathComponent(".codex/model-switcher")
        self.customProvidersFile = switcherDir.appendingPathComponent("custom-providers.json")
    }

    public func getAllProviders() -> [ProviderOption] {
        var all = ProviderCatalog.builtinProviders
        all.append(contentsOf: loadCustomProviders())
        return all
    }

    public func findProvider(byId id: String) -> ProviderOption? {
        let cleanId = id.hasPrefix("codex-switcher-") ? String(id.dropFirst("codex-switcher-".count)) : id
        return getAllProviders().first { $0.id == cleanId }
    }

    public func loadCustomProviders() -> [ProviderOption] {
        guard FileManager.default.fileExists(atPath: customProvidersFile.path) else {
            return []
        }
        do {
            let data = try Data(contentsOf: customProvidersFile)
            let list = try JSONDecoder().decode([ProviderOption].self, from: data)
            return list
        } catch {
            return []
        }
    }

    public func saveCustomProviders(_ providers: [ProviderOption]) throws {
        let switcherDir = customProvidersFile.deletingLastPathComponent()
        if !FileManager.default.fileExists(atPath: switcherDir.path) {
            try FileManager.default.createDirectory(at: switcherDir, withIntermediateDirectories: true)
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = .prettyPrinted
        let data = try encoder.encode(providers)
        try data.write(to: customProvidersFile, options: .atomic)
    }

    public func addCustomProvider(_ provider: ProviderOption) throws {
        var custom = loadCustomProviders()
        custom.removeAll { $0.id == provider.id }
        custom.append(provider)
        try saveCustomProviders(custom)
    }

    public func removeCustomProvider(byId id: String) throws {
        var custom = loadCustomProviders()
        custom.removeAll { $0.id == id }
        try saveCustomProviders(custom)
    }
}
