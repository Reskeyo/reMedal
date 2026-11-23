using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MedalShadowSync;

public sealed class MainForm : Form
{
    private static readonly Color BgDark = Color.FromArgb(15, 23, 42);        // #0F172A
    private static readonly Color CardBg = Color.FromArgb(30, 41, 59);        // #1E293B
    private static readonly Color InputBg = Color.FromArgb(15, 23, 42);       // #0F172A
    private static readonly Color LogBg = Color.FromArgb(11, 17, 32);         // #0B1120
    private static readonly Color BorderColor = Color.FromArgb(51, 65, 85);   // #334155
    private static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);// #F8FAFC
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);  // #94A3B8
    private static readonly Color BtnSlate = Color.FromArgb(51, 65, 85);      // #334155
    private static readonly Color BtnSlateHover = Color.FromArgb(71, 85, 105);
    private static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);   // #2563EB
    private static readonly Color AccentBlueHover = Color.FromArgb(59, 130, 246);
    private static readonly Color AccentAmber = Color.FromArgb(245, 158, 11); // #F59E0B
    private static readonly Color AccentRed = Color.FromArgb(239, 68, 68);    // #EF4444

    private static readonly (int Days, string Label)[] WindowOptions =
    [
        (3, "±3 Tage"),
        (7, "±7 Tage"),
        (14, "±14 Tage"),
        (30, "±30 Tage")
    ];

    private readonly AppConfig _config;
    private readonly MedalDbInjector _injector = new();
    private readonly ClipWatcher _watcher;
    private readonly bool _launchMinimized;
    private readonly ToolTip _toolTip = new();

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _trayToggleItem;
    private readonly Icon _appIcon;
    private readonly Icon _trayActiveIcon;
    private readonly Icon _trayPausedIcon;

    private readonly Label _statusLabel;
    private readonly Label _envLabel;
    private readonly TextBox _folderBox;
    private readonly CheckBox _subfoldersCheck;
    private readonly CheckBox _autostartCheck;
    private readonly CheckBox _minimizedCheck;
    private readonly Label _patchStatusLabel;
    private readonly Button _patchBtn;
    private readonly Button _restoreBtn;
    private readonly ComboBox _windowDaysCombo;
    private readonly CheckBox _onlyLastYearCheck;
    private readonly CheckBox _blockAdsCheck;
    private readonly Button _toggleButton;
    private readonly Button _backfillBtn;
    private readonly Button _updateBtn;
    private readonly TextBox _logBox;

    private bool _exiting;
    private bool _trayHintShown;
    private int _backfillRunning;

    public MainForm(AppConfig config, bool launchMinimized)
    {
        _config = config;
        _launchMinimized = launchMinimized || config.StartMinimized;
        _watcher = new ClipWatcher(_injector, AppendLog);

        _appIcon = LoadEmbeddedIcon("remedal.ico", paused: false);
        _trayActiveIcon = LoadEmbeddedIcon("remedal_tray.ico", paused: false);
        _trayPausedIcon = LoadEmbeddedIcon("remedal_tray.ico", paused: true);

        Text = $"reMedal v{AppUpdater.CurrentVersion}";
        Icon = _appIcon;
        BackColor = BgDark;
        ForeColor = TextPrimary;
        Size = new Size(900, 700);
        MinimumSize = new Size(840, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        // --- Tray icon ---
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Öffnen", null, (_, _) => ShowMainWindow());
        _trayToggleItem = new ToolStripMenuItem("Auto-Import pausieren", null, (_, _) => ToggleWatching());
        trayMenu.Items.Add(_trayToggleItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Beenden", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Text = "reMedal",
            Icon = _trayActiveIcon,
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        // --- Main layout ---
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = BgDark
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 1. Status banner card
        var statusContent = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            BackColor = CardBg
        };
        statusContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _statusLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 4)
        };
        _envLabel = new Label
        {
            AutoSize = true,
            ForeColor = TextMuted,
            Margin = new Padding(0)
        };
        statusContent.Controls.Add(_statusLabel, 0, 0);
        statusContent.Controls.Add(_envLabel, 0, 1);
        root.Controls.Add(CreateSimpleCard(statusContent), 0, 0);

        // 2. Watch folder card
        var folderLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            BackColor = CardBg
        };
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _folderBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 28,
            Text = _config.WatchFolder,
            BackColor = InputBg,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 8, 2)
        };
        _folderBox.Leave += (_, _) => ApplyFolderChange();

        var browseBtn = CreateButton("Durchsuchen...", BtnSlate, BtnSlateHover);
        browseBtn.Margin = new Padding(0, 0, 8, 0);
        browseBtn.Click += (_, _) => BrowseFolder();

        var detectSpBtn = CreateButton("ShadowPlay suchen", BtnSlate, BtnSlateHover);
        detectSpBtn.Margin = new Padding(0);
        _toolTip.SetToolTip(detectSpBtn, "Erkennt den NVIDIA ShadowPlay-Ordner automatisch aus der Windows-Registry");
        detectSpBtn.Click += (_, _) =>
        {
            var detected = MedalEnvironment.FindShadowPlayFolder();
            if (detected is not null)
            {
                _folderBox.Text = detected;
                ApplyFolderChange();
            }
            else
            {
                MessageBox.Show(this, "Kein NVIDIA ShadowPlay-Ordner in der Registry gefunden.", "reMedal", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        folderLayout.Controls.Add(_folderBox, 0, 0);
        folderLayout.Controls.Add(browseBtn, 1, 0);
        folderLayout.Controls.Add(detectSpBtn, 2, 0);
        root.Controls.Add(CreateCard("Überwachter ShadowPlay-Ordner (automatische Spiel-Erkennung aktiv)", folderLayout), 0, 1);

        // 3. Medal extensions card (Story Throwbacks + Ad-Blocker)
        var extStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            BackColor = CardBg
        };
        extStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        extStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var extRow1 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = CardBg,
            Margin = new Padding(0, 0, 0, 10)
        };
        _patchStatusLabel = new Label
        {
            AutoSize = true,
            MinimumSize = new Size(0, 32),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 16, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = TextPrimary
        };
        _patchBtn = CreateButton("Patch aktivieren", AccentBlue, AccentBlueHover);
        _patchBtn.Click += (_, _) => ApplyThrowbacksPatch();

        _restoreBtn = CreateButton("Original wiederherstellen", BtnSlate, BtnSlateHover);
        _restoreBtn.Margin = new Padding(0);
        _restoreBtn.Click += (_, _) => RestoreMedalOriginal();

        extRow1.Controls.Add(_patchStatusLabel);
        extRow1.Controls.Add(_patchBtn);
        extRow1.Controls.Add(_restoreBtn);

        var extRow2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = CardBg,
            Margin = new Padding(0)
        };
        var windowLbl = new Label
        {
            Text = "Rückblick-Zeitraum:",
            AutoSize = true,
            MinimumSize = new Size(0, 28),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 8, 0)
        };
        _windowDaysCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = InputBg,
            ForeColor = TextPrimary,
            Width = 115,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 24, 0)
        };
        foreach (var opt in WindowOptions)
            _windowDaysCombo.Items.Add(opt.Label);
        var selectedIdx = Array.FindIndex(WindowOptions, o => o.Days == _config.ThrowbackWindowDays);
        _windowDaysCombo.SelectedIndex = selectedIdx >= 0 ? selectedIdx : 0;
        _windowDaysCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_windowDaysCombo.SelectedIndex < 0) return;
            _config.ThrowbackWindowDays = WindowOptions[_windowDaysCombo.SelectedIndex].Days;
            _config.Save();
            AppendLog($"Rückblick-Zeitraum auf ±{_config.ThrowbackWindowDays} Tage gesetzt (zum Übernehmen in Medal bitte 'Erneut patchen' klicken).");
        };

        _blockAdsCheck = new CheckBox
        {
            Text = "Werbung in Medal ausblenden (über Patch)",
            AutoSize = true,
            MinimumSize = new Size(0, 28),
            ForeColor = TextPrimary,
            Checked = _config.BlockAdsEnabled,
            Margin = new Padding(0, 0, 18, 0)
        };
        _blockAdsCheck.CheckedChanged += (_, _) =>
        {
            _config.BlockAdsEnabled = _blockAdsCheck.Checked;
            _config.Save();
            AppendLog(_config.BlockAdsEnabled
                ? "Werbe-Blocker aktiviert (zum Übernehmen in Medal bitte 'Erneut patchen' klicken)."
                : "Werbe-Blocker deaktiviert (zum Übernehmen in Medal bitte 'Erneut patchen' klicken).");
        };

        _onlyLastYearCheck = new CheckBox
        {
            Text = "Nur letztes Jahr",
            AutoSize = true,
            MinimumSize = new Size(0, 28),
            ForeColor = TextPrimary,
            Checked = _config.ThrowbackOnlyLastYear,
            Margin = new Padding(0)
        };
        _onlyLastYearCheck.CheckedChanged += (_, _) =>
        {
            _config.ThrowbackOnlyLastYear = _onlyLastYearCheck.Checked;
            _config.Save();
            AppendLog(_config.ThrowbackOnlyLastYear
                ? "Rückblick auf letztes Jahr beschränkt (zum Übernehmen bitte 'Erneut patchen' klicken)."
                : "Rückblick zeigt alle vergangenen Jahre (zum Übernehmen bitte 'Erneut patchen' klicken).");
        };

        extRow2.Controls.Add(windowLbl);
        extRow2.Controls.Add(_windowDaysCombo);
        extRow2.Controls.Add(_blockAdsCheck);
        extRow2.Controls.Add(_onlyLastYearCheck);

        extStack.Controls.Add(extRow1, 0, 0);
        extStack.Controls.Add(extRow2, 0, 1);
        root.Controls.Add(CreateCard("Medal-Erweiterungen (Story-Rückblicke & Werbefreiheit)", extStack), 0, 2);

        // 4. Options row
        var optionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = BgDark,
            Margin = new Padding(0, 0, 0, 10)
        };

        _subfoldersCheck = CreateCheckBox("Unterordner überwachen", _config.IncludeSubdirectories);
        _subfoldersCheck.CheckedChanged += (_, _) =>
        {
            _config.IncludeSubdirectories = _subfoldersCheck.Checked;
            _config.Save();
            if (_watcher.IsRunning) _watcher.Start(_config);
        };

        _autostartCheck = CreateCheckBox("Mit Windows starten", MedalEnvironment.IsAutostartEnabled());
        _autostartCheck.CheckedChanged += (_, _) =>
        {
            try
            {
                MedalEnvironment.SetAutostart(_autostartCheck.Checked);
                AppendLog(_autostartCheck.Checked ? "Autostart aktiviert." : "Autostart deaktiviert.");
            }
            catch (Exception ex)
            {
                AppendLog($"Autostart-Fehler: {ex.Message}");
            }
        };

        _minimizedCheck = CreateCheckBox("Minimiert im Tray starten", _config.StartMinimized);
        _minimizedCheck.CheckedChanged += (_, _) =>
        {
            _config.StartMinimized = _minimizedCheck.Checked;
            _config.Save();
        };

        optionsFlow.Controls.Add(_subfoldersCheck);
        optionsFlow.Controls.Add(_autostartCheck);
        optionsFlow.Controls.Add(_minimizedCheck);
        root.Controls.Add(optionsFlow, 0, 3);

        // 5. Action buttons row
        var actionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = BgDark,
            Margin = new Padding(0, 0, 0, 10)
        };

        _toggleButton = CreateButton("Auto-Import pausieren", AccentBlue, AccentBlueHover);
        _toggleButton.Click += (_, _) => ToggleWatching();

        var importOneBtn = CreateButton("Einzelnen Clip importieren...", BtnSlate, BtnSlateHover);
        importOneBtn.Click += async (_, _) => await ImportSingleClipAsync();

        _backfillBtn = CreateButton("Letzten Monat nachimportieren", BtnSlate, BtnSlateHover);
        _backfillBtn.Click += async (_, _) => await RunBackfillAsync(isStartup: false);

        var backupsBtn = CreateButton("Backups öffnen", BtnSlate, BtnSlateHover);
        backupsBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppConfig.BackupsDir);
            Process.Start(new ProcessStartInfo("explorer.exe", AppConfig.BackupsDir) { UseShellExecute = true });
        };

        _updateBtn = CreateButton("Nach Updates suchen", BtnSlate, BtnSlateHover);
        _updateBtn.Margin = new Padding(0, 0, 0, 4);
        _updateBtn.Click += async (_, _) => await CheckForUpdatesAsync(silent: false);

        actionsFlow.Controls.Add(_toggleButton);
        actionsFlow.Controls.Add(importOneBtn);
        actionsFlow.Controls.Add(_backfillBtn);
        actionsFlow.Controls.Add(backupsBtn);
        actionsFlow.Controls.Add(_updateBtn);
        root.Controls.Add(actionsFlow, 0, 4);

        // 6. Log card
        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = LogBg,
            ForeColor = Color.FromArgb(226, 232, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
            Margin = new Padding(0)
        };
        root.Controls.Add(CreateCard("Aktivitätsprotokoll", _logBox, fillDock: true), 0, 5);

        Controls.Add(root);

        RefreshEnvironmentLabel();
        RefreshPatchStatus();
        StartWatching();
        _ = RunStartupUpdateCheckAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int useDark = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_launchMinimized)
        {
            Hide();
            WindowState = FormWindowState.Minimized;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _trayIcon.ShowBalloonTip(2500, "reMedal läuft weiter", "Die App bleibt im Hintergrund im System-Tray aktiv.", ToolTipIcon.Info);
            }
            return;
        }

        _watcher.Dispose();
        _toolTip.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
        _trayActiveIcon.Dispose();
        _trayPausedIcon.Dispose();
        base.OnFormClosing(e);
    }

    private static Panel CreateSimpleCard(Control content)
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBg,
            AutoSize = true,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 10)
        };
        p.Controls.Add(content);
        p.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, BorderColor, ButtonBorderStyle.Solid);
        return p;
    }

    private static Panel CreateCard(string title, Control content, bool fillDock = false)
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBg,
            AutoSize = !fillDock,
            Padding = new Padding(14, 10, 14, 12),
            Margin = new Padding(0, 0, 0, fillDock ? 0 : 10)
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = !fillDock,
            BackColor = CardBg
        };
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(fillDock ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(203, 213, 225),
            Margin = new Padding(0, 0, 0, 8)
        };
        content.Dock = DockStyle.Fill;

        inner.Controls.Add(header, 0, 0);
        inner.Controls.Add(content, 0, 1);
        p.Controls.Add(inner);
        p.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, BorderColor, ButtonBorderStyle.Solid);
        return p;
    }

    private static Button CreateButton(string text, Color bg, Color hover)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(0, 32),
            Anchor = AnchorStyles.Left,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = TextPrimary,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 4),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = BorderColor;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = hover;
        return btn;
    }

    private static CheckBox CreateCheckBox(string text, bool isChecked) => new()
    {
        Text = text,
        AutoSize = true,
        Checked = isChecked,
        ForeColor = TextPrimary,
        BackColor = BgDark,
        Margin = new Padding(0, 0, 18, 4)
    };

    private void RefreshEnvironmentLabel()
    {
        var db = MedalEnvironment.FindMedalDb(_config.MedalDbPath);
        var ff = MedalEnvironment.FindFfmpeg();
        var dbText = db is not null ? Path.GetFileName(db) : "NICHT GEFUNDEN";
        var ffText = ff is not null ? Path.GetFileName(ff) : "NICHT GEFUNDEN";
        _envLabel.Text = $"Medal DB: {dbText}   |   FFmpeg: {ffText}";
    }

    private void RefreshPatchStatus()
    {
        var status = MedalAsarPatcher.GetStatus();
        switch (status)
        {
            case PatchStatus.Patched:
                _patchStatusLabel.Text = "Status: Aktiv (V8 gepatcht)";
                _patchStatusLabel.ForeColor = Color.FromArgb(74, 222, 128);
                _patchBtn.Text = "Erneut patchen";
                _restoreBtn.Enabled = true;
                break;
            case PatchStatus.OutdatedPatch:
                _patchStatusLabel.Text = "Status: Veralteter Patch (Update nötig)";
                _patchStatusLabel.ForeColor = AccentAmber;
                _patchBtn.Text = "Patch auf V8 aktualisieren";
                _restoreBtn.Enabled = true;
                break;
            case PatchStatus.Unpatched:
                _patchStatusLabel.Text = _config.ThrowbacksEnabled
                    ? "Status: Medal-Update erkannt (neu patchen)"
                    : "Status: Original (nicht aktiv)";
                _patchStatusLabel.ForeColor = _config.ThrowbacksEnabled ? AccentAmber : TextMuted;
                _patchBtn.Text = "Patch in Medal aktivieren";
                _restoreBtn.Enabled = false;
                break;
            default:
                _patchStatusLabel.Text = "Status: Medal app.asar nicht gefunden";
                _patchStatusLabel.ForeColor = AccentRed;
                _patchBtn.Enabled = false;
                _restoreBtn.Enabled = false;
                break;
        }
    }

    private void ApplyThrowbacksPatch()
    {
        var medalWasRunning = MedalAsarPatcher.IsMedalRunning();
        if (medalWasRunning)
        {
            var choice = MessageBox.Show(
                this,
                "Medal läuft aktuell und sperrt app.asar.\nSoll Medal kurz beendet, gepatcht und direkt wieder gestartet werden?",
                "reMedal – Patch anwenden",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (choice != DialogResult.Yes)
                return;

            foreach (var p in Process.GetProcessesByName("Medal"))
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
            }
            Thread.Sleep(500);
        }

        try
        {
            var (ok, msg) = MedalAsarPatcher.ApplyPatch(null, _config.ThrowbackWindowDays, _config.BlockAdsEnabled, _config.ThrowbackOnlyLastYear);
            AppendLog(msg);
            if (ok)
            {
                _config.ThrowbacksEnabled = true;
                _config.Save();
                if (medalWasRunning)
                    MedalAsarPatcher.RestartMedal();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Patch-Fehler: {ex.Message}");
            MessageBox.Show(this, $"Fehler beim Patchen:\n{ex.Message}", "reMedal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshPatchStatus();
    }

    private void RestoreMedalOriginal()
    {
        var medalWasRunning = MedalAsarPatcher.IsMedalRunning();
        if (medalWasRunning)
        {
            var choice = MessageBox.Show(
                this,
                "Medal läuft aktuell. Soll Medal kurz beendet, wiederhergestellt und neu gestartet werden?",
                "reMedal – Original wiederherstellen",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (choice != DialogResult.Yes)
                return;

            foreach (var p in Process.GetProcessesByName("Medal"))
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
            }
            Thread.Sleep(500);
        }

        try
        {
            var (ok, msg) = MedalAsarPatcher.RestoreOriginal();
            AppendLog(msg);
            if (ok)
            {
                _config.ThrowbacksEnabled = false;
                _config.Save();
                if (medalWasRunning)
                    MedalAsarPatcher.RestartMedal();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Restore-Fehler: {ex.Message}");
        }

        RefreshPatchStatus();
    }

    private async Task RunBackfillAsync(bool isStartup)
    {
        if (Interlocked.CompareExchange(ref _backfillRunning, 1, 0) != 0)
            return;

        _backfillBtn.Enabled = false;
        try
        {
            var dbPath = MedalEnvironment.FindMedalDb(_config.MedalDbPath);
            var ffmpeg = MedalEnvironment.FindFfmpeg();
            if (dbPath is null || ffmpeg is null)
            {
                if (!isStartup)
                    MessageBox.Show(this, "Medal-Datenbank oder FFmpeg wurde nicht gefunden.", "reMedal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var clipFolder = MedalEnvironment.FindMedalClipFolder(_config.MedalClipFolder);
            await Task.Run(() => _injector.BackfillRecentClipsAsync(
                dbPath,
                clipFolder,
                ffmpeg,
                _config.WatchFolder,
                _config.IncludeSubdirectories,
                daysBack: 30,
                _config.MaxBackups,
                AppendLog,
                CancellationToken.None));
        }
        catch (Exception ex)
        {
            AppendLog($"Nach-Import-Fehler: {ex.Message}");
        }
        finally
        {
            _backfillBtn.Enabled = true;
            Interlocked.Exchange(ref _backfillRunning, 0);
        }
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        // Give Windows a few seconds after login/boot to bring up network interfaces
        await Task.Delay(TimeSpan.FromSeconds(12));
        if (!IsDisposed)
            await CheckForUpdatesAsync(silent: true);
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        _updateBtn.Enabled = false;
        try
        {
            var (available, message, mgr, info) = await AppUpdater.CheckAsync();
            if (!silent || available)
                AppendLog(message);

            if (available && mgr is not null && info is not null)
            {
                var restartArgs = (_launchMinimized || WindowState == FormWindowState.Minimized || !Visible)
                    ? new[] { "--minimized" }
                    : null;

                if (silent)
                {
                    AppendLog("Installiere Update automatisch im Hintergrund...");
                    await AppUpdater.DownloadAndRestartAsync(mgr, info, restartArgs);
                    return;
                }

                var answer = MessageBox.Show(
                    this,
                    $"{message}\n\nJetzt herunterladen und reMedal neu starten?",
                    "reMedal Update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (answer == DialogResult.Yes)
                {
                    AppendLog("Lade Update herunter...");
                    await AppUpdater.DownloadAndRestartAsync(mgr, info, restartArgs);
                }
            }
            else if (!silent)
            {
                MessageBox.Show(this, message, "reMedal Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            if (!IsDisposed)
                _updateBtn.Enabled = true;
        }
    }

    private void StartWatching()
    {
        _watcher.Start(_config);
        UpdateStatusUi();
    }

    private void ToggleWatching()
    {
        if (_watcher.IsRunning)
        {
            _watcher.Stop();
            AppendLog("Auto-Import pausiert.");
        }
        else
        {
            _watcher.Start(_config);
        }
        UpdateStatusUi();
    }

    private void UpdateStatusUi()
    {
        var active = _watcher.IsRunning;
        _statusLabel.Text = active
            ? "Auto-Import: AKTIV – warte auf neue ShadowPlay-Clips"
            : "Auto-Import: PAUSIERT";
        _statusLabel.ForeColor = active ? Color.FromArgb(74, 222, 128) : AccentAmber;
        _toggleButton.Text = active ? "Auto-Import pausieren" : "Auto-Import starten";
        _trayToggleItem.Text = active ? "Auto-Import pausieren" : "Auto-Import starten";
        _trayIcon.Icon = active ? _trayActiveIcon : _trayPausedIcon;
        _trayIcon.Text = active ? "reMedal (Auto-Import aktiv)" : "reMedal (Auto-Import pausiert)";
        Icon = _appIcon;
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "ShadowPlay-Clip-Ordner auswählen",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_folderBox.Text) ? _folderBox.Text : ""
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _folderBox.Text = dlg.SelectedPath;
            ApplyFolderChange();
        }
    }

    private void ApplyFolderChange()
    {
        var path = _folderBox.Text.Trim();
        if (string.Equals(path, _config.WatchFolder, StringComparison.OrdinalIgnoreCase))
            return;

        _config.WatchFolder = path;
        _config.Save();
        if (_watcher.IsRunning)
            _watcher.Start(_config);
        UpdateStatusUi();
    }

    private async Task ImportSingleClipAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "MP4-Clip manuell in Medal importieren",
            Filter = "MP4 Videos (*.mp4)|*.mp4",
            InitialDirectory = Directory.Exists(_config.WatchFolder) ? _config.WatchFolder : ""
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var dbPath = MedalEnvironment.FindMedalDb(_config.MedalDbPath);
        var ffmpeg = MedalEnvironment.FindFfmpeg();
        if (dbPath is null || ffmpeg is null)
        {
            MessageBox.Show(this, "Medal-Datenbank oder FFmpeg wurde nicht gefunden.", "reMedal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var clipFolder = MedalEnvironment.FindMedalClipFolder(_config.MedalClipFolder);
        AppendLog($"Manueller Import gestartet: {Path.GetFileName(dlg.FileName)}");
        try
        {
            var res = await Task.Run(() => _injector.ImportClipAsync(dbPath, clipFolder, ffmpeg, dlg.FileName, _config.MaxBackups, CancellationToken.None));
            AppendLog(res.Message);
        }
        catch (Exception ex)
        {
            AppendLog($"Fehler beim Import: {ex.Message}");
        }
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void AppendLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => AppendLog(message)); } catch { /* closing */ }
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _logBox.AppendText(line);
    }

    private static Icon LoadEmbeddedIcon(string fileName, bool paused)
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .First(r => r.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resName)!;
        using var baseIcon = new Icon(stream, new Size(32, 32));
        if (!paused)
            return (Icon)baseIcon.Clone();

        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawIcon(baseIcon, new Rectangle(0, 0, 32, 32));
        using var bg = new SolidBrush(Color.FromArgb(15, 23, 42));
        g.FillEllipse(bg, 18, 18, 13, 13);
        using var dot = new SolidBrush(AccentAmber);
        g.FillEllipse(dot, 20, 20, 9, 9);
        var hIcon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
