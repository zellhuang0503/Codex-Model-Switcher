# Codex 多模型切換器 macOS 版（終端機原型）

> **這是原型測試版**，功能與 Windows 版相同（切換、備份、還原、金鑰保管），但操作方式是**終端機文字選單**，不是圖形視窗。
>
> **為什麼 Mac 版長這樣？** macOS 對沒有 Apple 簽章與公證的圖形程式會直接顯示「已損毀」並阻止執行，簽章需要每年付費的開發者帳號。改用終端機腳本＋`git clone` 取得，就完全沒有這個問題，工具可以永久免費分享。

---

## 開始之前

- macOS 12（Monterey）或更新版本
- 已安裝 Codex，而且**至少成功啟動過一次**
- 需要自備 DeepSeek 或 MiniMax 的 API Key（申請方式見[新手完整教學](../docs/新手教學.md)的步驟 3，費用自付）

## 安裝（只需做一次）

打開「終端機」（Terminal，可用 Spotlight 搜尋 ⌘+空白鍵 → 輸入 Terminal），逐行貼上：

```bash
git clone https://github.com/zellhuang0503/Codex-Model-Switcher.git
```

> 第一次用 `git` 時，macOS 可能跳出「需要安裝命令列開發者工具」——按「安裝」等它跑完（免費，約幾分鐘），再執行一次上面的指令。

## 每次使用

```bash
cd Codex-Model-Switcher/mac
./codex-switch.sh
```

會出現文字選單：

```
1) 查看目前狀態
2) 切換模型
3) 設定／管理 API Key
4) 測試連線
5) 恢復原始設定
6) 離開
```

**建議的首次流程**：`3` 設定 API Key →（貼上金鑰，畫面不會顯示，貼完按 Enter）→ `4` 測試連線 → 關閉 Codex → `2` 切換模型 → 重新開啟 Codex。

## 你的金鑰放在哪裡

- 保存在 macOS 內建的**鑰匙圈（Keychain）**，項目名稱為 `CodexModelSwitcher/provider/<供應商>`。
- **不會**寫進 Codex 設定檔、**不會**放在這個資料夾裡；把資料夾分享給別人不會帶走你的金鑰。
- 想親眼確認或手動刪除：打開內建的「鑰匙圈存取」App 搜尋 `CodexModelSwitcher`。
- Codex 需要金鑰時，會執行 `codex-switch.sh token <供應商>` 即時取用，金鑰不落地。

## 注意事項

- **資料夾位置不要搬動**：切換時腳本會把自己的路徑寫進 Codex 設定。要搬移前先執行選單 `5` 恢復原始設定，搬完重新切換一次即可。
- 切換前請**自行關閉 Codex**（腳本會檢查並提醒，不會強制關閉）。
- 切換後 Codex 模型選單顯示「**自訂**」是正常現象；到供應商用量頁核對 token 數即可確認生效。
- 每次切換前會自動備份：首次原始設定永久保留，另保留最近五份（位於 `~/.codex/model-switcher/backups/`）。

## 完全移除

1. 執行 `./codex-switch.sh` → 選 `5` 恢復原始設定
2. 選 `3` 刪除各供應商金鑰（或在「鑰匙圈存取」刪除 `CodexModelSwitcher` 開頭的項目）
3. 刪除 `Codex-Model-Switcher` 資料夾
4. （選用）刪除 `~/.codex/model-switcher/` 備份資料夾

## 原型版與 Windows 版的差異

| 項目 | Windows 0.9.0 | macOS 原型 |
| --- | --- | --- |
| 操作介面 | 圖形視窗 | 終端機選單 |
| 切換／備份／還原／金鑰保管 | ✅ | ✅（協定相同） |
| 連線測試 | ✅ | ✅（僅文字測試，未含工具呼叫驗證） |
| 自訂供應商 | ✅ | ❌ 尚未支援 |
| 供應商目錄 | `providers.json` | 內建於腳本（內容相同） |

設定檔標記格式與 Windows 版完全相同，同一份 `config.toml` 由哪一版切換、由哪一版還原都可以。

## 問題回報

到 [Issues](../../../issues) 回報時請附上：macOS 版本、畫面上的錯誤訊息文字。**請勿貼上 API Key 或含金鑰的截圖。**
