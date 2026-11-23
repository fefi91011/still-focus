using System.Globalization;
using System.Windows.Input;
using Still.App.Services;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IPageViewModel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly MainViewModel _main;

    private bool _closeToTray = true;
    private bool _startMinimized;
    private bool _showOverlay = true;
    private bool _requireApprovalForEdits;
    private bool _requireApprovalForUnlock;
    private bool _blockDoh = true;
    private bool _checkForUpdates = true;
    private string _defaultGrace = "5";
    private string _defaultMaxGrace = "2";
    private string _warningThreshold = "80";
    private string _browserProcesses = string.Empty;
    private string _platformStatus = string.Empty;
    private string _adminStatus = string.Empty;
    private string _websiteStatus = string.Empty;
    private string _processStatus = string.Empty;
    private string _startupStatus = string.Empty;
    private string _dataPath = string.Empty;
    private bool _adminOk;
    private bool _websiteOk;
    private bool _processOk;
    private bool _startupOk;
    private bool _startupInstalled;
    private bool _canInstallStartup;
    private bool _isDemo;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        Update = main.Update;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ex => main.ShowToast(ex.Message, error: true));
        InstallStartupCommand = new AsyncRelayCommand(InstallStartupAsync, ex => main.ShowToast(ex.Message, error: true));
        RemoveStartupCommand = new AsyncRelayCommand(RemoveStartupAsync, ex => main.ShowToast(ex.Message, error: true));
        OpenDataFolderCommand = new RelayCommand(() => _main.Runtime.RevealDataFolder());
        ResetCommand = new AsyncRelayCommand(ResetAsync, ex => main.ShowToast(ex.Message, error: true));
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, ex => main.ShowToast(ex.Message, error: true));

        LoadDraft();
        Refresh();
    }

    public ICommand SaveCommand { get; }
    public ICommand InstallStartupCommand { get; }
    public ICommand RemoveStartupCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand UninstallCommand { get; }
    public UpdateViewModel Update { get; }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => Set(ref _closeToTray, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => Set(ref _startMinimized, value);
    }

    public bool ShowOverlay
    {
        get => _showOverlay;
        set => Set(ref _showOverlay, value);
    }

    public bool RequireApprovalForEdits
    {
        get => _requireApprovalForEdits;
        set => Set(ref _requireApprovalForEdits, value);
    }

    public bool RequireApprovalForUnlock
    {
        get => _requireApprovalForUnlock;
        set => Set(ref _requireApprovalForUnlock, value);
    }

    public bool BlockDoh
    {
        get => _blockDoh;
        set => Set(ref _blockDoh, value);
    }

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set => Set(ref _checkForUpdates, value);
    }

    public string DefaultGrace
    {
        get => _defaultGrace;
        set => Set(ref _defaultGrace, value);
    }

    public string DefaultMaxGrace
    {
        get => _defaultMaxGrace;
        set => Set(ref _defaultMaxGrace, value);
    }

    public string WarningThreshold
    {
        get => _warningThreshold;
        set => Set(ref _warningThreshold, value);
    }

    public string BrowserProcesses
    {
        get => _browserProcesses;
        set => Set(ref _browserProcesses, value);
    }

    public string PlatformStatus
    {
        get => _platformStatus;
        private set => Set(ref _platformStatus, value);
    }

    public string AdminStatus
    {
        get => _adminStatus;
        private set => Set(ref _adminStatus, value);
    }

    public string WebsiteStatus
    {
        get => _websiteStatus;
        private set => Set(ref _websiteStatus, value);
    }

    public string ProcessStatus
    {
        get => _processStatus;
        private set => Set(ref _processStatus, value);
    }

    public string StartupStatus
    {
        get => _startupStatus;
        private set => Set(ref _startupStatus, value);
    }

    public string DataPath
    {
        get => _dataPath;
        private set => Set(ref _dataPath, value);
    }

    public bool AdminOk
    {
        get => _adminOk;
        private set => Set(ref _adminOk, value);
    }

    public bool WebsiteOk
    {
        get => _websiteOk;
        private set => Set(ref _websiteOk, value);
    }

    public bool ProcessOk
    {
        get => _processOk;
        private set => Set(ref _processOk, value);
    }

    public bool StartupOk
    {
        get => _startupOk;
        private set => Set(ref _startupOk, value);
    }

    public bool StartupInstalled
    {
        get => _startupInstalled;
        private set
        {
            if (Set(ref _startupInstalled, value))
            {
                Raise(nameof(CanRemoveStartup));
            }
        }
    }

    public bool CanRemoveStartup => StartupInstalled;
    public bool CanInstallStartup
    {
        get => _canInstallStartup;
        private set => Set(ref _canInstallStartup, value);
    }

    public bool IsDemo
    {
        get => _isDemo;
        private set
        {
            if (Set(ref _isDemo, value))
            {
                Raise(nameof(CanUninstall));
            }
        }
    }

    public bool CanUninstall => !IsDemo && AppUninstaller.Find() is not null;

    public string VersionText => $"Still {_main.VersionText}";

    public void Refresh()
    {
        var runtime = _main.Runtime;
        PlatformStatus = runtime.IsWindows ? runtime.PlatformName : "Demo-Modus";
        AdminStatus = runtime.IsElevated ? "Administrator" : "Normale Rechte";
        AdminOk = runtime.IsElevated;

        WebsiteStatus = runtime.CanBlockWebsites
            ? "Bereit"
            : runtime.WebsiteBlockerReason ?? "Nicht verfügbar";
        WebsiteOk = runtime.CanBlockWebsites;

        ProcessStatus = runtime.CanBlockProcesses
            ? "Bereit"
            : runtime.ProcessBlockerReason ?? "Nicht verfügbar";
        ProcessOk = runtime.CanBlockProcesses;

        StartupInstalled = runtime.IsStartupInstalled;
        CanInstallStartup = runtime.CanInstallStartup;
        StartupStatus = runtime.IsStartupInstalled
            ? "Eingerichtet"
            : runtime.CanInstallStartup
                ? "Nicht eingerichtet"
                : runtime.StartupUnavailableReason ?? "Nicht verfügbar";
        StartupOk = runtime.IsStartupInstalled;

        DataPath = runtime.DataDirectory;
        IsDemo = _main.IsDemo;
    }

    private void LoadDraft()
    {
        var settings = _main.Runtime.State.Settings;
        CloseToTray = settings.CloseToTray;
        StartMinimized = settings.StartMinimized;
        ShowOverlay = settings.ShowBlockedOverlay;
        RequireApprovalForEdits = settings.RequireApprovalForEdits;
        RequireApprovalForUnlock = settings.RequireApprovalForUnlock;
        BlockDoh = settings.BlockDohWhenBlocking;
        CheckForUpdates = settings.CheckForUpdates;
        DefaultGrace = settings.DefaultGraceMinutes.ToString(German);
        DefaultMaxGrace = settings.DefaultMaxGraceUses.ToString(German);
        WarningThreshold = settings.WarningThresholdPercent.ToString(German);
        BrowserProcesses = string.Join(", ", settings.BrowserProcesses);
    }

    private async Task SaveAsync()
    {
        if (!int.TryParse(DefaultGrace.Trim(), NumberStyles.Integer, German, out var grace) || grace is < 1 or > 60)
        {
            _main.ShowToast("Standard-Nachfrist: 1 bis 60 Minuten.", error: true);
            return;
        }

        if (!int.TryParse(DefaultMaxGrace.Trim(), NumberStyles.Integer, German, out var maxGrace) || maxGrace is < 0 or > 10)
        {
            _main.ShowToast("Standard-Nachfristen: 0 bis 10.", error: true);
            return;
        }

        if (!int.TryParse(WarningThreshold.Trim(), NumberStyles.Integer, German, out var threshold) || threshold is < 50 or > 95)
        {
            _main.ShowToast("Warnschwelle: 50 bis 95 Prozent.", error: true);
            return;
        }

        var browsers = BrowserProcesses
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(Target.NormalizeProcess)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (browsers.Count == 0)
        {
            _main.ShowToast("Mindestens ein Browser-Prozess wird benötigt.", error: true);
            return;
        }

        var approved = await _main.EnsureApprovalAsync("diese Einstellungen");
        if (!approved)
        {
            return;
        }

        var current = _main.Runtime.State.Settings;
        var draft = new StillSettings
        {
            CloseToTray = CloseToTray,
            StartMinimized = StartMinimized,
            LaunchAtLogon = current.LaunchAtLogon,
            ShowBlockedOverlay = ShowOverlay,
            RequireApprovalForEdits = RequireApprovalForEdits,
            RequireApprovalForUnlock = RequireApprovalForUnlock,
            DefaultGraceMinutes = grace,
            DefaultMaxGraceUses = maxGrace,
            WarningThresholdPercent = threshold,
            BlockDohWhenBlocking = BlockDoh,
            CheckForUpdates = CheckForUpdates,
            LastUpdateCheckUtc = current.LastUpdateCheckUtc,
            BrowserProcesses = browsers
        };

        var result = _main.Runtime.SaveSettings(draft, approved: true);
        if (result.Ok)
        {
            LoadDraft();
        }

        _main.Notify(result);
    }

    private async Task InstallStartupAsync()
    {
        var result = await Task.Run(() => _main.Runtime.InstallStartup());
        _main.Notify(result);
        Refresh();
    }

    private async Task RemoveStartupAsync()
    {
        var result = await Task.Run(() => _main.Runtime.RemoveStartup());
        _main.Notify(result);
        Refresh();
    }

    private async Task UninstallAsync()
    {
        if (_main.Runtime.IsLockdownActive)
        {
            _main.ShowToast("Cold Turkey läuft. Deinstallieren ist erst danach möglich.", error: true);
            return;
        }

        var uninstaller = AppUninstaller.Find();
        if (uninstaller is null)
        {
            _main.ShowToast(
                "Kein Deinstallationsprogramm gefunden. Diese Version wurde nicht über den Installer installiert.",
                error: true);
            return;
        }

        var confirmed = await _main.ConfirmAsync(
            "Still deinstallieren",
            "Still wird beendet, die hosts-Datei wird wiederhergestellt und der Autostart entfernt. " +
            "Danach öffnet sich der Windows-Deinstaller. " +
            "Regeln und Statistiken bleiben im Datenordner und können dort manuell gelöscht werden.",
            "Deinstallieren");

        if (!confirmed)
        {
            return;
        }

        try
        {
            AppUninstaller.StartAfterExit(uninstaller);
        }
        catch (Exception ex)
        {
            _main.ShowToast("Deinstaller konnte nicht gestartet werden: " + ex.Message, error: true);
            return;
        }

        _main.ShowToast("Still wird beendet, die Deinstallation startet in wenigen Sekunden.");
        _main.RequestShutdown();
    }

    private async Task ResetAsync()
    {
        if (!await _main.ConfirmTypedAsync(
                "Alle Daten zurücksetzen",
                "Alle Regeln, Ausnahmen, Aufgaben, Kontakte und Statistiken werden gelöscht. " +
                "Einstellungen und Timer-Vorlagen bleiben erhalten.",
                "ZURÜCKSETZEN"))
        {
            return;
        }

        var approved = await _main.EnsureApprovalAsync("das Zurücksetzen");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.ResetAll(approved: true);
        _main.Notify(result);
        LoadDraft();
        Refresh();
    }
}
