using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace LectureAgent.Desktop;

/// <summary>
/// The graphical first-run setup shown when the app is opened on a PC that has not been
/// configured yet (no shared settings file / no API key). Replaces the old text console
/// wizard: every choice — recordings folder, center, room, Google Drive, background
/// service — is made with buttons and folder pickers, and finished in one click.
/// </summary>
internal sealed class SetupWizardForm : Form
{
    private static readonly Color HeaderColor = Color.FromArgb(27, 16, 51);
    private static readonly Color AccentColor = Color.FromArgb(134, 59, 255);
    private static readonly Color MutedTextColor = Color.FromArgb(185, 174, 220);
    private static readonly Color LabelColor = Color.FromArgb(51, 41, 82);

    private readonly AgentSettings _settings;
    private readonly Button _backButton = new() { Text = "< Back", AutoSize = true };
    private readonly Button _nextButton = new() { Text = "Next >", AutoSize = true };
    private readonly Label _stepLabel = new();
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly Panel _stepHost = new() { Dock = DockStyle.Fill, BackColor = Color.White };

    private readonly TextBox _folderBox = new();
    private readonly TextBox _centerBox = new();
    private readonly TextBox _roomBox = new();
    private readonly Label _deviceIdPreview = new();
    private readonly CheckBox _driveEnabled = new() { Text = "Upload recordings to Google Drive", AutoSize = true, Checked = true };
    private readonly TextBox _credentialsBox = new();
    private readonly TextBox _rootFolderBox = new();
    private readonly CheckBox _installService = new()
    {
        Text = "Install the background service so uploads run 24x7 (recommended)",
        AutoSize = true,
        Checked = true
    };
    private readonly CheckBox _openDashboard = new() { Text = "Open the dashboard when finished", AutoSize = true, Checked = true };

    private int _step;
    private int _lastStep = 4;

    internal bool OpenDashboardWhenDone => _openDashboard.Checked;

    internal SetupWizardForm(AgentSettings settings)
    {
        _settings = settings;

        Text = "Centrix Setup";
        Icon = AppPaths.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 560);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;

        _folderBox.Text = DefaultIfBlank(settings.MonitorFolder,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Recordings"));
        _centerBox.Text = DefaultIfBlank(settings.CenterId, "Pune - PCMC Vidyapeeth");
        _roomBox.Text = settings.RoomId;
        _credentialsBox.Text = settings.GoogleDriveCredentialsPath;
        _rootFolderBox.Text = DefaultIfBlank(settings.GoogleDriveRootFolder, "LectureRecordings");
        if (string.IsNullOrWhiteSpace(_roomBox.Text) == false)
        {
            UpdateDevicePreview(this, EventArgs.Empty);
        }

        _centerBox.TextChanged += UpdateDevicePreview;
        _roomBox.TextChanged += UpdateDevicePreview;

        Controls.Add(BuildRoot());
        ShowStep();
    }

    private Control BuildRoot()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(_stepHost, 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        return root;
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = HeaderColor };

        var logoImage = AppPaths.LoadLogoImage();
        if (logoImage != null)
        {
            var logoBox = new PictureBox
            {
                Location = new Point(22, 18),
                Size = new Size(54, 54),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = logoImage
            };
            header.Controls.Add(logoBox);
            _titleLabel.Location = new Point(88, 18);
            _subtitleLabel.Location = new Point(90, 50);
        }
        else
        {
            _titleLabel.Location = new Point(28, 18);
            _subtitleLabel.Location = new Point(30, 50);
        }

        _titleLabel.AutoSize = true;
        _titleLabel.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        _titleLabel.ForeColor = Color.White;

        _subtitleLabel.AutoSize = true;
        _subtitleLabel.Font = new Font("Segoe UI", 9F);
        _subtitleLabel.ForeColor = MutedTextColor;

