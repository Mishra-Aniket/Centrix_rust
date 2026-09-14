using System.Diagnostics;

namespace LectureAgent.Desktop;

/// <summary>
/// Graphical editor for everything the local operator is allowed to change: which folder
/// is watched, the center/room identity, the dashboard key and port, and the Google Drive
/// connection. Saving rewrites the shared settings file and the caller restarts the agent.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color HeaderColor = Color.FromArgb(27, 16, 51);
    private static readonly Color AccentColor = Color.FromArgb(134, 59, 255);
    private static readonly Color MutedTextColor = Color.FromArgb(185, 174, 220);
    private static readonly Color FieldBorder = Color.FromArgb(210, 204, 232);
    private static readonly Color LabelColor = Color.FromArgb(51, 41, 82);

    private readonly AgentSettings _settings;

    private readonly TextBox _folderBox = new();
    private readonly TextBox _centerBox = new();
    private readonly TextBox _roomBox = new();
    private readonly NumericUpDown _portBox = new() { Minimum = 1024, Maximum = 65535, Width = 120 };
    private readonly TextBox _keyBox = new() { ReadOnly = true };
    private readonly CheckBox _driveEnabled = new() { Text = "Upload to Google Drive", AutoSize = true };
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
        ClientSize = new Size(620, 640);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;

        _folderBox.Text = DefaultIfBlank(settings.MonitorFolder, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        _centerBox.Text = settings.CenterId;
        _roomBox.Text = settings.RoomId;
        _portBox.Value = Math.Clamp(settings.Port, 1024, 65535);
        _keyBox.Text = settings.ApiKey.Length > 0 ? settings.ApiKey : AgentSettings.GenerateApiKey();
        _driveEnabled.Checked = settings.GoogleDriveEnabled;
        _rootFolderBox.Text = DefaultIfBlank(settings.GoogleDriveRootFolder, "LectureRecordings");
        _credentialsBox.Text = settings.GoogleDriveCredentialsPath;

        Controls.Add(BuildRoot());
        AcceptButton = MakeButton("Save", SaveAndClose, accent: true);
        CancelButton = MakeButton("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        Controls.Add(BuildButtonRow());
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
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var scroll = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        scroll.Controls.Add(BuildBody());
        root.Controls.Add(scroll, 0, 0);
        return root;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 12)
        };

        body.Controls.Add(MakeHeading("This PC"));
        body.Controls.Add(MakeRow("Recordings folder", FolderPicker()));
        body.Controls.Add(MakeNote("Videos and notes are detected here, including subfolders. Only files from the last 24 hours are picked up."));
        body.Controls.Add(MakeRow("Center name", _centerBox));
        body.Controls.Add(MakeRow("Room ID", _roomBox));

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

        return body;
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
        _signInButton.BackColor = AccentColor;
        _signInButton.ForeColor = Color.White;
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
        _settings.MonitorFolder = _folderBox.Text.Trim();
        _settings.CenterId = _centerBox.Text.Trim();
        _settings.RoomId = _roomBox.Text.Trim();
        _settings.Port = (int)_portBox.Value;
        _settings.ApiKey = _keyBox.Text;
        _settings.GoogleDriveEnabled = _driveEnabled.Checked;
        _settings.GoogleDriveRootFolder = _rootFolderBox.Text.Trim();

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
            BackColor = Color.FromArgb(246, 244, 251)
        };
        row.Controls.Add(cancel);
        row.Controls.Add(save);
        return row;
    }

    private static Label MakeHeading(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 11F, FontStyle.Bold),
        ForeColor = HeaderColor,
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
        ForeColor = Color.FromArgb(120, 113, 145),
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
            Padding = new Padding(14, 7, 14, 7),
            Margin = new Padding(6, 0, 0, 0),
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 0;
        if (accent)
        {
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(108, 42, 214);
        }
        else
        {
            button.BackColor = Color.FromArgb(238, 235, 248);
            button.ForeColor = LabelColor;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(228, 222, 246);
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    private static string DefaultIfBlank(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
