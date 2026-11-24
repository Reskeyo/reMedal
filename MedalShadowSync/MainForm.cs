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
        (0, "1 Tag (selber Tag)"),
        (1, "±1 Tag"),
        (2, "±2 Tage"),
        (3, "±3 Tage"),
        (7, "±7 Tage"),
        (14, "±14 Tage"),
        (30, "±30 Tage")
    ];

    private static readonly (int Years, string Label)[] MaxYearsOptions =
    [
        (0, "Alle Jahre"),
        (1, "Letztes Jahr (1J)"),
        (2, "Bis 2 Jahre"),
        (3, "Bis 3 Jahre"),
        (5, "Bis 5 Jahre")
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
    private readonly DarkComboBox _windowDaysCombo;
    private readonly DarkComboBox _maxYearsCombo;
    private readonly CheckBox _blockAdsCheck;
    private readonly Button _toggleButton;
    private readonly Button _backfillBtn;
    private readonly Button _updateBtn;
    private readonly TextBox _logBox;

    private bool _exiting;
    private bool _trayHintShown;
    private int _backfillRunning;
    private int _repatchRunning;
    private readonly CancellationTokenSource _lifecycleCts = new();

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
        Size = new Size(820, 620);
        MinimumSize = new Size(760, 540);
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

        // --- Main layout (2 compact cards + log + slim footer) ---
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BgDark
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 1. ShadowPlay-Import card (status + quick actions + folder + options)
        var importStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            BackColor = CardBg
        };
        importStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        importStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        importStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var importRow1 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = CardBg,
            Margin = new Padding(0, 0, 0, 8)
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            MinimumSize = new Size(0, 30),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 14, 0)
        };
        _toggleButton = CreateButton("Pausieren", AccentBlue, AccentBlueHover);
        _toggleButton.Click += (_, _) => ToggleWatching();

        var importOneBtn = CreateButton("Clip importieren...", BtnSlate, BtnSlateHover);
        importOneBtn.Click += async (_, _) => await ImportSingleClipAsync();

        _backfillBtn = CreateButton("Letzte 30 Tage importieren", BtnSlate, BtnSlateHover);
        _backfillBtn.Margin = new Padding(0);
        _backfillBtn.Click += async (_, _) => await RunBackfillAsync(isStartup: false);

        importRow1.Controls.Add(_statusLabel);
        importRow1.Controls.Add(_toggleButton);
        importRow1.Controls.Add(importOneBtn);
        importRow1.Controls.Add(_backfillBtn);

        var folderLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            BackColor = CardBg,
            Margin = new Padding(0, 0, 0, 8)
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

        var browseBtn = CreateButton("...", BtnSlate, BtnSlateHover);
        browseBtn.AutoSize = false;
        browseBtn.Size = new Size(36, 30);
        browseBtn.Padding = new Padding(0);
        browseBtn.Margin = new Padding(0, 0, 8, 0);
        _toolTip.SetToolTip(browseBtn, "ShadowPlay-Ordner auswählen");
        browseBtn.Click += (_, _) => BrowseFolder();

        var detectSpBtn = CreateButton("ShadowPlay finden", BtnSlate, BtnSlateHover);
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

        var optionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = CardBg,
            Margin = new Padding(0)
        };
        _subfoldersCheck = CreateCheckBox("Unterordner", _config.IncludeSubdirectories, CardBg);
        _subfoldersCheck.CheckedChanged += (_, _) =>
        {
            _config.IncludeSubdirectories = _subfoldersCheck.Checked;
            _config.Save();
            if (_watcher.IsRunning) _watcher.Start(_config);
        };
        _autostartCheck = CreateCheckBox("Mit Windows starten", MedalEnvironment.IsAutostartEnabled(), CardBg);
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
        _minimizedCheck = CreateCheckBox("Minimiert im Tray starten", _config.StartMinimized, CardBg);
        _minimizedCheck.CheckedChanged += (_, _) =>
        {
            _config.StartMinimized = _minimizedCheck.Checked;
            _config.Save();
        };
        optionsFlow.Controls.Add(_subfoldersCheck);
        optionsFlow.Controls.Add(_autostartCheck);
        optionsFlow.Controls.Add(_minimizedCheck);

        importStack.Controls.Add(importRow1, 0, 0);
        importStack.Controls.Add(folderLayout, 0, 1);
        importStack.Controls.Add(optionsFlow, 0, 2);
        root.Controls.Add(CreateCard("ShadowPlay-Import", importStack), 0, 0);

        // 2. Medal-Patch card (Throwbacks + Ad-Blocker)
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
            Margin = new Padding(0, 0, 0, 8)
        };
        _patchStatusLabel = new Label
        {
            AutoSize = true,
            MinimumSize = new Size(0, 30),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 14, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = TextPrimary
        };
        _patchBtn = CreateButton("Patch aktivieren", AccentBlue, AccentBlueHover);
        _patchBtn.Click += (_, _) => ApplyThrowbacksPatch();

        _restoreBtn = CreateButton("Wiederherstellen", BtnSlate, BtnSlateHover);
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
            Text = "Zeitraum:",
            AutoSize = true,
            MinimumSize = new Size(0, 28),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 6, 0)
        };
        _windowDaysCombo = new DarkComboBox(InputBg, TextPrimary, BorderColor, AccentBlue)
        {
            Width = 140,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 16, 0)
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
            var desc = _config.ThrowbackWindowDays == 0 ? "1 Tag (selber Tag)" : $"±{_config.ThrowbackWindowDays} {(_config.ThrowbackWindowDays == 1 ? "Tag" : "Tage")}";
            AppendLog($"Rückblick-Zeitraum auf {desc} gesetzt (zum Übernehmen bitte 'Erneut patchen' klicken).");
        };

        var yearsLbl = new Label
        {
            Text = "Jahre:",
            AutoSize = true,
            MinimumSize = new Size(0, 28),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            Margin = new Padding(0, 0, 6, 0)
        };
        _maxYearsCombo = new DarkComboBox(InputBg, TextPrimary, BorderColor, AccentBlue)
        {
            Width = 135,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 18, 0)
        };
        foreach (var opt in MaxYearsOptions)
            _maxYearsCombo.Items.Add(opt.Label);
        var yearsIdx = Array.FindIndex(MaxYearsOptions, o => o.Years == _config.ThrowbackMaxYears);
        _maxYearsCombo.SelectedIndex = yearsIdx >= 0 ? yearsIdx : 0;
        _maxYearsCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_maxYearsCombo.SelectedIndex < 0) return;
            _config.ThrowbackMaxYears = MaxYearsOptions[_maxYearsCombo.SelectedIndex].Years;
            _config.ThrowbackOnlyLastYear = _config.ThrowbackMaxYears == 1;
            _config.Save();
            AppendLog($"Rückblick-Tiefe auf '{MaxYearsOptions[_maxYearsCombo.SelectedIndex].Label}' gesetzt (zum Übernehmen bitte 'Erneut patchen' klicken).");
        };

        _blockAdsCheck = CreateCheckBox("Werbung ausblenden", _config.BlockAdsEnabled, CardBg);
        _blockAdsCheck.Margin = new Padding(0, 3, 0, 0);
        _blockAdsCheck.CheckedChanged += (_, _) =>
        {
            _config.BlockAdsEnabled = _blockAdsCheck.Checked;
            _config.Save();
            AppendLog(_config.BlockAdsEnabled
                ? "Werbe-Blocker aktiviert (zum Übernehmen bitte 'Erneut patchen' klicken)."
                : "Werbe-Blocker deaktiviert (zum Übernehmen bitte 'Erneut patchen' klicken).");
        };

        extRow2.Controls.Add(windowLbl);
        extRow2.Controls.Add(_windowDaysCombo);
        extRow2.Controls.Add(yearsLbl);
        extRow2.Controls.Add(_maxYearsCombo);
        extRow2.Controls.Add(_blockAdsCheck);

        extStack.Controls.Add(extRow1, 0, 0);
        extStack.Controls.Add(extRow2, 0, 1);
        root.Controls.Add(CreateCard("Medal-Patch (Rückblicke & Werbefreiheit)", extStack), 0, 1);

        // 3. Log card
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
        root.Controls.Add(CreateCard("Aktivitätsprotokoll", _logBox, fillDock: true), 0, 2);

        // 4. Slim footer (environment info + Backups / Updates)
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            BackColor = BgDark,
            Margin = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _envLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(2, 6, 8, 0)
        };
        var backupsBtn = CreateButton("Backups", BtnSlate, BtnSlateHover);
        backupsBtn.MinimumSize = new Size(0, 28);
        backupsBtn.Margin = new Padding(0, 0, 8, 0);
        backupsBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppConfig.BackupsDir);
            Process.Start(new ProcessStartInfo("explorer.exe", AppConfig.BackupsDir) { UseShellExecute = true });
        };

        _updateBtn = CreateButton("Updates prüfen", BtnSlate, BtnSlateHover);
        _updateBtn.MinimumSize = new Size(0, 28);
        _updateBtn.Margin = new Padding(0);
        _updateBtn.Click += async (_, _) => await CheckForUpdatesAsync(silent: false);

        footer.Controls.Add(_envLabel, 0, 0);
        footer.Controls.Add(backupsBtn, 1, 0);
        footer.Controls.Add(_updateBtn, 2, 0);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);

        RefreshEnvironmentLabel();
        RefreshPatchStatus();
        StartWatching();
        _ = RunStartupUpdateCheckAsync();
        _ = DelayedAutoRepatchCheckAsync(TimeSpan.FromSeconds(25), "App-Start");
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

        _lifecycleCts.Cancel();
        _lifecycleCts.Dispose();
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
            Height = 30,
            MinimumSize = new Size(0, 30),
            Anchor = AnchorStyles.Left,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = TextPrimary,
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = BorderColor;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = hover;
        return btn;
    }

    private static CheckBox CreateCheckBox(string text, bool isChecked, Color? bg = null) => new()
    {
        Text = text,
        AutoSize = true,
        Checked = isChecked,
        ForeColor = TextPrimary,
        BackColor = bg ?? BgDark,
        Margin = new Padding(0, 0, 18, 2)
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
                _patchStatusLabel.Text = "Status: Aktiv (V18 gepatcht)";
                _patchStatusLabel.ForeColor = Color.FromArgb(74, 222, 128);
                _patchBtn.Text = "Erneut patchen";
                _restoreBtn.Enabled = true;
                break;
            case PatchStatus.OutdatedPatch:
                _patchStatusLabel.Text = "Status: Veralteter Patch (Update nötig)";
                _patchStatusLabel.ForeColor = AccentAmber;
                _patchBtn.Text = "Patch auf V18 aktualisieren";
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

            MedalAsarPatcher.KillMedal();
            Thread.Sleep(500);
        }

        if (Interlocked.CompareExchange(ref _repatchRunning, 1, 0) != 0)
            return;

        try
        {
            var (ok, msg) = MedalAsarPatcher.ApplyPatch(null, _config.ThrowbackWindowDays, _config.BlockAdsEnabled, _config.ThrowbackMaxYears);
            AppendLog(msg);
            if (ok)
            {
                _config.ThrowbacksEnabled = true;
                _config.Save();
                if (medalWasRunning)
                {
                    MedalAsarPatcher.RestartMedal();
                    // Medal may trigger a self-update on restart; re-check once after 20s
                    _ = DelayedAutoRepatchCheckAsync(TimeSpan.FromSeconds(20), "nach Medal-Neustart");
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Patch-Fehler: {ex.Message}");
            MessageBox.Show(this, $"Fehler beim Patchen:\n{ex.Message}", "reMedal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _repatchRunning, 0);
        }

        RefreshPatchStatus();
    }

    private async Task DelayedAutoRepatchCheckAsync(TimeSpan delay, string reason)
    {
        try
        {
            await Task.Delay(delay, _lifecycleCts.Token);
            await TryAutoRepatchAsync(reason, _lifecycleCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TryAutoRepatchAsync(string reason, CancellationToken ct)
    {
        if (!_config.ThrowbacksEnabled || IsDisposed)
            return;

        if (Interlocked.CompareExchange(ref _repatchRunning, 1, 0) != 0)
            return;

        try
        {
            var status = await Task.Run(() => MedalAsarPatcher.GetStatus(), ct);
            if (status is not (PatchStatus.Unpatched or PatchStatus.OutdatedPatch))
                return;

            AppendLog($"Medal-Update/ungepatchter Stand erkannt ({reason}) – patche automatisch neu...");

            var (ok, msg) = await Task.Run(() =>
            {
                var wasRunning = MedalAsarPatcher.IsMedalRunning();
                if (wasRunning)
                {
                    MedalAsarPatcher.KillMedal();
                    Thread.Sleep(600);
                }

                var result = MedalAsarPatcher.ApplyPatch(
                    null,
                    _config.ThrowbackWindowDays,
                    _config.BlockAdsEnabled,
                    _config.ThrowbackMaxYears);

                if (result.Ok && wasRunning)
                    MedalAsarPatcher.RestartMedal();

                return result;
            }, ct);

            AppendLog(ok ? $"Auto-Patch erfolgreich ({reason})." : $"Auto-Patch fehlgeschlagen: {msg}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppendLog($"Auto-Patch-Fehler ({reason}): {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _repatchRunning, 0);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(RefreshPatchStatus);
        }
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
        _statusLabel.Text = active ? "Auto-Import: AKTIV" : "Auto-Import: PAUSIERT";
        _statusLabel.ForeColor = active ? Color.FromArgb(74, 222, 128) : AccentAmber;
        _toggleButton.Text = active ? "Pausieren" : "Starten";
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

    private sealed class DarkComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;
        private readonly Color _bg;
        private readonly Color _fg;
        private readonly Color _border;
        private readonly Color _highlight;

        public DarkComboBox(Color bg, Color fg, Color border, Color highlight)
        {
            _bg = bg;
            _fg = fg;
            _border = border;
            _highlight = highlight;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = bg;
            ForeColor = fg;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var bgBrush = new SolidBrush(selected ? _highlight : _bg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                GetItemText(Items[e.Index]),
                Font,
                new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                _fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
            {
                using var g = Graphics.FromHwnd(Handle);
                using var bgBrush = new SolidBrush(_bg);
                g.FillRectangle(bgBrush, Width - 20, 1, 19, Height - 2);
                using var borderPen = new Pen(_border, 1);
                using var innerPen = new Pen(_bg, 2);
                g.DrawRectangle(innerPen, 1, 1, Width - 3, Height - 3);
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
                var arrowX = Width - 14;
                var arrowY = Height / 2 - 2;
                using var arrowBrush = new SolidBrush(_fg);
                g.FillPolygon(arrowBrush, new[]
                {
                    new Point(arrowX, arrowY),
                    new Point(arrowX + 8, arrowY),
                    new Point(arrowX + 4, arrowY + 5)
                });
            }
        }
    }
}
