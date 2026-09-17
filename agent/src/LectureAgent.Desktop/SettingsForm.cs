using System.Diagnostics;

namespace LectureAgent.Desktop;

/// <summary>
/// Graphical editor for everything the local operator is allowed to change: which folder
/// is watched, the center/room identity, the dashboard key and port, and the Google Drive
/// connection. Saving rewrites the shared settings file and the caller restarts the agent.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color HeaderColor = Color.FromArgb(20, 20, 19);
    private static readonly Color CardColor = Color.FromArgb(28, 28, 26);
    private static readonly Color BorderColor = Color.FromArgb(40, 40, 37);
    private static readonly Color TextColor = Color.FromArgb(236, 232, 225);
    private static readonly Color MutedTextColor = Color.FromArgb(142, 139, 133);
    private static readonly Color CreamAccent = Color.FromArgb(237, 234, 229);
    private static readonly Color DarkAccent = Color.FromArgb(20, 20, 19);
    private static readonly Color FieldBorder = Color.FromArgb(50, 50, 46);
    private static readonly Color LabelColor = Color.FromArgb(236, 232, 225);

    private readonly AgentSettings _settings;

    private readonly TextBox _folderBox = new();
    private readonly TextBox _notesFolderBox = new();
    private readonly TextBox _centerBox = new();
    private readonly TextBox _roomBox = new();
    private readonly NumericUpDown _portBox = new() { Minimum = 1024, Maximum = 65535, Width = 120 };
    private readonly TextBox _keyBox = new() { ReadOnly = true };
    private readonly CheckBox _driveEnabled = new() { Text = "Upload to Google Drive", AutoSize = true };
    private readonly CheckBox _youtubeEnabled = new() { Text = "Enable YouTube upload", AutoSize = true };
    private readonly CheckBox _youtubeAutoPublish = new() { Text = "Auto-publish to YouTube after Drive upload", AutoSize = true };
    private readonly TextBox _rootFolderBox = new();
    private readonly TextBox _credentialsBox = new();
    private readonly Button _signInButton = new() { Text = "Sign in to Google now…", AutoSize = true };
    private readonly Label _signInStatus = new();

    internal SettingsForm(AgentSettings settings)
    {
        _settings = settings;

        Text = "Centrix — Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 720);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(20, 20, 19);
        ForeColor = TextColor;

        foreach (var tb in new[] { _folderBox, _notesFolderBox, _centerBox, _roomBox, _keyBox, _rootFolderBox, _credentialsBox })
        {
            tb.BackColor = Color.FromArgb(28, 28, 26);
            tb.ForeColor = TextColor;
            tb.BorderStyle = BorderStyle.FixedSingle;
        }
        _portBox.BackColor = Color.FromArgb(28, 28, 26);
        _portBox.ForeColor = TextColor;
        _driveEnabled.ForeColor = TextColor;
        _youtubeEnabled.ForeColor = TextColor;
        _youtubeAutoPublish.ForeColor = TextColor;

        _folderBox.Text = DefaultIfBlank(settings.MonitorFolder, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        _notesFolderBox.Text = settings.NotesMonitorFolder;
        _centerBox.Text = settings.CenterId;
        _roomBox.Text = settings.RoomId;
        _portBox.Value = Math.Clamp(settings.Port, 1024, 65535);
        _keyBox.Text = settings.ApiKey.Length > 0 ? settings.ApiKey : AgentSettings.GenerateApiKey();
        _driveEnabled.Checked = settings.GoogleDriveEnabled;
        _youtubeEnabled.Checked = settings.YouTubeEnabled;
        _youtubeAutoPublish.Checked = settings.YouTubeAutoPublish;
        _youtubeAutoPublish.Enabled = settings.YouTubeEnabled;
        _youtubeEnabled.CheckedChanged += (_, _) => _youtubeAutoPublish.Enabled = _youtubeEnabled.Checked;
        _rootFolderBox.Text = DefaultIfBlank(settings.GoogleDriveRootFolder, "");
        _credentialsBox.Text = settings.GoogleDriveCredentialsPath;

        AcceptButton = MakeButton("Save", SaveAndClose, accent: true);
        CancelButton = MakeButton("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        Controls.Add(BuildRoot());
    }

    private Control BuildRoot()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

        var scroll = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        scroll.Controls.Add(BuildBody());
        root.Controls.Add(scroll, 0, 0);
        root.Controls.Add(BuildButtonRow(), 0, 1);
        return root;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(24, 18, 24, 20)
        };

        body.Controls.Add(MakeHeading("This PC"));
        body.Controls.Add(MakeRow("Recordings folder", FolderPicker()));
        body.Controls.Add(MakeNote("Videos and notes are detected here, including subfolders. Only files from the last 24 hours are picked up."));
        body.Controls.Add(MakeRow("Notes / PDF folder (optional)", NotesFolderPicker()));
        body.Controls.Add(MakeNote("Leave blank to keep notes in the recordings folder. Any folder on any drive works — type or browse a full path (e.g. D:\\Notes or \\\\NAS\\share)."));
        body.Controls.Add(MakeRow("Center name", _centerBox));
        body.Controls.Add(MakeRow("Room ID", _roomBox));

        body.Controls.Add(MakeHeading("Background Service"));
        body.Controls.Add(ServiceControlRow());
        body.Controls.Add(MakeNote("The background service keeps watching and uploading even when this window is closed."));

        body.Controls.Add(MakeHeading("Dashboard"));
        body.Controls.Add(MakeRow("API key", KeyRow()));
        body.Controls.Add(MakeNote("Paste this key once on the phone/tablet login screen. Regenerating it signs every device out."));
        body.Controls.Add(MakeRow("Port", _portBox));

        body.Controls.Add(MakeHeading("Google Drive"));
        body.Controls.Add(_driveEnabled);
        body.Controls.Add(MakeRow("Root folder name", _rootFolderBox));
        body.Controls.Add(MakeNote("The folder on Google Drive where batch folders exist (e.g. Center-Pune). The agent searches inside this folder."));
        body.Controls.Add(MakeRow("Credentials file", CredentialsPicker()));
        body.Controls.Add(MakeNote("The Google OAuth desktop JSON issued for the center's account."));
        body.Controls.Add(MakeRow(string.Empty, SignInRow()));

        body.Controls.Add(MakeHeading("YouTube (Optional)"));
        body.Controls.Add(_youtubeEnabled);
        body.Controls.Add(_youtubeAutoPublish);
        body.Controls.Add(MakeNote("Optionally upload lectures to YouTube after processing. Keep disabled if YouTube upload is not needed."));

        return body;
    }

    private Control ServiceControlRow()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var statusLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            Margin = new Padding(0, 8, 10, 8)
        };

        var actionButton = new Button
        {
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(0, 4, 0, 4)
        };
        actionButton.FlatAppearance.BorderSize = 0;

        void UpdateServiceState()
        {
            var state = AgentServiceControl.GetState();
            switch (state)
            {
                case AgentServiceState.Running:
                    statusLabel.Text = "● Service is running.";
                    statusLabel.ForeColor = Color.FromArgb(16, 185, 129);
                    actionButton.Text = "Restart service";
                    actionButton.BackColor = Color.FromArgb(35, 35, 32);
                    actionButton.ForeColor = TextColor;
                    actionButton.Visible = true;
                    break;
                case AgentServiceState.Stopped:
                    statusLabel.Text = "● Service is stopped.";
                    statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
                    actionButton.Text = "Start service";
                    actionButton.BackColor = CreamAccent;
                    actionButton.ForeColor = DarkAccent;
                    actionButton.Visible = true;
                    break;
                case AgentServiceState.NotInstalled:
                    statusLabel.Text = "● Service is not installed on this PC.";
                    statusLabel.ForeColor = Color.FromArgb(239, 68, 68);
                    actionButton.Text = "Install service (elevated)…";
                    actionButton.BackColor = CreamAccent;
                    actionButton.ForeColor = DarkAccent;
                    actionButton.Visible = true;
                    break;
                default:
                    statusLabel.Text = $"● Service status: {state}";
                    statusLabel.ForeColor = Color.FromArgb(245, 158, 11);
                    actionButton.Visible = false;
                    break;
            }
        }

        actionButton.Click += async (_, _) =>
        {
            actionButton.Enabled = false;
            var state = AgentServiceControl.GetState();
            try
            {
                if (state == AgentServiceState.NotInstalled)
                {
                    ApplyValues();
                    _settings.Save();
                    await Task.Run(() => AgentServiceControl.Install((int)_portBox.Value));
                    MessageBox.Show(this, "Centrix background service has been installed and started.",
                        "Centrix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (state == AgentServiceState.Stopped)
                {
                    await Task.Run(AgentServiceControl.Start);
                }
                else
                {
                    await Task.Run(AgentServiceControl.Restart);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Service operation failed:\n\n{ex.Message}", "Centrix",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UpdateServiceState();
                actionButton.Enabled = true;
            }
        };

        UpdateServiceState();
        panel.Controls.Add(statusLabel, 0, 0);
        panel.Controls.Add(actionButton, 1, 0);
        return panel;
    }

    private Control FolderPicker()
    {
        _folderBox.Dock = DockStyle.Fill;
        var browse = MakeButton("Browse…", BrowseFolder);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_folderBox, 0, 0);
        panel.Controls.Add(browse, 1, 0);
        return panel;
    }

    private Control NotesFolderPicker()
    {
        _notesFolderBox.Dock = DockStyle.Fill;
        var browse = MakeButton("Browse…", () =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the folder where class notes (PDF, PPT) are saved, if they arrive in a different folder than the recordings.",
                ShowNewFolderButton = true
            };
            if (Directory.Exists(_notesFolderBox.Text))
            {
                dialog.SelectedPath = _notesFolderBox.Text;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _notesFolderBox.Text = dialog.SelectedPath;
            }
        });

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_notesFolderBox, 0, 0);
        panel.Controls.Add(browse, 1, 0);
        return panel;
    }

    private Control KeyRow()
    {
        var copy = MakeButton("Copy", () =>
        {
            if (_keyBox.Text.Length > 0)
            {
                Clipboard.SetText(_keyBox.Text);
            }
        });
        var regenerate = MakeButton("New key", () =>
        {
            if (MessageBox.Show(this,
                    "Generate a new key? Every phone and tablet will have to sign in again.",
                    "Centrix", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _keyBox.Text = AgentSettings.GenerateApiKey();
            }
        });
        _keyBox.Dock = DockStyle.Fill;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_keyBox, 0, 0);
        panel.Controls.Add(copy, 1, 0);
        panel.Controls.Add(regenerate, 2, 0);
        return panel;
    }

    private Control CredentialsPicker()
    {
        var browse = MakeButton("Browse…", () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Google credentials (*.json)|*.json|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _credentialsBox.Text = dialog.FileName;
            }
        });
        _credentialsBox.Dock = DockStyle.Fill;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_credentialsBox, 0, 0);
        panel.Controls.Add(browse, 1, 0);
        return panel;
    }

    private Control SignInRow()
    {
        _signInButton.FlatStyle = FlatStyle.Flat;
        _signInButton.BackColor = CreamAccent;
        _signInButton.ForeColor = DarkAccent;
        _signInButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _signInButton.FlatAppearance.BorderSize = 0;
        _signInButton.UseVisualStyleBackColor = false;
        _signInButton.Click += async (_, _) => await SignInAsync();

        _signInStatus.AutoSize = true;
        _signInStatus.ForeColor = MutedTextColor;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.Controls.Add(_signInButton, 0, 0);
        panel.Controls.Add(_signInStatus, 1, 0);
        return panel;
    }

    private async Task SignInAsync()
    {
        if (_driveEnabled.Checked)
        {
            ApplyValues();
            _settings.Save();
        }

        if (!File.Exists(AppPaths.AgentExecutable))
        {
            MessageBox.Show(this,
                "The agent program is not installed on this PC, so Google sign-in cannot run yet.",
                "Centrix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _signInButton.Enabled = false;
        _signInStatus.Text = "Waiting for Google sign-in to finish…";
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

            _signInStatus.Text = exitCode == 0
                ? "Signed in — uploads will use this Google account."
                : "Sign-in did not complete. Check the credentials file and try again.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _signInStatus.Text = "Could not start the sign-in helper.";
        }
        finally
        {
            _signInButton.Enabled = true;
        }
    }

    private void BrowseFolder()
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
    }

    private void ApplyValues()
    {
        // Quotes around a pasted path are never part of the folder name.
        _settings.MonitorFolder = _folderBox.Text.Trim().Trim('"', '\'').Trim();
        _settings.NotesMonitorFolder = _notesFolderBox.Text.Trim().Trim('"', '\'').Trim();
        _settings.CenterId = _centerBox.Text.Trim();
        _settings.RoomId = _roomBox.Text.Trim();
        _settings.Port = (int)_portBox.Value;
        _settings.ApiKey = _keyBox.Text;
        _settings.GoogleDriveEnabled = _driveEnabled.Checked;
        _settings.GoogleDriveRootFolder = _rootFolderBox.Text.Trim();
        _settings.YouTubeEnabled = _youtubeEnabled.Checked;
        _settings.YouTubeAutoPublish = _youtubeAutoPublish.Checked;

        if (_credentialsBox.Text.Trim().Length > 0)
        {
            _settings.GoogleDriveCredentialsPath = CopyCredentials(_credentialsBox.Text.Trim());
        }
    }

    /// <summary>
    /// The service runs as LocalSystem and cannot read the user's folders, so a picked
    /// credentials file is copied into the shared ProgramData config folder.
    /// </summary>
    private static string CopyCredentials(string source)
    {
        var destination = Path.Combine(AppPaths.SharedRoot, "config", "google_credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_folderBox.Text))
        {
            MessageBox.Show(this, "Choose the recordings folder first.", "Centrix",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_roomBox.Text))
        {
            MessageBox.Show(this, "Enter the room ID.", "Centrix",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplyValues();
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save the settings:\n\n{ex.Message}", "Centrix",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private Control BuildButtonRow()
    {
        var save = (Button)AcceptButton!;
        var cancel = (Button)CancelButton!;

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12, 10, 24, 14),
            BackColor = Color.FromArgb(28, 28, 26)
        };
        row.Controls.Add(cancel);
        row.Controls.Add(save);
        return row;
    }

    private static Label MakeHeading(string text) => new()
    {
        Text = text,
        Font = new Font("Georgia", 11.5F, FontStyle.Regular),
        ForeColor = TextColor,
        AutoSize = true,
        Margin = new Padding(0, 16, 0, 8)
    };

    private static Control MakeRow(string caption, Control field)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 2, 0, 2)
        };
        if (caption.Length > 0)
        {
            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor = LabelColor,
                Margin = new Padding(0, 6, 0, 3)
            };
            row.Controls.Add(label);
        }

        field.Dock = DockStyle.Fill;
        row.Controls.Add(field);
        return row;
    }

    private static Label MakeNote(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = MutedTextColor,
        Font = new Font("Segoe UI", 8.25F),
        MaximumSize = new Size(560, 0),
        Margin = new Padding(1, 2, 0, 6)
    };

    private Button MakeButton(string text, Action onClick, bool accent = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(16, 7, 16, 7),
            Margin = new Padding(6, 0, 0, 0),
            TabStop = true,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        if (accent)
        {
            button.BackColor = CreamAccent;
            button.ForeColor = DarkAccent;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.FlatAppearance.MouseOverBackColor = Color.White;
        }
        else
        {
            button.BackColor = Color.FromArgb(35, 35, 32);
            button.ForeColor = TextColor;
            button.Font = new Font("Segoe UI", 9F);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 46);
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    private static string DefaultIfBlank(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
