#!/bin/bash
# Codex 多模型切換器 macOS 版（終端機原型）
#
# 與 Windows 版共用相同的設定檔協定：
# - 只修改 Codex config.toml 中與模型相關的欄位，其餘設定不動。
# - 首次切換前，以「# codex-model-switcher-saved-<key>: <base64>」標記保存原始行，
#   切回 OpenAI 時據此還原；標記格式與 Windows 版完全相同，兩平台可互相接手。
# - API Key 只存 macOS 鑰匙圈（Keychain），不寫入 config.toml、不寫入本專案資料夾。
# - Codex 以命令式驗證取得金鑰：本腳本的「token <供應商ID>」子命令只輸出金鑰本身。
# - 修改前備份（首次原始備份＋最近五份），寫入暫存檔驗證通過後才原子取代正式檔。
#
# 需求：macOS 內建工具（bash 3.2、security、curl、openssl、awk），無其他相依。

set -u

VERSION="0.1.0-mac-prototype"
CODEX_HOME="${CODEX_HOME:-$HOME/.codex}"
CONFIG_PATH="$CODEX_HOME/config.toml"
SWITCHER_DIR="$CODEX_HOME/model-switcher"
BACKUP_DIR="$SWITCHER_DIR/backups"
ORIGINAL_BACKUP="$BACKUP_DIR/original-config.toml"
NOTICE_FLAG="$SWITCHER_DIR/data-flow-notice-accepted.txt"
LOCK_DIR="$SWITCHER_DIR/config.lock.d"
KEYCHAIN_PREFIX="CodexModelSwitcher/provider/"
MARKER_PREFIX="# codex-model-switcher-saved-"
MANAGED_PREFIX="codex-switcher-"
MANAGED_KEYS="model model_provider model_reasoning_effort preferred_auth_method forced_login_method model_catalog_json"
RECENT_BACKUP_LIMIT=5
MAX_CONFIG_BYTES=5242880
MAX_KEY_LENGTH=1200

# ---------------------------------------------------------------------------
# 供應商目錄（內容對應 providers.json；原型版直接內建，避免 JSON 解析相依）
# ---------------------------------------------------------------------------
ROW_PROVIDER_ID=(openai deepseek deepseek minimax minimax)
ROW_PROVIDER_NAME=("OpenAI（Codex 原生）" "DeepSeek" "DeepSeek" "MiniMax" "MiniMax")
ROW_MODEL_ID=(codex-native deepseek-v4-flash deepseek-v4-pro MiniMax-M3 MiniMax-M2.7)
ROW_MODEL_NAME=("使用 Codex 原生模型選單" "DeepSeek V4 Flash" "DeepSeek V4 Pro" "MiniMax M3" "MiniMax M2.7")
ROW_BASE_URL=("-" "https://api.deepseek.com" "https://api.deepseek.com" "https://api.minimax.io/v1" "https://api.minimax.io/v1")
ROW_EFFORT=("-" "high" "high" "-" "-")
ROW_COUNT=5

SCRIPT_PATH="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"

say()  { printf '%s\n' "$1"; }
err()  { printf '%s\n' "$1" >&2; }
line() { printf '%s\n' "----------------------------------------"; }

