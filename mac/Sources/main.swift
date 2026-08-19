import SwiftUI
import AppKit

@main
struct CodexModelSwitcherApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    init() {
        // MARK: - CLI Mode Check
        if CommandLine.arguments.count >= 3 && CommandLine.arguments[1] == "token" {
            let providerId = CommandLine.arguments[2]
            guard KeychainVault.validateProviderId(providerId) else {
                fputs("供應商識別碼格式不正確。\n", stderr)
                exit(2)
            }
            if let secret = KeychainVault.readKey(providerId: providerId), !secret.isEmpty {
                print(secret, terminator: "")
                exit(0)
            } else {
                fputs("尚未設定 \(providerId) 的 API Key。\n", stderr)
                exit(1)
            }
        }
    }

    var body: some Scene {
        WindowGroup {
            AppView()
        }
        .windowStyle(.titleBar)
        .windowToolbarStyle(.unified)
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("關於 Codex 多模型切換器") {
                    NSApplication.shared.orderFrontStandardAboutPanel(
                        options: [
                            NSApplication.AboutPanelOptionKey.applicationName: "Codex 多模型切換器",
                            NSApplication.AboutPanelOptionKey.version: "1.0.0"
                        ]
                    )
                }
            }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }
}
