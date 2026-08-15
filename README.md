# Codex Model Switcher

Codex Model Switcher 是一個 Windows 可攜版工具，讓使用者在單一視窗中切換 Codex App 使用的模型供應商與模型，不需要在桌面建立大量捷徑，也不需要手動編輯 `config.toml`。

## MVP 目標

- 下載 ZIP、解壓縮、雙擊即可使用。
- 不需要管理員權限或預先安裝開發工具。
- 顯示目前使用中的供應商與模型。
- 支援 OpenAI、DeepSeek V4 Flash、DeepSeek V4 Pro、MiniMax M3。
- 支援自訂 OpenAI Responses API 相容供應商。
- 切換前自動備份 Codex 設定，驗證成功後才寫入。
- API Key 存入 Windows 認證管理員，不寫入專案或一般設定檔。
- 隨時可以恢復切換前的 Codex 設定。

## 典型使用情境

1. 使用 GPT-5.6 Sol 或其他 OpenAI 模型完成規劃。
2. OpenAI 額度不足時，開啟 Codex Model Switcher。
3. 選擇 DeepSeek V4 Flash、DeepSeek V4 Pro 或 MiniMax M3。
4. 切換器安全更新模型設定並重新開啟 Codex。
5. 新模型在同一專案中讀取工作交接檔後繼續執行。

## MVP 不包含

- Gemini、Kimi、Z.AI GLM
- macOS 與 Linux
- 自動更新
- 正式安裝程式
- 程式碼簽章
- 任何預先內建的 API Key
- 使用者資料蒐集或遙測

## 目前進度

MVP 功能開發已全部完成：

- Windows 主視窗與高解析度支援
- 供應商目錄（OpenAI、DeepSeek V4 Flash／Pro、MiniMax M3／M2.7）與損壞時的安全退回
- API Key 保存於 Windows 認證管理員，Codex 以 `token <供應商ID>` 命令式驗證取用
- Codex 設定的局部修改、原設定標記保存、原子取代、首次原始備份與最近五份備份、一鍵恢復
- 連線測試（固定內容、極小 token 上限、不重試）與不含金鑰的診斷摘要
- 自訂 Responses API 相容供應商：通過文字與工具呼叫雙驗證後才可啟用
- 首次啟用第三方供應商前的一次性資料流提醒
- 桌面版 Codex（Windows 與 WSL 混合環境）相容：API 金鑰驗證模式切換、以 cmd.exe 包裝金鑰命令；切換後模型選單顯示「自訂」屬正常行為
- 96 個自動測試（`dotnet test tests/CodexModelSwitcher.Tests.csproj`）
- 可攜版打包指令碼（`build-portable.ps1`，支援 Windows PowerShell 5.1 與 PowerShell 7，含發佈內容白名單與金鑰掃描）

DeepSeek V4 Flash 已在桌面版 Codex 完成端到端實測（請求路由、金鑰取用、供應商用量核對、完整還原）；MiniMax M3 已完成官方 API 連線實測。剩餘工作為乾淨 Windows 環境的最終驗收。

詳細設計請見 [MVP 設計文件](docs/superpowers/specs/2026-08-14-windows-portable-mvp-design.md)。
