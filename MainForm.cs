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

    public MainForm()
    {
        Text = "Codex 多模型切換器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(940, 650);
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        Controls.Add(BuildPage());

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        PerformAutoScale();

        LoadProviderOptions();
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
            "MVP · 介面骨架",
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
        layout.Controls.Add(MakeLabel(
            "尚未讀取 Codex 設定",
            17F,
            FontStyle.Bold,
            Ink,
            new Padding(0, 8, 0, 0)), 0, 1);

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
        details.Controls.Add(MakeDetailValue("—"), 1, 0);
        details.Controls.Add(MakeDetailLabel("模型"), 0, 1);
        details.Controls.Add(MakeDetailValue("—"), 1, 1);
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
            "安全模式\n這一階段不會讀取或修改 Codex 設定。",
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
            RowCount = 9,
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
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(MakeEyebrow("準備切換"), 0, 0);
        layout.Controls.Add(MakeFieldLabel("供應商"), 0, 2);
        providerSelector = MakeSelector("供應商");
        providerSelector.SelectedIndexChanged += ProviderSelectionChanged;
        layout.Controls.Add(providerSelector, 0, 3);
        layout.Controls.Add(MakeFieldLabel("模型"), 0, 4);
        modelSelector = MakeSelector("模型");
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
        var testButton = MakeDisabledButton("測試連線");
        testButton.Margin = new Padding(6, 0, 0, 6);
        var restoreButton = MakeDisabledButton("恢復原始設定");
        restoreButton.Margin = new Padding(0, 6, 6, 0);
        var switchButton = MakeDisabledButton("切換並開啟 Codex", true);
        switchButton.Margin = new Padding(6, 6, 0, 0);

        actions.Controls.Add(keyButton, 0, 0);
        actions.Controls.Add(testButton, 1, 0);
        actions.Controls.Add(restoreButton, 0, 1);
        actions.Controls.Add(switchButton, 1, 1);
        layout.Controls.Add(actions, 0, 8);

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

    private void LoadProviderOptions()
    {
        if (providerSelector is null || modelSelector is null || footerStatus is null)
        {
            return;
        }

        var result = ProviderCatalog.Load();
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
        providerSelector.SelectedIndex = providerSelector.Items.Count > 0 ? 0 : -1;
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
        RefreshCredentialState(provider);
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

    private static Button MakeButton(string text)
    {
        var button = MakeDisabledButton(text);
        button.Enabled = true;
        button.TabStop = true;
        return button;
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
        ClientSize = new Size(510, alreadySaved ? 245 : 205);
        MinimumSize = new Size(510, ClientSize.Height);
        MaximumSize = new Size(680, ClientSize.Height);
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