        var stepPill = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(ClientSize.Width - 145, 28),
            Size = new Size(115, 32),
            BackColor = Color.FromArgb(48, 33, 84),
            Padding = new Padding(6, 4, 6, 4)
        };
        _stepLabel.Dock = DockStyle.Fill;
        _stepLabel.TextAlign = ContentAlignment.MiddleCenter;
        _stepLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _stepLabel.ForeColor = Color.FromArgb(220, 210, 245);
        stepPill.Controls.Add(_stepLabel);

        header.Controls.AddRange(new Control[] { _titleLabel, _subtitleLabel, stepPill });
        return header;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(246, 244, 251),
            Padding = new Padding(24, 10, 24, 10),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        foreach (var button in new[] { _backButton, _nextButton })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Padding = new Padding(16, 8, 16, 8);
            button.BackColor = Color.FromArgb(238, 235, 248);
            button.ForeColor = LabelColor;
            button.UseVisualStyleBackColor = false;
            button.Height = 38;
            button.Click += OnNavigate;
        }

        _nextButton.BackColor = AccentColor;
        _nextButton.ForeColor = Color.White;

        var cancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(16, 8, 16, 8),
            BackColor = Color.FromArgb(238, 235, 248),
            ForeColor = LabelColor,
            UseVisualStyleBackColor = false
        };
        cancel.FlatAppearance.BorderSize = 0;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var leftPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = Padding.Empty
        };
        leftPanel.Controls.Add(_backButton);

        var rightPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = Padding.Empty
        };
        rightPanel.Controls.Add(cancel);
        rightPanel.Controls.Add(_nextButton);

        footer.Controls.Add(leftPanel, 0, 0);
        footer.Controls.Add(rightPanel, 1, 0);
        return footer;
    }

    private void OnNavigate(object? sender, EventArgs e)
    {
        if (sender == _nextButton)
        {
            if (!ValidateStep())
            {
                return;
            }

            if (_step == _lastStep)
            {
                Finish();
                return;
            }

            _step++;
        }
        else
        {
            _step = Math.Max(0, _step - 1);
        }

        ShowStep();
    }

    private bool ValidateStep()
    {
        if (_step == 1)
        {
            var folder = _folderBox.Text.Trim();
            if (folder.Length == 0)
            {
                Warn("Pick the folder where class recordings and notes are saved.");
                return false;
            }

            if (!Directory.Exists(folder)
                && MessageBox.Show(this,
                    $"The folder does not exist yet:\n\n{folder}\n\nCreate it now?",
                    "Centrix Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Directory.CreateDirectory(folder);
            }
        }

        if (_step == 2 && (_centerBox.Text.Trim().Length == 0 || _roomBox.Text.Trim().Length == 0))
        {
            Warn("Enter both the center name and the room ID.");
            return false;
        }

        return true;
    }

    private void ShowStep()
    {
        (_titleLabel.Text, _subtitleLabel.Text, var content, var nextText) = _step switch
        {
            0 => ("Welcome to Centrix",
                "This small app turns this PC into an automatic lecture uploader.",
                BuildWelcomeStep(), "Get started"),
            1 => ("Where do class recordings appear?",
                "Pick the folder the recording software or teacher copies files into.",
                BuildFolderStep(), "Next >"),
            2 => ("Which room is this PC in?",
                "Lectures are labelled with this center and room before uploading.",
                BuildRoomStep(), "Next >"),
            3 => ("Connect the center's Google account",
                "One-time connection so recordings land in the center's Google Drive folders.",
                BuildDriveStep(), "Next >"),
            _ => ("Ready to set up",
                "Check the summary below — you can change anything later in Settings.",
                BuildSummaryStep(), "Set up & start")
        };

        _stepLabel.Text = $"Step {_step + 1} of {_lastStep + 1}";
        _backButton.Visible = _step > 0;
        _nextButton.Text = nextText;

        _stepHost.Controls.Clear();
        content.Dock = DockStyle.Fill;
        _stepHost.Controls.Add(content);
    }

    private Control BuildWelcomeStep()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(28, 20, 28, 16),
            AutoSize = true
        };

        var intro = new Label
        {
            Text = "Centrix automatically captures, organizes, and uploads classroom lectures:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = LabelColor,
            Margin = new Padding(0, 0, 0, 12)
        };

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            BackColor = Color.FromArgb(248, 246, 253),
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 14)
        };

        string[] features =
        {
            "✔  Watches classroom recording folders automatically (including all subfolders)",
            "✔  Offline-first resilience: recordings buffer safely locally during poor/no internet",
            "✔  Auto-syncs lectures to the center's Google Drive folders once internet restores",
            "✔  Live local dashboard accessible via classroom PC, phone, or tablet on local WiFi",
            "✔  Runs 24x7 as a silent Windows background service (no manual login required)"
        };

        foreach (var feat in features)
        {
            card.Controls.Add(new Label
            {
                Text = feat,
                AutoSize = true,
                Font = new Font("Segoe UI", 9.25F),
                ForeColor = Color.FromArgb(40, 32, 64),
                Margin = new Padding(0, 3, 0, 3)
            });
        }

        var note = MakeNote("Setup takes about 1 minute. Nothing is uploaded until setup completes.");

        panel.Controls.Add(intro);
        panel.Controls.Add(card);
        panel.Controls.Add(note);
        return panel;
    }

    private Control BuildFolderStep()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(28, 24, 28, 16) };

        panel.Controls.Add(FieldLabel("Recordings folder"));
        _folderBox.Dock = DockStyle.Fill;
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the folder where class recordings (video) and notes (PDF) are saved. All subfolders are included.",
                ShowNewFolderButton = true
            };
            if (Directory.Exists(_folderBox.Text))
            {
                dialog.SelectedPath = _folderBox.Text;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _folderBox.Text = dialog.SelectedPath;
            }
        };

        var row = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(_folderBox, 0, 0);
        row.Controls.Add(browse, 1, 0);
        panel.Controls.Add(row);

        panel.Controls.Add(MakeNote(
            "All subfolders are watched automatically. Videos (.mp4, .mkv, …) and notes (.pdf, .pptx) are picked up; " +
            "only files saved in the last 24 hours are treated as new."));
        return panel;
    }

    private Control BuildRoomStep()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(28, 24, 28, 16) };

        panel.Controls.Add(FieldLabel("Center name (exactly as in the tracker)"));
        _centerBox.Dock = DockStyle.Top;
        panel.Controls.Add(_centerBox);

        panel.Controls.Add(FieldLabel("Room ID"));
        _roomBox.Dock = DockStyle.Top;
        panel.Controls.Add(_roomBox);

        _deviceIdPreview.AutoSize = true;
        _deviceIdPreview.ForeColor = Color.FromArgb(120, 113, 145);
        _deviceIdPreview.Font = new Font("Segoe UI", 8.25F);
        _deviceIdPreview.Margin = new Padding(1, 6, 0, 0);
        panel.Controls.Add(_deviceIdPreview);

        panel.Controls.Add(MakeNote("New rooms (604, 501, …) use the same setup — just type a different room ID."));
        return panel;
    }

    private Control BuildDriveStep()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(28, 24, 28, 16) };

        _driveEnabled.Margin = new Padding(0, 0, 0, 10);
        panel.Controls.Add(_driveEnabled);

        // Root folder name
        var rootLabel = FieldLabel("Google Drive folder name (where batch folders are)");
        rootLabel.EnabledChanged += (_, _) => rootLabel.ForeColor = rootLabel.Enabled ? LabelColor : Color.Gray;
        panel.Controls.Add(rootLabel);
        _rootFolderBox.Dock = DockStyle.Top;
        panel.Controls.Add(_rootFolderBox);
        panel.Controls.Add(MakeNote(
            "Type the exact name of the existing folder on Google Drive that contains your batch folders " +
            "(e.g. \"Center-Pune\" or \"LectureRecordings\"). The agent will search inside this folder — it will NOT create new folders."));

        // Credentials file
        var caption = FieldLabel("Google credentials file (OAuth desktop JSON)");
        caption.EnabledChanged += (_, _) => caption.ForeColor = caption.Enabled ? LabelColor : Color.Gray;
        panel.Controls.Add(caption);

        _credentialsBox.Dock = DockStyle.Fill;
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Google credentials (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _credentialsBox.Text = dialog.FileName;
            }
        };

        var row = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(_credentialsBox, 0, 0);
        row.Controls.Add(browse, 1, 0);
        panel.Controls.Add(row);

        var signInButton = new Button
        {
            Text = "Sign in to Google now…",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.White,
            Padding = new Padding(14, 7, 14, 7),
            Margin = new Padding(0, 8, 10, 0)
        };
        signInButton.FlatAppearance.BorderSize = 0;
        var signInStatus = new Label
        {
            AutoSize = true,
            ForeColor = MutedTextColor,
            Font = new Font("Segoe UI", 8.5F),
            Margin = new Padding(0, 14, 0, 0)
        };

        signInButton.Click += async (_, _) =>
        {
            if (_credentialsBox.Text.Trim().Length > 0)
            {
                var destination = Path.Combine(AppPaths.SharedRoot, "config", "google_credentials.json");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(_credentialsBox.Text.Trim(), destination, overwrite: true);
                _settings.GoogleDriveCredentialsPath = destination;
            }
            if (_rootFolderBox.Text.Trim().Length > 0)
            {
                _settings.GoogleDriveRootFolder = _rootFolderBox.Text.Trim();
            }
            _settings.GoogleDriveEnabled = _driveEnabled.Checked;
            _settings.Save();

            if (!File.Exists(AppPaths.AgentExecutable))
            {
                MessageBox.Show(this, "The agent program was not found, so sign-in cannot start yet.",
                    "Centrix Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            signInButton.Enabled = false;
            signInStatus.Text = "Opening browser for Google sign-in…";
            try
            {
                var exitCode = await Task.Run(() =>
                {
                    var startInfo = new ProcessStartInfo(AppPaths.AgentExecutable, "--authorize-google")
                    {
                        UseShellExecute = true
                    };
                    using var process = Process.Start(startInfo);
                    process?.WaitForExit();
                    return process?.ExitCode ?? -1;
                });

                signInStatus.Text = exitCode == 0
                    ? "Signed in successfully!"
                    : "Sign-in did not complete. Check credentials and retry.";
            }
            catch
            {
                signInStatus.Text = "Could not launch sign-in helper.";
            }
            finally
            {
                signInButton.Enabled = true;
            }
        };

        var signInRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        signInRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        signInRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        signInRow.Controls.Add(signInButton, 0, 0);
        signInRow.Controls.Add(signInStatus, 1, 0);
        panel.Controls.Add(signInRow);

        SyncDriveControls(this, EventArgs.Empty);
        _driveEnabled.CheckedChanged += SyncDriveControls;

        panel.Controls.Add(MakeNote(
            "Leave credentials empty if already installed on this PC. You can also sign in or change this anytime from Settings."));
        return panel;
    }

    private void SyncDriveControls(object? sender, EventArgs e)
    {
        _credentialsBox.Enabled = _driveEnabled.Checked;
        _rootFolderBox.Enabled = _driveEnabled.Checked;
    }

    private Control BuildSummaryStep()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(28, 20, 28, 16) };

        var card = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Color.FromArgb(248, 246, 253),
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 14)
        };

        var summary = new Label
        {
            Text = BuildSummaryText(),
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = LabelColor,
            Font = new Font("Consolas", 9.25F),
            Margin = Padding.Empty
        };
        card.Controls.Add(summary);
        panel.Controls.Add(card);

        var serviceMissing = AgentServiceControl.GetState() == AgentServiceState.NotInstalled;
        _installService.Visible = serviceMissing;
        if (!serviceMissing)
        {
            panel.Controls.Add(MakeNote("The background service is already installed on this PC."));
        }

        _installService.Margin = new Padding(0, 0, 0, 8);
        panel.Controls.Add(_installService);
        panel.Controls.Add(_openDashboard);
        return panel;
    }

    private string BuildSummaryText() => new StringBuilder()
        .AppendLine($"Recordings folder :  {_folderBox.Text.Trim()}")
        .AppendLine($"Center            :  {_centerBox.Text.Trim()}")
        .AppendLine($"Room              :  {_roomBox.Text.Trim()}")
        .AppendLine($"Google Drive      :  {(_driveEnabled.Checked ? "enabled" + (_credentialsBox.Text.Trim().Length > 0 ? " (new credentials file)" : string.Empty) : "disabled — files queue locally only")}")
        .AppendLine($"Drive folder      :  {(_driveEnabled.Checked && _rootFolderBox.Text.Trim().Length > 0 ? _rootFolderBox.Text.Trim() : "—")}")
        .AppendLine($"Dashboard         :  http://localhost:{_settings.Port}/  +  phone link on the same WiFi")
        .ToString();

    private void UpdateDevicePreview(object? sender, EventArgs e)
    {
        var room = _roomBox.Text.Trim();
        _deviceIdPreview.Text = room.Length > 0
            ? $"This PC will be identified as “PC-ROOM-{room.ToUpperInvariant()}”"
            : string.Empty;
    }

    private void Finish()
    {
        var apiKey = _settings.ApiKey.Length > 0 ? _settings.ApiKey : AgentSettings.GenerateApiKey();
        var room = _roomBox.Text.Trim().ToUpperInvariant();

        try
        {
            _settings.MonitorFolder = _folderBox.Text.Trim();
            _settings.CenterId = _centerBox.Text.Trim();
            _settings.RoomId = _roomBox.Text.Trim();
            _settings.DeviceId = room.Length > 0 ? $"PC-ROOM-{room}" : _settings.DeviceId;
            _settings.ApiKey = apiKey;
            _settings.GoogleDriveEnabled = _driveEnabled.Checked;
            if (_driveEnabled.Checked && _rootFolderBox.Text.Trim().Length > 0)
            {
                _settings.GoogleDriveRootFolder = _rootFolderBox.Text.Trim();
            }

            if (_driveEnabled.Checked && _credentialsBox.Text.Trim().Length > 0)
            {
                var destination = Path.Combine(AppPaths.SharedRoot, "config", "google_credentials.json");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(_credentialsBox.Text.Trim(), destination, overwrite: true);
                _settings.GoogleDriveCredentialsPath = destination;
            }

            _settings.Save();
            WriteAccessCard(apiKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save the setup:\n\n{ex.Message}", "Centrix Setup",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (_installService.Visible && _installService.Checked
            && AgentServiceControl.GetState() == AgentServiceState.NotInstalled)
        {
            try
            {
                AgentServiceControl.Install(_settings.Port);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // The user declined the UAC prompt, or the policy blocked elevation: the app
                // still works for this session and the checkbox stays available next time.
                MessageBox.Show(this,
                    "The background service could not be installed:\n\n" + ex.Message
                    + "\n\nUploads will only run while this app is open. Re-run setup to try again.",
                    "Centrix Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>The one-page "how do I connect my phone" card, next to the settings file.</summary>
    private void WriteAccessCard(string apiKey)
    {
        var ip = LanIPv4() ?? "<this-pc-ip>";
        var card = $"""
            DASHBOARD ACCESS
            ================

            Dashboard (on this PC):   http://localhost:{_settings.Port}/
            Phone / tablet (same WiFi): http://{ip}:{_settings.Port}/

            API key (paste once on the dashboard login screen):
            {apiKey}

            Settings chosen during setup:
              Recordings folder : {_folderBox.Text.Trim()}
              Center            : {_centerBox.Text.Trim()}
              Room              : {_roomBox.Text.Trim()}

            Keep this file private — the key gives full control of the agent.
            """;
        File.WriteAllText(Path.Combine(AppPaths.SharedRoot, "DASHBOARD-ACCESS.txt"), card);
    }

    private static string? LanIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address)
                    && !address.ToString().StartsWith("169.254."))
                .Select(address => address.ToString())
                .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Centrix Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = LabelColor,
        Margin = new Padding(0, 8, 0, 3)
    };

    private static Label MakeNote(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.FromArgb(120, 113, 145),
        Font = new Font("Segoe UI", 8.25F),
        MaximumSize = new Size(580, 0),
        Margin = new Padding(1, 8, 0, 6)
    };

    private static string DefaultIfBlank(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