# ---------------------------------------------------------------------------
# 鑰匙圈（等同 Windows 版的 CredentialVault）
# ---------------------------------------------------------------------------
validate_provider_id() {
    case "$1" in
        (*[!a-zA-Z0-9-]*|"") return 1 ;;
    esac
    [ ${#1} -le 48 ]
}

key_exists() {
    security find-generic-password -s "${KEYCHAIN_PREFIX}$1" -a "$1" >/dev/null 2>&1
}

key_read() {
    security find-generic-password -s "${KEYCHAIN_PREFIX}$1" -a "$1" -w 2>/dev/null
}

key_save() {
    provider_id="$1" secret="$2"
    if [ -z "$secret" ] || [ ${#secret} -gt $MAX_KEY_LENGTH ]; then
        err "API Key 格式不正確（不可空白、長度上限 ${MAX_KEY_LENGTH}）。"
        return 1
    fi
    case "$secret" in
        (*'"'*|*'\'*|*[[:cntrl:]]*)
            err "API Key 含有不允許的字元（引號、反斜線或控制字元）。"
            return 1 ;;
    esac
    # 經 security 互動模式由標準輸入傳遞，金鑰不出現在程序清單（ps）中。
    printf 'add-generic-password -U -s "%s" -a "%s" -w "%s"\n' \
        "${KEYCHAIN_PREFIX}$provider_id" "$provider_id" "$secret" | security -i >/dev/null 2>&1
}

key_delete() {
    security delete-generic-password -s "${KEYCHAIN_PREFIX}$1" -a "$1" >/dev/null 2>&1
}

# ---------------------------------------------------------------------------
# token 子命令：Codex 透過它取得金鑰（行為與 Windows 版 Program.cs 相同）
# ---------------------------------------------------------------------------
cmd_token() {
    if [ $# -ne 1 ] || ! validate_provider_id "$1"; then
        err "無法辨識命令。"
        exit 2
    fi
    secret="$(key_read "$1")" || { err "尚未設定此供應商的 API Key。"; exit 1; }
    printf '%s' "$secret"
    exit 0
}

# ---------------------------------------------------------------------------
# base64（openssl 版本在所有 macOS 皆可用）
# ---------------------------------------------------------------------------
b64enc() { printf '%s' "$1" | openssl base64 | tr -d '\n'; }
b64dec() { printf '%s\n' "$1" | openssl base64 -d 2>/dev/null; }

# ---------------------------------------------------------------------------
# config.toml 讀取
# ---------------------------------------------------------------------------
require_config() {
    if [ ! -f "$CONFIG_PATH" ]; then
        err "找不到 Codex 設定檔（${CONFIG_PATH}）。請先完整啟動一次 Codex。"
        return 1
    fi
    size=$(wc -c < "$CONFIG_PATH" | tr -d ' ')
    if [ "$size" -gt $MAX_CONFIG_BYTES ]; then
        err "Codex 設定檔過大，已停止操作。"
        return 1
    fi
}

# 取得根層級（第一個 [table] 之前）某個鍵的值（僅支援雙引號字串）
root_value() {
    awk -v key="$2" '
        /^[[:space:]]*\[/ { exit }
        {
            line = $0
            sub(/^[[:space:]]+/, "", line)
            if (index(line, key) == 1) {
                rest = substr(line, length(key) + 1)
                if (rest ~ /^[[:space:]]*=/) {
                    sub(/^[[:space:]]*=[[:space:]]*/, "", rest)
                    if (rest ~ /^"/) {
                        rest = substr(rest, 2)
                        sub(/".*$/, "", rest)
                        print rest
                    }
                    exit
                }
            }
        }
    ' "$1"
}

# 取得根層級某個鍵的完整原始行（用於製作標記）
root_line() {
    awk -v key="$2" '
        /^[[:space:]]*\[/ { exit }
        {
            line = $0
            sub(/^[[:space:]]+/, "", line)
            if (index(line, key) == 1) {
                rest = substr(line, length(key) + 1)
                if (rest ~ /^[[:space:]]*=/) { print $0; exit }
            }
        }
    ' "$1"
}

# 讀出某個鍵的既有標記值（base64 或 "-"；找不到輸出空字串）
marker_value() {
    awk -v pfx="${MARKER_PREFIX}$2: " '
        index($0, pfx) == 1 { print substr($0, length(pfx) + 1); exit }
    ' "$1"
}

is_managed_provider() {
    case "$1" in ("$MANAGED_PREFIX"*) return 0 ;; (*) return 1 ;; esac
}

# ---------------------------------------------------------------------------
# 備份（等同 Windows 版 BackupManager：首次原始備份＋最近五份）
# ---------------------------------------------------------------------------
backup_before_write() {
    mkdir -p "$BACKUP_DIR"
    if [ ! -f "$ORIGINAL_BACKUP" ]; then
        cp "$CONFIG_PATH" "$ORIGINAL_BACKUP"
    fi
    stamp="$(date +%Y%m%d-%H%M%S)-$$"
    cp "$CONFIG_PATH" "$BACKUP_DIR/switch-$stamp.toml"
    # 只保留最近五份 switch-*.toml（原始備份另計，永久保留）
    ls -t "$BACKUP_DIR"/switch-*.toml 2>/dev/null | awk -v keep=$RECENT_BACKUP_LIMIT 'NR > keep' | \
        while IFS= read -r old; do rm -f "$old"; done
}

# ---------------------------------------------------------------------------
# 檔案鎖（mkdir 為原子操作）
# ---------------------------------------------------------------------------
acquire_lock() {
    mkdir -p "$SWITCHER_DIR"
    if ! mkdir "$LOCK_DIR" 2>/dev/null; then
        err "另一個切換器正在修改設定，請稍後再試。"
        return 1
    fi
    trap 'rmdir "$LOCK_DIR" 2>/dev/null' EXIT INT TERM
}

release_lock() {
    rmdir "$LOCK_DIR" 2>/dev/null
    trap - EXIT INT TERM
}

# ---------------------------------------------------------------------------
# 核心：產生切換後 / 還原後的設定內容
# ---------------------------------------------------------------------------

# 移除：標記行、受管理根鍵行、受管理 [model_providers.codex-switcher-*] 表格，
# 並在根層級尾端（第一個 [table] 之前）插入 insert_file 的內容。
rewrite_config() {
    src="$1" insert_file="$2" out="$3"
    awk -v insert_file="$insert_file" -v marker_pfx="$MARKER_PREFIX" -v managed_pfx="$MANAGED_PREFIX" '
        BEGIN {
            inserted = 0
            skipping_table = 0
            held = 0
            n_keys = split("model model_provider model_reasoning_effort preferred_auth_method forced_login_method model_catalog_json", keys, " ")
        }
        function is_managed_root_key_line(s,   t, i, rest) {
            t = s
            sub(/^[[:space:]]+/, "", t)
            for (i = 1; i <= n_keys; i++) {
                if (index(t, keys[i]) == 1) {
                    rest = substr(t, length(keys[i]) + 1)
                    if (rest ~ /^[[:space:]]*=/) return 1
                }
            }
            return 0
        }
        function emit_insert(   ln) {
            while ((getline ln < insert_file) > 0) print ln
            close(insert_file)
            inserted = 1
        }
        {
            trimmed = $0
            sub(/^[[:space:]]+/, "", trimmed)

            if (trimmed ~ /^\[/) {
                if (!inserted) emit_insert()
                # 進入表格區：判斷是否為受管理表格。
                # 受管理表格前的分隔空白行一併捨棄，避免多次切換後累積空行。
                if (index(trimmed, "[model_providers." managed_pfx) == 1) {
                    held = 0
                    skipping_table = 1
                    next
                }
                skipping_table = 0
                while (held > 0) { print ""; held-- }
                print
                next
            }

            if (skipping_table) next

            if (inserted && trimmed == "") { held++; next }
            if (inserted) {
                while (held > 0) { print ""; held-- }
                print
                next
            }

            if (!inserted) {
                # 仍在根層級：跳過舊標記與受管理根鍵
                if (index(trimmed, marker_pfx) == 1) next
                if (is_managed_root_key_line($0)) next
                print
                next
            }

            print
        }
        END { if (!inserted) emit_insert() }
    ' "$src" > "$out"
}

# 依目前設定產生六個鍵的標記行，寫入 $1（標記檔）
build_markers() {
    markers_out="$1"
    current_provider="$(root_value "$CONFIG_PATH" model_provider)"
    [ -z "$current_provider" ] && current_provider="openai"
    existing="$(marker_value "$CONFIG_PATH" model)"

    : > "$markers_out"
    if [ -n "$existing" ]; then
        # 已有標記：原樣保留（原始 OpenAI 設定以第一次保存的為準）
        for key in $MANAGED_KEYS; do
            val="$(marker_value "$CONFIG_PATH" "$key")"
            if [ -z "$val" ]; then
                err "找不到完整的 OpenAI 原設定標記，已停止切換。"
                return 1
            fi
            printf '%s%s: %s\n' "$MARKER_PREFIX" "$key" "$val" >> "$markers_out"
        done
    elif is_managed_provider "$current_provider"; then
        err "設定已由切換器管理但缺少標記，已停止切換。請先「恢復原始設定」。"
        return 1
    else
        # 首次切換：保存目前每個受管理鍵的原始行（不存在則記為 -）
        for key in $MANAGED_KEYS; do
            orig="$(root_line "$CONFIG_PATH" "$key")"
            if [ -n "$orig" ]; then
                printf '%s%s: %s\n' "$MARKER_PREFIX" "$key" "$(b64enc "$orig")" >> "$markers_out"
            else
                printf '%s%s: -\n' "$MARKER_PREFIX" "$key" >> "$markers_out"
            fi
        done
    fi
}

# 切換到第三方供應商
apply_switch() {
    provider_id="$1" provider_name="$2" model_id="$3" base_url="$4" effort="$5"
    managed_id="${MANAGED_PREFIX}${provider_id}"
    tmp_insert="$(mktemp "$SWITCHER_DIR/insert.XXXXXX")"
    tmp_config="$(mktemp "$CODEX_HOME/config.toml.tmp.XXXXXX")"

    if ! build_markers "$tmp_insert"; then
        rm -f "$tmp_insert" "$tmp_config"
        return 1
    fi
    {
        printf 'model = "%s"\n' "$model_id"
        printf 'model_provider = "%s"\n' "$managed_id"
        [ "$effort" != "-" ] && printf 'model_reasoning_effort = "%s"\n' "$effort"
        printf 'preferred_auth_method = "apikey"\n'
        printf 'forced_login_method = "api"\n'
    } >> "$tmp_insert"

    rewrite_config "$CONFIG_PATH" "$tmp_insert" "$tmp_config"
    {
        printf '\n[model_providers.%s]\n' "$managed_id"
        printf 'name = "%s"\n' "$provider_name"
        printf 'base_url = "%s"\n' "$base_url"
        printf 'wire_api = "responses"\n'
        printf '\n[model_providers.%s.auth]\n' "$managed_id"
        printf 'command = "/bin/bash"\n'
        printf 'args = ["%s", "token", "%s"]\n' "$SCRIPT_PATH" "$provider_id"
        printf 'timeout_ms = 5000\n'
    } >> "$tmp_config"

    # 寫入前驗證：暫存設定必須讀得回目標供應商與模型
    if [ "$(root_value "$tmp_config" model_provider)" != "$managed_id" ] || \
       [ "$(root_value "$tmp_config" model)" != "$model_id" ]; then
        rm -f "$tmp_insert" "$tmp_config"
        err "切換後設定驗證失敗，原設定未被修改。"
        return 1
    fi

    backup_before_write
    mv "$tmp_config" "$CONFIG_PATH"
    rm -f "$tmp_insert"
}

# 切回 OpenAI：解碼標記並還原原始行
apply_openai() {
    current_provider="$(root_value "$CONFIG_PATH" model_provider)"
    [ -z "$current_provider" ] && current_provider="openai"
    existing="$(marker_value "$CONFIG_PATH" model)"
    if [ -z "$existing" ] && ! is_managed_provider "$current_provider"; then
        say "目前已是 OpenAI 原生設定，無需切換。"
        return 0
    fi

    tmp_insert="$(mktemp "$SWITCHER_DIR/insert.XXXXXX")"
    tmp_config="$(mktemp "$CODEX_HOME/config.toml.tmp.XXXXXX")"
    : > "$tmp_insert"
    for key in $MANAGED_KEYS; do
        val="$(marker_value "$CONFIG_PATH" "$key")"
        if [ -z "$val" ]; then
            rm -f "$tmp_insert" "$tmp_config"
            err "原始 OpenAI 模型設定標記不完整，已停止切換。可改用「恢復原始設定」。"
            return 1
        fi
        if [ "$val" != "-" ]; then
            decoded="$(b64dec "$val")"
            if [ -z "$decoded" ]; then
                rm -f "$tmp_insert" "$tmp_config"
                err "標記內容無法解碼，已停止切換。可改用「恢復原始設定」。"
                return 1
            fi
            printf '%s\n' "$decoded" >> "$tmp_insert"
        fi
    done

    rewrite_config "$CONFIG_PATH" "$tmp_insert" "$tmp_config"
    restored_provider="$(root_value "$tmp_config" model_provider)"
    if is_managed_provider "${restored_provider:-openai}"; then
        rm -f "$tmp_insert" "$tmp_config"
        err "還原後設定驗證失敗，原設定未被修改。"
        return 1
    fi

    backup_before_write
    mv "$tmp_config" "$CONFIG_PATH"
    rm -f "$tmp_insert"
}

# ---------------------------------------------------------------------------
# 其他保護
# ---------------------------------------------------------------------------
codex_is_running() {
    pgrep -ix 'codex' >/dev/null 2>&1 || pgrep -ix 'openai\.codex' >/dev/null 2>&1
}

require_codex_closed() {
    if codex_is_running; then
        say "Codex 目前仍在執行。請先保存工作並自行關閉 Codex，再回來執行；本工具不會強制關閉 Codex。"
        return 1
    fi
}

confirm_data_flow() {
    provider_id="$1"
    [ "$provider_id" = "openai" ] && return 0
    [ -f "$NOTICE_FLAG" ] && return 0
    line
    say "第一次啟用第三方供應商前的提醒："
    say ""
    say "切換之後，Codex 為了完成您交付的任務，會把您輸入的提示與相關專案內容"
    say "片段，傳送給目前選擇的模型供應商處理。"
    say ""
    say "請不要讓第三方模型處理未經授權的個資或機密資料。"
    say "切換器本身不會讀取或上傳您的專案內容。"
    line
    printf '瞭解並同意後才會繼續切換。要繼續嗎？（輸入 yes 繼續）：'
    read -r answer
    [ "$answer" = "yes" ] || return 1
    mkdir -p "$SWITCHER_DIR"
    printf '已於 %s 顯示並同意資料流提醒。\n' "$(date '+%Y-%m-%d %H:%M')" > "$NOTICE_FLAG"
}

# ---------------------------------------------------------------------------
# 連線測試（等同 Windows 版：固定內容、16 token 上限、不重試）
# ---------------------------------------------------------------------------
describe_http_status() {
    case "$1" in
        400) say "請求格式不相容：供應商拒絕測試格式。" ;;
        401|403) say "API Key 驗證失敗：請重新設定這個供應商的 API Key。" ;;
        402) say "API 餘額不足：請至供應商帳戶確認餘額。" ;;
        404) say "找不到 API 端點：供應商網址或 Responses API 可能已變更。" ;;
        422) say "模型或參數無法使用：所選模型可能已更名或停用。" ;;
        429) say "目前受到限流：請稍後再試；程式不會自動重試。" ;;
        500) say "供應商服務異常：請稍後再試。" ;;
        502|503|504) say "供應商暫時無法使用：可能過載或維護中。" ;;
        000) say "連線失敗或逾時（30 秒）：請確認網路可用；程式不會自動重試。" ;;
        *)   say "連線測試未通過：供應商回傳 HTTP $1，程式未保存回應內容。" ;;
    esac
}

