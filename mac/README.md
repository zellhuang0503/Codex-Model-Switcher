# Codex 多模型切換器 macOS 原生圖形介面版（.app）

這是專為 macOS 打造的 **原生圖形介面（GUI）應用程式**，採用 **Swift / SwiftUI** 現代化架構開發，編譯為單一標準 `.app` 應用程式包（`Codex Model Switcher.app`），雙擊即可開啟直覺操作視窗。

---

## 主要特色

- **純原生 macOS 圖形介面**：使用 SwiftUI 打造現代化視覺效果，支援深色/淺色模式、原生選單、快速鍵與高解析度 Retina 圖示。
- **安全鑰匙圈保管（Keychain）**：API Key 儲存於 macOS 系統鑰匙圈（`CodexModelSwitcher/provider/<供應商>`），不寫入任何設定檔或專案檔案中。
- **雙模運作架構**：
  - **雙擊開啟**：啟動直觀的原生圖形視窗，輕鬆切換模型、管理金鑰與查詢 DeepSeek 帳戶餘額。
  - **Codex 即時授權**：Codex 執行時自動呼叫 `CodexModelSwitcher token <供應商>` 即時取得金鑰，零等待、不落地。
- **完整安全防護與備份**：
  - 首次切換前永久備份原始設定（`~/.codex/model-switcher/backups/original-config.toml`）。
  - 自動保留最近 5 份切換快照，可隨時一鍵還原。
  - 採用原子寫入與寫入前校驗，設定失敗時自動回滾。
- **相容 OpenAI Responses API 自訂供應商**：除預設的 DeepSeek 與 MiniMax 外，可自由新增相容供應商。

---

## 系統需求

- macOS 12（Monterey）或更新版本（支援 Apple Silicon M 系列及 Intel Mac）
- 已安裝 Codex，且至少成功啟動過一次
- 具備目標供應商之 API Key（如 DeepSeek 或 MiniMax）

---

## 快速使用

### 1. 一鍵建置（若從原始碼建置）

在專案目錄下執行建置腳本：

```bash
cd mac
./build-app.sh
```

建置完成後將在 `mac/` 目錄生成 **`Codex Model Switcher.app`**。

### 2. 開啟應用程式

- 在 Finder 中直接 **雙擊「Codex Model Switcher.app」** 即可開啟。
- 也可將其拖曳至 `/Applications`（應用程式）資料夾中使用。

> **首次開啟提醒**：  
> 若 macOS 顯示安全性提示（因本工具為開源分享、未購買 Apple 付費開發者證書），請至 **「系統設定」→「隱私權與安全性」** 點擊 **「仍要打開」**，或在 App 上按住 `Control` 鍵點擊「打開」。

---

## 操作指南

### 1. 設定 API Key
- 切換至 **「金鑰管理」** 分頁。
- 點擊對應供應商（如 DeepSeek、MiniMax）的 **「設定金鑰」** 或 **「更換金鑰」**。
- 貼上您的 API Key 並儲存，金鑰將直接加密存入 macOS 鑰匙圈。
- DeepSeek 支援點擊 **「重新查詢」** 即時顯示帳戶剩餘餘額。

### 2. 切換模型
- 切換至 **「模型切換」** 分頁。
- 選擇目標供應商與模型（如 DeepSeek V4 Flash / Pro、MiniMax M3 等）。
- 點擊 **「立即切換模型」**。
- 關閉並重新啟動 Codex，模型即切換完成（Codex 介面顯示「自訂」為正常現象）。

### 3. 切回 OpenAI 原生設定
- 在「模型切換」分頁點擊 **「切回 OpenAI 原生設定」**，系統將解碼還原最初的 OpenAI 設定。

### 4. 連線測試與備份還原
- **連線測試**：在「連線測試」分頁發送極少量探針（16 token），確認網路、金鑰與端點相容性。
- **備份管理**：在「備份與歷史」分頁可隨時還原首次原始備份或指定歷史快照，並可一鍵在 Finder 開啟設定資料夾。

---

## 完全移除

1. 開啟 `Codex Model Switcher.app` → 「備份與歷史」分頁 → 點擊 **「恢復首次原始備份」**。
2. 至「金鑰管理」分頁刪除各供應商金鑰（或在內建「鑰匙圈存取」App 搜尋並刪除 `CodexModelSwitcher`）。
3. 刪除 `Codex Model Switcher.app` 應用程式。
4. （選用）刪除 `~/.codex/model-switcher/` 備份資料夾。
