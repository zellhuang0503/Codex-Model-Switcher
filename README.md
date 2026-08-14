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

目前只完成 MVP 設計，尚未開始撰寫應用程式，也尚未修改任何使用者的 Codex 設定。

詳細設計請見 [MVP 設計文件](docs/superpowers/specs/2026-08-14-windows-portable-mvp-design.md)。