run_connection_test() {
    provider_id="$1" model_id="$2" base_url="$3"
    if ! key_exists "$provider_id"; then
        say "尚未設定 $provider_id 的 API Key，請先從選單設定。"
        return 1
    fi
    printf '測試會送出一段固定文字（上限 16 個輸出 token），可能產生極少量 API 費用。繼續嗎？(y/N)：'
    read -r go
    case "$go" in (y|Y) ;; (*) say "已取消。"; return 0 ;; esac

    secret="$(key_read "$provider_id")"
    status=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 \
        -H "Authorization: Bearer $secret" \
        -H "Content-Type: application/json" \
        -d "{\"model\":\"$model_id\",\"input\":\"Reply with exactly OK.\",\"max_output_tokens\":16,\"stream\":false}" \
        "$base_url/responses" 2>/dev/null) || status=000
    secret=""
    if [ "$status" = "200" ]; then
        say "連線測試成功：金鑰、模型與 Responses API 均可正常使用。"
    else
        describe_http_status "$status"
        say "診斷摘要：供應商=${provider_id}；模型=${model_id}；HTTP=$status"
        return 1
    fi
}

# ---------------------------------------------------------------------------
# 選單動作
# ---------------------------------------------------------------------------
show_status() {
    line
    if [ ! -f "$CONFIG_PATH" ]; then
        say "尚未找到 Codex 設定（${CONFIG_PATH}）。請先啟動一次 Codex。"
        return
    fi
    provider="$(root_value "$CONFIG_PATH" model_provider)"
    model="$(root_value "$CONFIG_PATH" model)"
    [ -z "$provider" ] && provider="openai（預設）"
    if is_managed_provider "$provider"; then
        say "目前供應商：${provider#$MANAGED_PREFIX}（由切換器管理）"
    else
        say "目前供應商：$provider"
    fi
    say "目前模型　：${model:-（Codex 原生選單）}"
    if [ -f "$ORIGINAL_BACKUP" ]; then
        say "原始備份　：$(date -r "$ORIGINAL_BACKUP" '+%Y-%m-%d %H:%M')（可隨時恢復）"
    else
        say "原始備份　：尚未建立（第一次切換時自動建立）"
    fi
    for pid in deepseek minimax; do
        if key_exists "$pid"; then
            say "API Key（${pid}）：已安全保存於鑰匙圈"
        else
            say "API Key（${pid}）：尚未設定"
        fi
    done
    line
}

