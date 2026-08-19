# Codex Model Switcher

Codex Model Switcher 是一個 Windows 可攜版工具，讓使用者在單一視窗中切換 Codex App 使用的模型供應商與模型，不需要在桌面建立大量捷徑，也不需要手動編輯 `config.toml`。

> ### 👉 第一次使用？請看 **[新手完整教學](docs/新手教學.md)**
> 不懂程式也能照做的逐步說明：名詞解釋、下載、申請 API Key、切換、還原、常見問題與錯誤排除。

## 文件導覽

| 文件 | 適合誰 | 內容 |
| --- | --- | --- |
| [新手完整教學](docs/新手教學.md) | **不懂程式的使用者** | 從零開始的逐步操作，含圖解說明與 FAQ |
| 本頁下方的[下載與開始使用](#下載與開始使用) | 想快速上手的使用者 | 系統需求、下載步驟、重要提醒 |
| `使用說明.html`（ZIP 內） | 已下載的使用者 | 離線版說明書，用瀏覽器打開即可 |
| [MVP 設計文件](docs/superpowers/specs/2026-08-14-windows-portable-mvp-design.md) | 開發者 | 架構、安全設計與驗收標準 |
| [發佈流程](docs/發佈流程.md) | 維護者 | 打包 ZIP 與發佈 GitHub Release 的逐步步驟 |
| [macOS 版說明](mac/README.md) | **Mac 使用者** | 終端機原型版的安裝與使用步驟 |
| [SECURITY.md](SECURITY.md) | 資安回報者 | 安全問題回報方式 |

## 下載與開始使用

**系統需求**

- Windows 10 或 Windows 11（64 位元）
- 已安裝並至少成功啟動過一次 Codex（切換器需要讀取它的設定檔）
- 不需要另外安裝 .NET，也不需要系統管理員權限

**下載步驟**

1. 到 [Releases](../../releases) 頁面下載最新的 `Codex-Model-Switcher-Windows-x64-*.zip`。
2. 解壓縮到一個**固定不會搬動的資料夾**，例如 `C:\Tools\Codex-Model-Switcher`。
3. 雙擊 `CodexModelSwitcher.exe` 即可使用。
4. 詳細操作步驟請看[新手完整教學](docs/新手教學.md)，或解壓縮後的 `使用說明.html`（離線版，內容相同）。

> **為什麼要放固定位置**：切換時程式會把自己的所在路徑寫入 Codex 設定，之後 Codex 需要靠這個路徑向切換器取得金鑰。若日後要搬移或刪除資料夾，請先按「恢復原始設定」，搬完再重新切換一次。

**開始之前請先了解**

- **需要自備 API Key**：本工具不含任何金鑰。DeepSeek 與 MiniMax 的金鑰請至各供應商官方平台申請，**使用費用由您自行負擔**。
- **金鑰只留在您的電腦**：保存於 Windows 認證管理員，不會寫入設定檔、不會隨 ZIP 散布；程式沒有遙測，也不會回傳任何資料。
- **Windows 可能顯示未知發行者警告**：本程式**尚未購買程式碼簽章**，首次執行時 SmartScreen 或防毒軟體可能出現警告，這是未簽章程式的預期行為。請自行核對下載來源，並比對 Release 頁面提供的 SHA256 後再決定是否執行。
- **切換後模型選單顯示「自訂」是正常現象**：桌面版 Codex 不會在原生選單列出第三方模型名稱；只要對話正常回應，就代表已由所選供應商處理，可到供應商用量頁核對 token 數字。
- **資料流提醒**：切換之後，Codex 為完成任務所送出的提示與專案內容片段會傳給您所選的供應商。請勿讓第三方模型處理未經授權的個資或機密資料。

> 本工具為個人開發的非官方工具，與 OpenAI、DeepSeek、MiniMax 均無隸屬關係。

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

## 版本

目前版本 **0.9.0**（可攜測試版）。版本號寫入執行檔，可於檔案內容的「詳細資料」頁確認。

## 授權

本專案採用 [MIT License](LICENSE)。本軟體按「現狀」提供，不附任何形式的擔保；使用第三方模型供應商所產生的費用與風險，由使用者自行負擔。
