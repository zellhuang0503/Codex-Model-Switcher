using System.Drawing;

namespace CodexModelSwitcher;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(24, 34, 53);
    private static readonly Color Accent = Color.FromArgb(49, 87, 213);
    private static readonly Color Canvas = Color.FromArgb(243, 246, 251);
    private static readonly Color Muted = Color.FromArgb(91, 103, 122);
    private static readonly Color Safe = Color.FromArgb(23, 134, 90);

    private ComboBox? providerSelector;
    private ComboBox? modelSelector;
    private Label? keyStatusValue;
    private Label? footerStatus;
    private Button? keyButton;
    private Button? testButton;
    private Button? restoreButton;
    private Button? switchButton;
    private Button? addCustomButton;
    private Button? removeCustomButton;
    private Label? currentSummary;
    private Label? currentProviderValue;
    private Label? currentModelValue;
    private readonly CodexConfigManager configManager = CodexConfigManager.ForCurrentUser();
    private readonly ConnectionTester connectionTester = new();
    private readonly CancellationTokenSource connectionTestCancellation = new();
    private bool connectionTestRunning;

    public MainForm()
    {
        Text = "Codex 多模型切換器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(940, 650);
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        Controls.Add(BuildPage());
        FormClosing += (_, _) => connectionTestCancellation.Cancel();
        FormClosed += (_, _) =>
        {
            connectionTester.Dispose();
            connectionTestCancellation.Dispose();
        };

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        PerformAutoScale();

        LoadProviderOptions();
        LoadCurrentConfig();
    }

    private Control BuildPage()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 142F));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

        page.Controls.Add(BuildHeader(), 0, 0);
        page.Controls.Add(BuildWorkspace(), 0, 1);
        page.Controls.Add(BuildFooter(), 0, 2);
        return page;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Ink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(36, 25, 36, 22),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleBlock = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Ink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        titleBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleBlock.Controls.Add(MakeLabel(
            "Codex 多模型切換器",
            24F,
            FontStyle.Bold,
            Color.White,
            new Padding(0, 0, 0, 5)), 0, 0);
        titleBlock.Controls.Add(MakeLabel(
            "在一個安全入口管理 Codex 的模型路線",
            10.5F,
            FontStyle.Regular,
            Color.FromArgb(193, 202, 220)), 0, 1);

        var phaseBadge = MakeLabel(
            "MVP · 安全切換",
            9.5F,
            FontStyle.Bold,
            Color.White,
            new Padding(14, 8, 14, 8));
        phaseBadge.BackColor = Accent;
        phaseBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        phaseBadge.AutoSize = true;

        header.Controls.Add(titleBlock, 0, 0);
        header.Controls.Add(phaseBadge, 1, 0);
        return header;
    }

    private Control BuildWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(30, 28, 30, 28),
            Margin = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var currentCard = BuildCurrentModelCard();
        currentCard.Margin = new Padding(0, 0, 12, 0);

        var switchCard = BuildSwitchCard();
        switchCard.Margin = new Padding(12, 0, 0, 0);

        workspace.Controls.Add(currentCard, 0, 0);
        workspace.Controls.Add(switchCard, 1, 0);
        return workspace;
    }

    private Control BuildCurrentModelCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(26, 24, 26, 24),
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(MakeEyebrow("目前使用中"), 0, 0);
        currentSummary = MakeLabel(
            "尚未讀取 Codex 設定",
            17F,
            FontStyle.Bold,
            Ink,
            new Padding(0, 8, 0, 0));
        layout.Controls.Add(currentSummary, 0, 1);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        details.Controls.Add(MakeDetailLabel("供應商"), 0, 0);
        currentProviderValue = MakeDetailValue("—");
        details.Controls.Add(currentProviderValue, 1, 0);
        details.Controls.Add(MakeDetailLabel("模型"), 0, 1);
        currentModelValue = MakeDetailValue("—");
        details.Controls.Add(currentModelValue, 1, 1);
        layout.Controls.Add(details, 0, 3);

        var safetyBox = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            BackColor = Color.FromArgb(235, 247, 241),
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 18, 0, 0)
        };
        safetyBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        safetyBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        safetyBox.Controls.Add(MakeLabel("●", 10F, FontStyle.Regular, Safe, new Padding(0, 0, 8, 0)), 0, 0);
        safetyBox.Controls.Add(MakeLabel(
            "安全模式\n切換前先備份，且只修改模型相關設定。",
            9.5F,
            FontStyle.Regular,
            Color.FromArgb(29, 94, 70)), 1, 0);
        layout.Controls.Add(safetyBox, 0, 5);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildSwitchCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            Padding = new Padding(28, 24, 28, 24),
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(MakeEyebrow("準備切換"), 0, 0);
        layout.Controls.Add(MakeFieldLabel("供應商"), 0, 2);
        providerSelector = MakeSelector("供應商");
        providerSelector.SelectedIndexChanged += ProviderSelectionChanged;
        layout.Controls.Add(providerSelector, 0, 3);
        layout.Controls.Add(MakeFieldLabel("模型"), 0, 4);
        modelSelector = MakeSelector("模型");
        modelSelector.SelectedIndexChanged += (_, _) => RefreshActionState();
        layout.Controls.Add(modelSelector, 0, 5);

        var keyStatus = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(247, 249, 252),
            Padding = new Padding(14, 11, 14, 11),
            Margin = new Padding(0, 16, 0, 0)
        };
        keyStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        keyStatus.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        keyStatus.Controls.Add(MakeLabel("API Key 狀態", 9.5F, FontStyle.Regular, Muted), 0, 0);
        keyStatusValue = MakeLabel("尚未設定", 9.5F, FontStyle.Bold, Ink);
        keyStatus.Controls.Add(keyStatusValue, 1, 0);
        layout.Controls.Add(keyStatus, 0, 6);

        var customRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0),
            Padding = Padding.Empty
        };
        addCustomButton = MakeCompactButton("新增自訂供應商…", Ink);
        addCustomButton.Click += AddCustomProviderClicked;
        removeCustomButton = MakeCompactButton("移除自訂供應商", Color.Firebrick);
        removeCustomButton.Visible = false;
        removeCustomButton.Click += RemoveCustomProviderClicked;
        customRow.Controls.Add(addCustomButton);
        customRow.Controls.Add(removeCustomButton);
        layout.Controls.Add(customRow, 0, 7);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 22, 0, 0),
            Padding = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        keyButton = MakeButton("設定 API Key");
        keyButton.Margin = new Padding(0, 0, 6, 6);
        keyButton.Click += ManageKeyClicked;
        testButton = MakeButton("測試連線");
        testButton.Margin = new Padding(6, 0, 0, 6);
        testButton.Click += TestConnectionClicked;
        restoreButton = MakeButton("恢復原始設定");
        restoreButton.Margin = new Padding(0, 6, 6, 0);
        restoreButton.Click += RestoreOriginalClicked;
        switchButton = MakeButton("安全切換設定", true);
        switchButton.Margin = new Padding(6, 6, 0, 0);
        switchButton.Click += SwitchConfigurationClicked;

        actions.Controls.Add(keyButton, 0, 0);
        actions.Controls.Add(testButton, 1, 0);
        actions.Controls.Add(restoreButton, 0, 1);
        actions.Controls.Add(switchButton, 1, 1);
        layout.Controls.Add(actions, 0, 9);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(30, 0, 30, 0),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        footerStatus = MakeLabel(
            "所有切換功能尚未啟用，您目前的 Codex 不會受到影響。",
            9F,
            FontStyle.Regular,
            Muted);
        footerStatus.Anchor = AnchorStyles.Left;

        var version = MakeLabel("Windows 可攜版 MVP", 9F, FontStyle.Bold, Accent);
        version.Anchor = AnchorStyles.Right;

        footer.Controls.Add(footerStatus, 0, 0);
        footer.Controls.Add(version, 1, 0);
        return footer;
    }

    private static Panel MakeCard() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Label MakeLabel(
        string text,
        float size,
        FontStyle style,
        Color color,
        Padding? padding = null)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", size, style, GraphicsUnit.Point),
            ForeColor = color,
            Padding = padding ?? Padding.Empty,
            Margin = Padding.Empty,
            UseMnemonic = false
        };
    }

    private static Label MakeEyebrow(string text)
    {
        var label = MakeLabel(text, 9F, FontStyle.Bold, Accent);
        label.Text = text.ToUpperInvariant();
        return label;
    }

    private static Label MakeDetailLabel(string text) => MakeLabel(
        text,
        9.5F,
        FontStyle.Regular,
        Muted,
        new Padding(0, 7, 0, 7));

    private static Label MakeDetailValue(string text) => MakeLabel(
        text,
        10F,
        FontStyle.Bold,
        Ink,
        new Padding(0, 7, 0, 7));

    private static Label MakeFieldLabel(string text) => MakeLabel(
        text,
        9.5F,
        FontStyle.Bold,
        Ink,
        new Padding(0, 7, 0, 6));

    private static ComboBox MakeSelector(string accessibleName)
    {
        return new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = true,
            Height = 34,
            Margin = Padding.Empty,
            IntegralHeight = false,
            AccessibleName = accessibleName
        };
    }

    private void LoadProviderOptions(string? preferredProviderId = null)
    {
        if (providerSelector is null || modelSelector is null || footerStatus is null)
        {
            return;
        }

        var result = ProviderCatalog.Load(configManager.CustomProvidersPath);
        providerSelector.BeginUpdate();
        try
        {
            providerSelector.Items.Clear();
            foreach (var provider in result.Providers)
            {
                providerSelector.Items.Add(provider);
            }
        }
        finally
        {
            providerSelector.EndUpdate();
        }

        footerStatus.Text = result.Notice;
        var targetIndex = providerSelector.Items.Count > 0 ? 0 : -1;
        if (preferredProviderId is not null)
        {
            for (var index = 0; index < providerSelector.Items.Count; index++)
            {
                if (providerSelector.Items[index] is ProviderDefinition candidate &&
                    candidate.Id == preferredProviderId)
                {
                    targetIndex = index;
                    break;
                }
            }
        }

        providerSelector.SelectedIndex = targetIndex;
    }

    private void ProviderSelectionChanged(object? sender, EventArgs e)
    {
        if (providerSelector?.SelectedItem is not ProviderDefinition provider ||
            modelSelector is null ||
            keyStatusValue is null)
        {
            return;
        }

        modelSelector.BeginUpdate();
        try
        {
            modelSelector.Items.Clear();
            foreach (var model in provider.Models)
            {
                modelSelector.Items.Add(model);
            }
        }
        finally
        {
            modelSelector.EndUpdate();
        }

        modelSelector.SelectedIndex = modelSelector.Items.Count > 0 ? 0 : -1;
        keyStatusValue.Text = provider.RequiresApiKey ? "尚未設定" : "使用 Codex 原登入";
        keyStatusValue.ForeColor = provider.RequiresApiKey ? Ink : Safe;
        if (removeCustomButton is not null)
        {
            removeCustomButton.Visible = ProviderCatalog.IsCustomProviderId(provider.Id);
        }

        RefreshCredentialState(provider);
        RefreshActionState();
    }

    private void AddCustomProviderClicked(object? sender, EventArgs e)
    {
        using var dialog = new CustomProviderDialog(
            connectionTester,
            configManager.CustomProvidersPath,
            connectionTestCancellation.Token);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SavedProvider is null)
        {
            return;
        }

        LoadProviderOptions(dialog.SavedProvider.Id);
        footerStatus!.Text = $"已新增自訂供應商 {dialog.SavedProvider.DisplayName}，並通過文字與工具呼叫驗證。";
    }

    private void RemoveCustomProviderClicked(object? sender, EventArgs e)
    {
        if (providerSelector?.SelectedItem is not ProviderDefinition provider ||
            !ProviderCatalog.IsCustomProviderId(provider.Id))
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"確定要移除自訂供應商 {provider.DisplayName} 嗎？",
                "移除自訂供應商",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            ProviderCatalog.RemoveCustomProvider(configManager.CustomProvidersPath, provider.Id);
            var keyDeleted = false;
            try
            {
                if (CredentialVault.Exists(provider.Id) &&
                    MessageBox.Show(
                        this,
                        "是否同時刪除已保存的 API Key？",
                        "刪除 API Key",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    CredentialVault.Delete(provider.Id);
                    keyDeleted = true;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 金鑰狀態無法讀取時仍完成供應商移除，金鑰可之後手動處理。
            }

            LoadProviderOptions();
            footerStatus!.Text = keyDeleted
                ? $"已移除自訂供應商 {provider.DisplayName} 與其 API Key。"
                : $"已移除自訂供應商 {provider.DisplayName}。";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "移除失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshCredentialState(ProviderDefinition provider)
    {
        if (keyStatusValue is null || keyButton is null)
        {
            return;
        }

        keyButton.Enabled = provider.RequiresApiKey;
        keyButton.TabStop = provider.RequiresApiKey;
        if (!provider.RequiresApiKey)
        {
            keyButton.Text = "不需要設定金鑰";
            keyStatusValue.Text = "使用 Codex 原登入";
            keyStatusValue.ForeColor = Safe;
            return;
        }

        try
        {
            var saved = CredentialVault.Exists(provider.Id);
            keyButton.Text = saved ? "管理 API Key" : "設定 API Key";
            keyStatusValue.Text = saved ? "已安全保存" : "尚未設定";
            keyStatusValue.ForeColor = saved ? Safe : Ink;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            keyButton.Text = "設定 API Key";
            keyStatusValue.Text = "無法讀取狀態";
            keyStatusValue.ForeColor = Color.Firebrick;
        }
    }

    private void ManageKeyClicked(object? sender, EventArgs e)
    {
        if (providerSelector?.SelectedItem is not ProviderDefinition provider || !provider.RequiresApiKey)
        {
            return;
        }

        var alreadySaved = false;
        try
        {
            alreadySaved = CredentialVault.Exists(provider.Id);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            ShowSafeError(exception.Message);
            return;
        }

        using var dialog = new ApiKeyDialog(provider.DisplayName, alreadySaved);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (dialog.DeleteRequested)
            {
                CredentialVault.Delete(provider.Id);
                footerStatus!.Text = $"已刪除 {provider.DisplayName} 的 API Key。";
            }
            else
            {
                CredentialVault.Save(provider.Id, dialog.ApiKey);
                footerStatus!.Text = $"已將 {provider.DisplayName} 的 API Key 安全保存至 Windows 認證管理員。";
            }

            RefreshCredentialState(provider);
            RefreshActionState();
        }
        catch (Exception exception) when (exception is ArgumentException or System.ComponentModel.Win32Exception)
        {
            ShowSafeError(exception.Message);
        }
    }

    private void ShowSafeError(string message)
    {
        MessageBox.Show(this, message, "無法管理 API Key", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void LoadCurrentConfig()
    {
        if (currentSummary is null || currentProviderValue is null || currentModelValue is null || footerStatus is null)
        {
            return;
        }

        try
        {
            var current = configManager.ReadCurrent();
            currentSummary.Text = current.ConfigExists ? "目前 Codex 模型設定" : "尚未找到 Codex 設定";
            currentProviderValue.Text = current.ProviderDisplayName;
            currentModelValue.Text = current.ModelDisplayName;
            footerStatus.Text = current.Notice;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            currentSummary.Text = "無法安全讀取 Codex 設定";
            currentProviderValue.Text = "—";
            currentModelValue.Text = "—";
            footerStatus.Text = exception.Message;
        }

        RefreshActionState();
    }

    private void RefreshActionState()
    {
        if (switchButton is null || restoreButton is null || testButton is null)
        {
            return;
        }

        var hasSelection = providerSelector?.SelectedItem is ProviderDefinition &&
                           modelSelector?.SelectedItem is ModelDefinition;
        var hasRequiredKey = true;
        if (providerSelector?.SelectedItem is ProviderDefinition provider && provider.RequiresApiKey)
        {
            try
            {
                hasRequiredKey = CredentialVault.Exists(provider.Id);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                hasRequiredKey = false;
            }
        }

        switchButton.Enabled = hasSelection && hasRequiredKey && configManager.ConfigExists;
        switchButton.TabStop = switchButton.Enabled;
        restoreButton.Enabled = configManager.CanRestoreOriginal;
        restoreButton.TabStop = restoreButton.Enabled;
        var canTest = providerSelector?.SelectedItem is ProviderDefinition selectedProvider &&
                      selectedProvider.RequiresApiKey &&
                      hasSelection &&
                      hasRequiredKey &&
                      !connectionTestRunning;
        testButton.Enabled = canTest;
        testButton.TabStop = canTest;
    }

    private async void TestConnectionClicked(object? sender, EventArgs e)
    {
        if (providerSelector?.SelectedItem is not ProviderDefinition provider ||
            modelSelector?.SelectedItem is not ModelDefinition model ||
            connectionTestRunning)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"即將向 {provider.DisplayName} 送出一次最短測試：\n\n模型：{model.DisplayName}\n內容：固定測試文字，不含任何專案資料\n限制：最多 16 個輸出 token，不自動重試\n\n這次測試可能產生極少量 API 費用。確定繼續嗎？",
                "確認付費連線測試",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        string apiKey;
        try
        {
            apiKey = CredentialVault.Read(provider.Id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowSafeError(exception.Message);
            return;
        }

        connectionTestRunning = true;
        testButton!.Text = "測試中…";
        footerStatus!.Text = $"正在測試 {provider.DisplayName}／{model.DisplayName}，不會自動重試。";
        RefreshActionState();
        try
        {
            var result = await connectionTester.TestAsync(provider, model, apiKey, connectionTestCancellation.Token);
            if (IsDisposed || Disposing)
            {
                return;
            }
            var usage = result.InputTokens is null && result.OutputTokens is null
                ? string.Empty
                : $"\n\n本次用量：輸入 {result.InputTokens?.ToString() ?? "未提供"} token；輸出 {result.OutputTokens?.ToString() ?? "未提供"} token。";
            var message = $"{result.UserMessage}{usage}\n\n診斷摘要：{result.DiagnosticSummary}";
            footerStatus.Text = result.Succeeded
                ? $"{provider.DisplayName}／{model.DisplayName} 連線測試成功。"
                : $"{provider.DisplayName}／{model.DisplayName} 連線測試未通過。";
            MessageBox.Show(
                this,
                message,
                result.Title,
                MessageBoxButtons.OK,
                result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        finally
        {
            connectionTestRunning = false;
            apiKey = string.Empty;
            if (!IsDisposed && !Disposing)
            {
                testButton.Text = "測試連線";
                RefreshActionState();
            }
        }
    }

    private void SwitchConfigurationClicked(object? sender, EventArgs e)
    {
        if (providerSelector?.SelectedItem is not ProviderDefinition provider ||
            modelSelector?.SelectedItem is not ModelDefinition model)
        {
            return;
        }

        if (CodexConfigManager.IsCodexRunning())
        {
            MessageBox.Show(
                this,
                "Codex 目前仍在執行。請先保存工作並自行關閉 Codex，再回來執行切換；程式不會強制關閉 Codex。",
                "請先關閉 Codex",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmThirdPartyDataFlow(provider))
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"即將把 Codex 模型設定切換為：\n\n供應商：{provider.DisplayName}\n模型：{model.DisplayName}\n\n切換前會自動備份。確定繼續嗎？",
                "確認安全切換",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
            var result = configManager.ApplySelection(provider, model, executablePath);
            footerStatus!.Text = $"已安全切換為 {result.ProviderDisplayName}／{result.ModelDisplayName}。請重新開啟 Codex。";
            LoadCurrentConfig();
            MessageBox.Show(this, "設定已完成並重新驗證。現在可以重新開啟 Codex。", "切換完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "切換失敗，原設定已保留", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadCurrentConfig();
        }
    }

    private bool ConfirmThirdPartyDataFlow(ProviderDefinition provider)
    {
        if (provider.Id == "openai")
        {
            return true;
        }

        var noticePath = Path.Combine(
            Path.GetDirectoryName(configManager.CustomProvidersPath)!,
            "data-flow-notice-accepted.txt");
        if (File.Exists(noticePath))
        {
            return true;
        }

        if (MessageBox.Show(
                this,
                "第一次啟用第三方供應商前的提醒：\n\n切換之後，Codex 為了完成您交付的任務，會把您輸入的提示與相關專案內容片段，傳送給目前選擇的模型供應商處理。\n\n請不要讓第三方模型處理未經授權的個資或機密資料。切換器本身不會讀取或上傳您的專案內容。\n\n瞭解並同意後才會繼續切換。要繼續嗎？",
                "資料流提醒",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(noticePath)!);
            File.WriteAllText(noticePath, $"已於 {DateTime.Now:yyyy-MM-dd HH:mm} 顯示並同意資料流提醒。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 記錄失敗只代表下次會再提醒一次，不影響本次切換。
        }

        return true;
    }

    private void RestoreOriginalClicked(object? sender, EventArgs e)
    {
        if (CodexConfigManager.IsCodexRunning())
        {
            MessageBox.Show(this, "請先保存工作並自行關閉 Codex，再執行恢復。", "請先關閉 Codex", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var backupTime = configManager.OriginalBackupTime?.ToString("yyyy/MM/dd HH:mm") ?? "未知時間";
        if (MessageBox.Show(
                this,
                $"將恢復首次切換前的原始 Codex 設定（備份時間：{backupTime}）。\n目前設定也會先備份。確定繼續嗎？",
                "恢復原始設定",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            configManager.RestoreOriginal();
            LoadCurrentConfig();
            MessageBox.Show(this, "原始 Codex 設定已恢復並重新驗證。", "恢復完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, "恢復失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadCurrentConfig();
        }
    }

    private static Button MakeButton(string text, bool primary = false)
    {
        var button = MakeDisabledButton(text, primary);
        button.Enabled = true;
        button.TabStop = true;
        return button;
    }

    private static Button MakeCompactButton(string text, Color foreColor)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = foreColor,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            UseVisualStyleBackColor = false,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(8, 2, 8, 2)
        };
    }

    private static Button MakeDisabledButton(string text, bool primary = false)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Enabled = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(225, 231, 248) : Color.White,
            ForeColor = primary ? Accent : Ink,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            UseVisualStyleBackColor = false,
            TabStop = false
        };
    }
}

internal sealed class ApiKeyDialog : Form
{
    private readonly TextBox keyInput;

    public ApiKeyDialog(string providerName, bool alreadySaved)
    {
        Text = $"{providerName} API Key";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(510, alreadySaved ? 285 : 245);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = alreadySaved ? 5 : 4,
            Padding = new Padding(22),
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (alreadySaved)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(new Label
        {
            Text = alreadySaved
                ? "已保存金鑰。如要更換，請輸入新金鑰；原金鑰不會顯示。"
                : "請輸入 API Key。金鑰只會保存於 Windows 認證管理員。",
            AutoSize = true,
            // 限制寬度讓長句自動換行，避免固定視窗把文字裁掉。
            MaximumSize = new Size(466, 0),
            ForeColor = Color.FromArgb(24, 34, 53)
        });
        keyInput = new TextBox
        {
            Dock = DockStyle.Top,
            UseSystemPasswordChar = true,
            Margin = new Padding(0, 12, 0, 12),
            AccessibleName = "API Key"
        };
        layout.Controls.Add(keyInput);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var saveButton = new Button { Text = alreadySaved ? "更換金鑰" : "安全保存", AutoSize = true };
        saveButton.Click += (_, _) => SaveRequested();
        var cancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        if (alreadySaved)
        {
            var deleteButton = new Button { Text = "刪除金鑰", AutoSize = true };
            deleteButton.Click += (_, _) =>
            {
                if (MessageBox.Show(this, "確定要刪除已保存的 API Key 嗎？", "刪除 API Key", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    DeleteRequested = true;
                    DialogResult = DialogResult.OK;
                }
            };
            buttons.Controls.Add(deleteButton);
        }
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        CancelButton = cancelButton;
        AcceptButton = saveButton;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        PerformAutoScale();
    }

    public string ApiKey => keyInput.Text;

    public bool DeleteRequested { get; private set; }

    private void SaveRequested()
    {
        if (string.IsNullOrWhiteSpace(keyInput.Text))
        {
            MessageBox.Show(this, "請先輸入 API Key。", "尚未輸入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            keyInput.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
    }
}

internal sealed class CustomProviderDialog : Form
{
    private static readonly Color Ink = Color.FromArgb(24, 34, 53);
    private static readonly Color Muted = Color.FromArgb(91, 103, 122);

    private readonly ConnectionTester connectionTester;
    private readonly string customProvidersPath;
    private readonly CancellationToken cancellationToken;
    private readonly TextBox nameInput;
    private readonly TextBox baseUrlInput;
    private readonly TextBox modelIdInput;
    private readonly TextBox apiKeyInput;
    private readonly NumericUpDown contextInput;
    private readonly CheckBox imagesInput;
    private readonly CheckedListBox effortInput;
    private readonly Label statusLabel;
    private readonly Button verifyButton;
    private readonly Button cancelButton;
    private bool verifying;

    public CustomProviderDialog(
        ConnectionTester connectionTester,
        string customProvidersPath,
        CancellationToken cancellationToken)
    {
        this.connectionTester = connectionTester;
        this.customProvidersPath = customProvidersPath;
        this.cancellationToken = cancellationToken;

        Text = "新增自訂供應商";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 566);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.White;
        FormClosing += (_, e) => e.Cancel = verifying;

        nameInput = new TextBox { Dock = DockStyle.Fill, AccessibleName = "顯示名稱" };
        baseUrlInput = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "https://api.example.com/v1",
            AccessibleName = "Base URL"
        };
        modelIdInput = new TextBox { Dock = DockStyle.Fill, AccessibleName = "模型 ID" };
        apiKeyInput = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            AccessibleName = "API Key"
        };
        contextInput = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1_000,
            Maximum = 100_000_000,
            Increment = 1_000,
            Value = 200_000,
            ThousandsSeparator = true,
            AccessibleName = "上下文長度"
        };
        imagesInput = new CheckBox
        {
            Text = "支援圖片輸入",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        effortInput = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            Height = 100,
            AccessibleName = "可用推理等級"
        };
        effortInput.Items.AddRange(["minimal", "low", "medium", "high", "xhigh"]);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(22)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var row = 0; row < 10; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var intro = new Label
        {
            Text = "只支援明確標示為 OpenAI Responses API 相容的供應商。按「驗證並加入」會以此金鑰送出兩次極小測試（文字回應與工具呼叫），任一測試失敗都不會保存。",
            AutoSize = true,
            MaximumSize = new Size(494, 0),
            ForeColor = Muted,
            Margin = new Padding(0, 0, 0, 12)
        };
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 2);

        AddField(layout, 1, "顯示名稱", nameInput);
        AddField(layout, 2, "Base URL", baseUrlInput);
        AddField(layout, 3, "模型 ID", modelIdInput);
        AddField(layout, 4, "API Key", apiKeyInput);
        AddField(layout, 5, "上下文長度", contextInput);
        layout.Controls.Add(imagesInput, 1, 6);
        AddField(layout, 7, "推理等級", effortInput);

        statusLabel = new Label
        {
            Text = "尚未開始驗證。",
            AutoSize = true,
            MaximumSize = new Size(494, 0),
            ForeColor = Muted,
            Margin = new Padding(0, 14, 0, 0)
        };
        layout.Controls.Add(statusLabel, 0, 8);
        layout.SetColumnSpan(statusLabel, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 14, 0, 0)
        };
        verifyButton = new Button { Text = "驗證並加入", AutoSize = true };
        verifyButton.Click += VerifyClicked;
        cancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(verifyButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 9);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        CancelButton = cancelButton;
        AcceptButton = verifyButton;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        PerformAutoScale();
    }

    public ProviderDefinition? SavedProvider { get; private set; }

    private static void AddField(TableLayoutPanel layout, int row, string title, Control control)
    {
        layout.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Ink,
            Margin = new Padding(0, 10, 8, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        }, 0, row);
        control.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(control, 1, row);
    }

    private async void VerifyClicked(object? sender, EventArgs e)
    {
        if (verifying)
        {
            return;
        }

        var apiKey = apiKeyInput.Text.Trim();
        var provider = BuildProvider();
        if (provider is null || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                this,
                "請確認顯示名稱、Base URL（必須是 https 開頭的完整網址）、模型 ID 與 API Key 均已正確填寫。",
                "資料不完整",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"即將向 {provider.DisplayName} 送出兩次極小測試（文字回應＋工具呼叫），可能產生極少量 API 費用。確定繼續嗎？",
                "確認驗證",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        verifying = true;
        SetBusy(true);
        try
        {
            statusLabel.Text = "正在執行文字回應測試…";
            var textResult = await connectionTester.TestAsync(provider, provider.Models[0], apiKey, cancellationToken);
            if (IsDisposed)
            {
                return;
            }

            if (!textResult.Succeeded)
            {
                ShowFailure("文字回應測試未通過", textResult);
                return;
            }

            statusLabel.Text = "正在執行工具呼叫測試…";
            var toolResult = await connectionTester.TestToolCallAsync(provider, provider.Models[0], apiKey, cancellationToken);
            if (IsDisposed)
            {
                return;
            }

            if (!toolResult.Succeeded)
            {
                ShowFailure("工具呼叫測試未通過", toolResult);
                return;
            }

            CredentialVault.Save(provider.Id, apiKey);
            try
            {
                ProviderCatalog.SaveCustomProvider(customProvidersPath, provider);
            }
            catch (Exception)
            {
                try
                {
                    CredentialVault.Delete(provider.Id);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 保存失敗時的金鑰清理失敗不掩蓋原始錯誤。
                }

                throw;
            }

            SavedProvider = provider;
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            statusLabel.Text = "驗證已停止。";
            MessageBox.Show(this, exception.Message, "無法加入自訂供應商", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            verifying = false;
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private void ShowFailure(string stage, ConnectionTestResult result)
    {
        statusLabel.Text = $"{stage}，供應商與金鑰皆未保存。";
        MessageBox.Show(
            this,
            $"{result.UserMessage}\n\n診斷摘要：{result.DiagnosticSummary}\n\n供應商與 API Key 皆未保存。",
            result.Title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private ProviderDefinition? BuildProvider()
    {
        var modelId = modelIdInput.Text.Trim();
        var provider = new ProviderDefinition
        {
            Id = ProviderCatalog.CustomProviderIdPrefix + Guid.NewGuid().ToString("N")[..8],
            DisplayName = nameInput.Text.Trim(),
            Enabled = true,
            BaseUrl = baseUrlInput.Text.Trim(),
            RequiresApiKey = true,
            Protocol = "responses",
            Models =
            [
                new ModelDefinition
                {
                    Id = modelId,
                    DisplayName = modelId,
                    ContextWindow = (int)contextInput.Value,
                    SupportsImages = imagesInput.Checked,
                    ReasoningEfforts = effortInput.CheckedItems.Cast<string>().ToList()
                }
            ]
        };
        return ProviderCatalog.IsValidCustomProvider(provider) ? provider : null;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        verifyButton.Enabled = !busy;
        cancelButton.Enabled = !busy;
        nameInput.Enabled = !busy;
        baseUrlInput.Enabled = !busy;
        modelIdInput.Enabled = !busy;
        apiKeyInput.Enabled = !busy;
        contextInput.Enabled = !busy;
        imagesInput.Enabled = !busy;
        effortInput.Enabled = !busy;
    }
}