pick_target() {
    say "可切換的目標："
    i=0
    while [ $i -lt $ROW_COUNT ]; do
        printf '  %d) %s ─ %s\n' $((i + 1)) "${ROW_PROVIDER_NAME[$i]}" "${ROW_MODEL_NAME[$i]}"
        i=$((i + 1))
    done
    printf '請選擇（1-%d，其他鍵取消）：' $ROW_COUNT
    read -r choice
    case "$choice" in
        (*[!0-9]*|"") return 1 ;;
    esac
    [ "$choice" -ge 1 ] && [ "$choice" -le $ROW_COUNT ] || return 1
    TARGET=$((choice - 1))
}

menu_switch() {
    require_config || return 1
    pick_target || { say "已取消。"; return 0; }
    pid="${ROW_PROVIDER_ID[$TARGET]}"
    require_codex_closed || return 1

    if [ "$pid" = "openai" ]; then
        acquire_lock || return 1
        if apply_openai; then
            say "已切回 OpenAI 原生設定。現在可以重新開啟 Codex。"
        fi
        release_lock
        return 0
    fi

    if ! key_exists "$pid"; then
        say "尚未設定 ${ROW_PROVIDER_NAME[$TARGET]} 的 API Key。"
        menu_key_setup "$pid" || return 1
        key_exists "$pid" || return 1
    fi
    confirm_data_flow "$pid" || { say "已取消切換。"; return 0; }

    printf '即將切換為：%s ─ %s。切換前會自動備份。確定？(y/N)：' \
        "${ROW_PROVIDER_NAME[$TARGET]}" "${ROW_MODEL_NAME[$TARGET]}"
    read -r go
    case "$go" in (y|Y) ;; (*) say "已取消。"; return 0 ;; esac

    acquire_lock || return 1
    if apply_switch "$pid" "${ROW_PROVIDER_NAME[$TARGET]}" "${ROW_MODEL_ID[$TARGET]}" \
        "${ROW_BASE_URL[$TARGET]}" "${ROW_EFFORT[$TARGET]}"; then
        say "已安全切換為 ${ROW_PROVIDER_NAME[$TARGET]}／${ROW_MODEL_NAME[$TARGET]}。請重新開啟 Codex。"
        say "提醒：Codex 模型選單顯示「自訂」是正常現象。"
    fi
    release_lock
}

