using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LectureAgent.Desktop;

/// <summary>
/// The app window: a status header, the agent dashboard hosted in WebView2 (the dashboard
/// key is injected so nobody has to paste it), and a tray icon that keeps the app available
/// after the window is closed.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color HeaderColor = Color.FromArgb(20, 20, 19);
    private static readonly Color CardColor = Color.FromArgb(28, 28, 26);
    private static readonly Color BorderColor = Color.FromArgb(40, 40, 37);
    private static readonly Color TextColor = Color.FromArgb(236, 232, 225);
    private static readonly Color MutedTextColor = Color.FromArgb(142, 139, 133);
    private static readonly Color CreamAccent = Color.FromArgb(237, 234, 229);
    private static readonly Color DarkAccent = Color.FromArgb(20, 20, 19);
    private static readonly Color RunningColor = Color.FromArgb(52, 211, 153);
    private static readonly Color WarningColor = Color.FromArgb(251, 191, 36);
    private static readonly Color StoppedColor = Color.FromArgb(248, 113, 113);

    private readonly AgentApiClient _api = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 3000 };
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayStatusItem = new("Checking…") { Enabled = false };
    private readonly ToolStripMenuItem _trayPauseItem = new("Pause uploads");
    private readonly Label _statusDot = new();
    private readonly Label _statusLabel = new();
    private readonly Label _detailLabel = new();
    private readonly Button _pauseButton;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 19) };
    private readonly TableLayoutPanel _offlinePanel;
    private readonly Label _offlineTitle = new();
    private readonly Label _offlineDetail = new();
    private readonly Button _startAgentButton = new()
    {
        Text = "Start agent →",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = CreamAccent,
        ForeColor = DarkAccent,
        Padding = new Padding(16, 8, 16, 8),
        Margin = new Padding(0, 0, 10, 0),
        Cursor = Cursors.Hand
    };
    private readonly Button _installServiceButton = new()
    {
        Text = "Install background service →",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = CreamAccent,
        ForeColor = DarkAccent,
        Padding = new Padding(16, 8, 16, 8),
        Margin = new Padding(0, 0, 10, 0),
        Cursor = Cursors.Hand,
        Visible = false
    };
    private readonly Button _setupWizardButton = new()
    {
        Text = "Run setup wizard",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(35, 35, 32),
        ForeColor = TextColor,
        Padding = new Padding(14, 8, 14, 8),
        Margin = new Padding(0, 0, 10, 0),
        Cursor = Cursors.Hand,
        Visible = false
    };

    private AgentSettings _settings = AgentSettings.Load();
    private string _injectedScriptId = string.Empty;
    private string _phoneUrl = string.Empty;
    private string _recordingsFolder = string.Empty;
    private bool _polling;
    private bool _exiting;
    private bool _balloonShown;
    private bool _webReady;
    private bool _dashboardVisible;

    internal MainForm()
    {
        Text = "Centrix";
        Icon = LoadAppIcon();
        MinimumSize = new Size(940, 620);
        Size = new Size(1240, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 20, 19);
        Font = new Font("Segoe UI", 9F);

        _pauseButton = CreateHeaderButton("Pause uploads");
        _offlinePanel = BuildOfflinePanel();

        Controls.Add(BuildRootLayout());

        BuildTrayIcon();

        _pollTimer.Tick += async (_, _) => await PollAsync();
        Shown += async (_, _) => await StartUpAsync();
    }

    private TableLayoutPanel BuildRootLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        root.Controls.Add(BuildHeader(), 0, 0);

        _content.Controls.Add(_offlinePanel);
        _content.Controls.Add(_web);
        _web.Visible = false;
        root.Controls.Add(_content, 0, 1);

        return root;
    }

    private TableLayoutPanel BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = HeaderColor,
            Padding = new Padding(18, 0, 12, 0),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = HeaderColor,
            Padding = new Padding(0, 10, 0, 8),
            Margin = Padding.Empty
        };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var logoImage = AppPaths.LoadLogoImage();
        if (logoImage != null)
        {
            var logoBox = new PictureBox
            {
                Size = new Size(42, 42),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = logoImage,
                Margin = new Padding(0, 2, 12, 0)
            };
            info.Controls.Add(logoBox, 0, 0);
        }

        var textStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = HeaderColor,
            Margin = Padding.Empty
        };
        textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var topRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = HeaderColor,
            WrapContents = false,
            Margin = Padding.Empty
        };

        var brandLabel = new Label
        {
            Text = "Centrix",
            AutoSize = true,
            Font = new Font("Georgia", 13F, FontStyle.Regular),
            ForeColor = TextColor,
            Margin = new Padding(0, 0, 8, 0)
        };

        _statusDot.AutoSize = true;
        _statusDot.Text = "●";
        _statusDot.Font = new Font("Segoe UI", 10F);
        _statusDot.ForeColor = WarningColor;
        _statusDot.Margin = new Padding(0, 2, 4, 0);

        _statusLabel.AutoSize = true;
        _statusLabel.Text = "Checking…";
        _statusLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        _statusLabel.ForeColor = Color.White;
        _statusLabel.Margin = new Padding(0, 1, 0, 0);

        topRow.Controls.Add(brandLabel);
        topRow.Controls.Add(_statusDot);
        topRow.Controls.Add(_statusLabel);

        _detailLabel.AutoSize = true;
        _detailLabel.Text = "Connecting to the agent on this PC…";
        _detailLabel.Font = new Font("Segoe UI", 8.5F);
        _detailLabel.ForeColor = MutedTextColor;
        _detailLabel.Margin = new Padding(0, 2, 0, 0);

        textStack.Controls.Add(topRow, 0, 0);
        textStack.Controls.Add(_detailLabel, 0, 1);
        info.Controls.Add(textStack, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = HeaderColor,
            Anchor = AnchorStyles.None,
            WrapContents = false
        };

        var copyLink = CreateHeaderButton("Copy phone link");
        copyLink.Click += (_, _) => CopyPhoneLink();

        _pauseButton.Click += async (_, _) => await TogglePauseAsync();

        var restart = CreateHeaderButton("Restart agent");
        restart.Click += async (_, _) => await RestartAgentAsync();

        var settings = CreateHeaderButton("Settings");
        settings.Click += (_, _) => OpenSettings();

        var browser = CreateHeaderButton("Open in browser");
        browser.Click += (_, _) => OpenInBrowser(_settings.LocalBaseUrl + "/");

        buttons.Controls.Add(copyLink);
        buttons.Controls.Add(_pauseButton);
        buttons.Controls.Add(restart);
        buttons.Controls.Add(settings);
        buttons.Controls.Add(browser);

        header.Controls.Add(info, 0, 0);
        header.Controls.Add(buttons, 1, 0);
        return header;
    }

    private TableLayoutPanel BuildOfflinePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(20, 20, 19)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        var card = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Anchor = AnchorStyles.None,
            BackColor = CardColor,
            Padding = new Padding(36, 32, 36, 32),
            Width = 640
        };

        var logoImage = AppPaths.LoadLogoImage();
        if (logoImage != null)
        {
            var logoBox = new PictureBox
            {
                Size = new Size(54, 54),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = logoImage,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 14)
            };
            card.Controls.Add(logoBox);
        }

        _offlineTitle.AutoSize = true;
        _offlineTitle.Text = "Connecting to Centrix…";
        _offlineTitle.Font = new Font("Georgia", 18F, FontStyle.Regular);
        _offlineTitle.ForeColor = TextColor;
        _offlineTitle.Margin = new Padding(0, 0, 0, 8);

        _offlineDetail.AutoSize = true;
        _offlineDetail.MaximumSize = new Size(560, 0);
        _offlineDetail.Text = "This takes a few seconds after the PC starts.";
        _offlineDetail.Font = new Font("Segoe UI", 9.5F);
        _offlineDetail.ForeColor = MutedTextColor;
        _offlineDetail.Margin = new Padding(0, 0, 0, 24);

        var badges = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 24),
            WrapContents = false
        };

        string[] highlights = { "🛡️ 24x7 Silent Service", "📂 Offline-Safe Buffer", "☁️ Google Drive Sync" };
        foreach (var h in highlights)
        {
            var badge = new Label
            {
                Text = h,
                AutoSize = true,
                BackColor = Color.FromArgb(35, 35, 32),
                ForeColor = TextColor,
                Font = new Font("Segoe UI", 8.25F),
                Padding = new Padding(10, 5, 10, 5),
                Margin = new Padding(0, 0, 8, 0)
            };
            badges.Controls.Add(badge);
        }

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };

        _installServiceButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _installServiceButton.FlatAppearance.BorderSize = 0;
        _installServiceButton.Click += async (_, _) => await InstallServiceAsync();

        _setupWizardButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _setupWizardButton.FlatAppearance.BorderSize = 0;
        _setupWizardButton.Click += async (_, _) =>
        {
            RunFirstRunSetup();
            await PollAsync();
        };

        _startAgentButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _startAgentButton.FlatAppearance.BorderSize = 0;
        _startAgentButton.Click += async (_, _) => await StartAgentAsync();

        var logs = new Button
        {
            Text = "Open logs folder",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 35, 32),
            ForeColor = TextColor,
            Padding = new Padding(14, 8, 14, 8),
            Cursor = Cursors.Hand
        };
        logs.FlatAppearance.BorderSize = 0;
        logs.Click += (_, _) => OpenFolder(AppPaths.LogDirectory);

        var settingsBtn = new Button
        {
            Text = "Settings",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 35, 32),
            ForeColor = TextColor,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        settingsBtn.FlatAppearance.BorderSize = 0;
        settingsBtn.Click += (_, _) => OpenSettings();

        actions.Controls.Add(_installServiceButton);
        actions.Controls.Add(_setupWizardButton);
        actions.Controls.Add(_startAgentButton);
        actions.Controls.Add(settingsBtn);
        actions.Controls.Add(logs);

        card.Controls.Add(_offlineTitle);
        card.Controls.Add(_offlineDetail);
        card.Controls.Add(badges);
        card.Controls.Add(actions);

        panel.Controls.Add(card, 0, 1);
        return panel;
    }

    private void BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("Open Centrix", null, (_, _) => ShowMainWindow())
        {
            Font = new Font(menu.Font, FontStyle.Bold)
        };

        _trayPauseItem.Click += async (_, _) => await TogglePauseAsync();

        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayStatusItem);
        menu.Items.Add(_trayPauseItem);
        menu.Items.Add(new ToolStripMenuItem("Rescan recordings folder", null, async (_, _) => await RescanAsync()));
        menu.Items.Add(new ToolStripMenuItem("Restart agent", null, async (_, _) => await RestartAgentAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open dashboard in browser", null, (_, _) => OpenInBrowser(_settings.LocalBaseUrl + "/")));
        menu.Items.Add(new ToolStripMenuItem("Copy phone / tablet link", null, (_, _) => CopyPhoneLink()));
        menu.Items.Add(new ToolStripMenuItem("Open recordings folder", null, (_, _) => OpenFolder(RecordingsFolder())));
        menu.Items.Add(new ToolStripMenuItem("Open logs folder", null, (_, _) => OpenFolder(AppPaths.LogDirectory)));
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit app (uploads keep running)", null, (_, _) => QuitApp()));

        _tray.Icon = LoadAppIcon();
        _tray.Text = "Centrix";
        _tray.Visible = true;
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private async Task StartUpAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            RunFirstRunSetup();
        }

        if (AgentServiceControl.GetState() == AgentServiceState.Stopped)
        {
            await StartAgentAsync();
        }

        await InitializeWebViewAsync();
        await PollAsync();
        _pollTimer.Start();
    }

    /// <summary>
    /// First launch after install (no settings file / no key yet): the graphical setup
    /// wizard collects every choice — folder, center, room, Google Drive, background
    /// service — so nothing has to be edited by hand.
    /// </summary>
    private void RunFirstRunSetup()
    {
        using var wizard = new SetupWizardForm(_settings);
        if (wizard.ShowDialog(this) != DialogResult.OK)
        {
            // Cancelled: the window below explains what is not working and offers the
            // setup again through Settings.
            _settings = AgentSettings.Load();
            return;
        }

        _settings = AgentSettings.Load();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.WebViewUserData);
            var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewUserData);
            await _web.EnsureCoreWebView2Async(environment);

            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += OnNewWindowRequested;

            await ApplyDashboardKeyAsync();
            _webReady = true;
            _web.Source = new Uri(_settings.LocalBaseUrl + "/");
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException)
        {
            _webReady = false;
            ShowOffline(
                "Dashboard viewer is missing",
                "The Microsoft Edge WebView2 runtime is needed to show the dashboard inside this app. "
                + "Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and reopen Centrix. "
                + "Until then, use \"Open in browser\".");
        }
    }

    /// <summary>
    /// Pre-seeds the dashboard's own storage with the agent key so the app never shows the
    /// login screen. Re-applied whenever the key changes in Settings.
    /// </summary>
    private async Task ApplyDashboardKeyAsync()
    {
        if (_web.CoreWebView2 is null)
        {
            return;
        }

        var script = "(function(){try{localStorage.setItem('lasrs.apiKey',"
            + JsonSerializer.Serialize(_settings.ApiKey)
            + ");localStorage.removeItem('lasrs.agentUrl');}catch(e){}})();";

        if (_injectedScriptId.Length > 0)
        {
            _web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_injectedScriptId);
        }

        _injectedScriptId = await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            Apply(await _api.ProbeAsync(_settings));
        }
        finally
        {
            _polling = false;
        }
    }

    private void Apply(AgentProbe probe)
    {
        if (probe.Snapshot is { } snapshot)
        {
            _phoneUrl = snapshot.LanAddresses.Count > 0
                ? $"http://{snapshot.LanAddresses[0]}:{_settings.Port}/"
                : string.Empty;
            _recordingsFolder = snapshot.MonitorFolder;

            var status = snapshot.UploadsPaused ? "Uploads paused" : "Running";
            SetStatus(status, snapshot.UploadsPaused ? WarningColor : RunningColor);

            var details = new List<string>();
            if (snapshot.RoomId.Length > 0)
            {
                details.Add($"Room {snapshot.RoomId}");
            }

            if (snapshot.CenterId.Length > 0)
            {
                details.Add(snapshot.CenterId);
            }

            details.Add($"{snapshot.Uploading} uploading");
            details.Add($"{snapshot.Pending} waiting");
            details.Add($"{snapshot.Failed} failed");
            details.Add($"{snapshot.Uploaded} uploaded");
            details.Add($"up {FormatUptime(snapshot.Uptime)}");
            if (_phoneUrl.Length > 0)
            {
                details.Add($"phone {_phoneUrl}");
            }

            _detailLabel.Text = string.Join("   ·   ", details);
            _pauseButton.Text = snapshot.UploadsPaused ? "Resume uploads" : "Pause uploads";
            _trayPauseItem.Text = snapshot.UploadsPaused ? "Resume uploads" : "Pause uploads";
            _trayStatusItem.Text = $"{status} · Room {snapshot.RoomId}";
            _tray.Text = Truncate($"Centrix — {status}", 63);

            ShowDashboard();
            return;
        }

        if (probe.Unauthorized)
        {
            SetStatus("Key mismatch", StoppedColor);
            _detailLabel.Text = "The agent rejected this app's dashboard key.";
            _trayStatusItem.Text = "Key mismatch";
            _tray.Text = "Centrix — key mismatch";
            ShowOffline(
                "Dashboard key does not match",
                "The agent is running but rejected the key this app is using. Open Settings and save the "
                + "key again (or generate a new one) to reconnect.");
            return;
        }

        switch (AgentServiceControl.GetState())
        {
            case AgentServiceState.NotInstalled:
                SetStatus("Not installed", StoppedColor);
                _detailLabel.Text = "The Centrix background service is missing.";
                _trayStatusItem.Text = "Service not installed";
                _tray.Text = "Centrix — service not installed";
                _installServiceButton.Visible = true;
                _setupWizardButton.Visible = true;
                _startAgentButton.Visible = false;
                ShowOffline(
                    "Background service is not installed",
                    "The service that watches recordings and uploads to Google Drive is not installed on this PC. "
                    + "Click below to install it, or run the setup wizard to configure your room and Google Drive.");
                break;

            case AgentServiceState.Running:
            case AgentServiceState.Starting:
                SetStatus("Starting…", WarningColor);
                _detailLabel.Text = "The agent is starting up.";
                _trayStatusItem.Text = "Starting…";
                _tray.Text = "Centrix — starting";
                _installServiceButton.Visible = false;
                _setupWizardButton.Visible = false;
                _startAgentButton.Visible = false;
                ShowOffline("Starting the agent…", "This takes a few seconds after the PC starts.");
                break;

            default:
                SetStatus("Stopped", StoppedColor);
                _detailLabel.Text = "Nothing is being watched or uploaded right now.";
                _trayStatusItem.Text = "Stopped";
                _tray.Text = "Centrix — stopped";
                _installServiceButton.Visible = false;
                _setupWizardButton.Visible = false;
                _startAgentButton.Visible = true;
                ShowOffline(
                    "Agent is not running",
                    "Recordings are not being detected or uploaded. Start the agent to resume.");
                break;
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusDot.ForeColor = color;
    }

    private void ShowDashboard()
    {
        if (!_webReady || _dashboardVisible)
        {
            return;
        }

        _dashboardVisible = true;
        _offlinePanel.Visible = false;
        _web.Visible = true;
        _web.BringToFront();

        if (_web.Source is null)
        {
            _web.Source = new Uri(_settings.LocalBaseUrl + "/");
        }
        else
        {
            _web.CoreWebView2?.Reload();
        }
    }

    private void ShowOffline(string title, string detail)
    {
        _offlineTitle.Text = title;
        _offlineDetail.Text = detail;
        _dashboardVisible = false;
        _web.Visible = false;
        _offlinePanel.Visible = true;
        _offlinePanel.BringToFront();
    }

    private async Task InstallServiceAsync()
    {
        SetStatus("Installing…", WarningColor);
        try
        {
            await Task.Run(() => AgentServiceControl.Install(_settings.Port));
            MessageBox.Show(this, "Centrix background service has been installed and started successfully.",
                "Centrix", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not install the background service:\n\n{ex.Message}",
                "Centrix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        await PollAsync();
    }

    private async Task StartAgentAsync()
    {
        SetStatus("Starting…", WarningColor);
        try
        {
            await Task.Run(AgentServiceControl.Start);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Could not start the agent service.\n\n{ex.Message}", "Centrix",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        await PollAsync();
    }

    private async Task RestartAgentAsync()
    {
        SetStatus("Restarting…", WarningColor);
        try
        {
            await Task.Run(AgentServiceControl.Restart);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Could not restart the agent service.\n\n{ex.Message}", "Centrix",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _dashboardVisible = false;
        await PollAsync();
    }

    private async Task TogglePauseAsync()
    {
        var probe = await _api.ProbeAsync(_settings);
        if (probe.Snapshot is null)
        {
            return;
        }

        var path = probe.Snapshot.UploadsPaused ? "/api/control/uploads/resume" : "/api/control/uploads/pause";
        await _api.PostAsync(_settings, path);
        await PollAsync();
    }

    private async Task RescanAsync()
    {
        var accepted = await _api.PostAsync(_settings, "/api/control/monitoring/rescan");
        _tray.ShowBalloonTip(
            3000,
            "Centrix",
            accepted ? "Rescanning the recordings folder." : "Could not reach the agent to rescan.",
            accepted ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings = AgentSettings.Load();
        _ = ApplySettingsChangeAsync();
    }

    private async Task ApplySettingsChangeAsync()
    {
        await ApplyDashboardKeyAsync();
        await RestartAgentAsync();
    }

    private void CopyPhoneLink()
    {
        if (_phoneUrl.Length == 0)
        {
            MessageBox.Show(this, "The agent has not reported a network address yet. Try again once it is running.",
                "Centrix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Clipboard.SetText(_phoneUrl);
        _tray.ShowBalloonTip(3000, "Centrix", $"Copied {_phoneUrl}", ToolTipIcon.Info);
    }

    private string RecordingsFolder() =>
        _recordingsFolder.Length > 0 ? _recordingsFolder : _settings.MonitorFolder;

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Google sign-in and external links belong in the real browser, not a bare popup.
        e.Handled = true;
        OpenInBrowser(e.Uri);
    }

    private void ShowMainWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
    }

    private void QuitApp()
    {
        _exiting = true;
        Close();
    }

    protected override void WndProc(ref Message m)
    {
        if (Program.ShowWindowMessage != 0 && (uint)m.Msg == Program.ShowWindowMessage)
        {
            ShowMainWindow();
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();

            if (!_balloonShown)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(4000, "Centrix",
                    "Uploads keep running in the background. Double-click this icon to open the app again.",
                    ToolTipIcon.Info);
            }

            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _api.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Button CreateHeaderButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 35, 32),
            ForeColor = TextColor,
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(6, 0, 0, 0),
            TabStop = false,
            Font = new Font("Segoe UI", 8.5F),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 46);
        return button;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            if (File.Exists(AppPaths.AppIcon))
            {
                return new Icon(AppPaths.AppIcon);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Falls back to the generic application icon below.
        }

        return SystemIcons.Application;
    }

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    private void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this, $"Folder not found:\n{path}", "Centrix",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        }

        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }

        return uptime.TotalMinutes >= 1 ? $"{uptime.Minutes}m" : $"{uptime.Seconds}s";
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