menu_key_setup() {
    if [ $# -eq 1 ]; then
        pid="$1"
    else
        printf '要管理哪個供應商的 API Key？（deepseek / minimax）：'
        read -r pid
    fi
    validate_provider_id "$pid" || { say "供應商識別碼格式不正確。"; return 1; }
    case "$pid" in (deepseek|minimax) ;; (*) say "原型版僅支援 deepseek 與 minimax。"; return 1 ;; esac

    if key_exists "$pid"; then
        say "此供應商已保存金鑰。1) 更換  2) 刪除  3) 取消"
        printf '請選擇：'
        read -r act
        case "$act" in
            2) key_delete "$pid" && say "已從鑰匙圈刪除 $pid 的 API Key。"; return 0 ;;
            1) ;;
            *) return 0 ;;
        esac
    fi
    printf '請貼上 %s 的 API Key（輸入時不顯示）：' "$pid"
    read -r -s new_key
    printf '\n'
    if key_save "$pid" "$new_key"; then
        say "已將 $pid 的 API Key 安全保存至 macOS 鑰匙圈。"
    else
        say "保存失敗，金鑰未寫入。"
        return 1
    fi
    new_key=""
}

menu_test() {
    pick_target || { say "已取消。"; return 0; }
    pid="${ROW_PROVIDER_ID[$TARGET]}"
    if [ "$pid" = "openai" ]; then
        say "OpenAI 沿用 Codex 原生登入，不需要連線測試。"
        return 0
    fi
    run_connection_test "$pid" "${ROW_MODEL_ID[$TARGET]}" "${ROW_BASE_URL[$TARGET]}"
}

menu_restore() {
    require_config || return 1
    if [ ! -f "$ORIGINAL_BACKUP" ]; then
        say "找不到首次切換前的原始設定備份（尚未執行過切換）。"
        return 1
    fi
    require_codex_closed || return 1
    printf '將恢復為 %s 的原始設定。確定？(y/N)：' "$(date -r "$ORIGINAL_BACKUP" '+%Y-%m-%d %H:%M')"
    read -r go
    case "$go" in (y|Y) ;; (*) say "已取消。"; return 0 ;; esac
    acquire_lock || return 1
    backup_before_write
    cp "$ORIGINAL_BACKUP" "$CONFIG_PATH"
    release_lock
    say "原始 Codex 設定已恢復。請重新開啟 Codex，確認原有模型、插件與 MCP Server。"
    say "提醒：恢復設定不會刪除鑰匙圈裡的 API Key，可從「管理 API Key」另行刪除。"
}

main_menu() {
    say "Codex 多模型切換器 macOS 版（原型 ${VERSION}）"
    while :; do
        line
        say "1) 查看目前狀態"
        say "2) 切換模型"
        say "3) 設定／管理 API Key"
        say "4) 測試連線"
        say "5) 恢復原始設定"
        say "6) 離開"
        printf '請選擇：'
        read -r pick
        case "$pick" in
            1) show_status ;;
            2) menu_switch ;;
            3) menu_key_setup ;;
            4) menu_test ;;
            5) menu_restore ;;
            6|q) exit 0 ;;
            *) say "請輸入 1-6。" ;;
        esac
    done
}

# ---------------------------------------------------------------------------
# 進入點
# ---------------------------------------------------------------------------
if [ $# -gt 0 ]; then
    case "$1" in
        token) shift; cmd_token "$@" ;;
        *) err "無法辨識命令。"; exit 2 ;;
    esac
fi
main_menu
