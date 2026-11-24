using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Still.App.Services;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IPageViewModel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly MainViewModel _main;
    private readonly BrowserExtensionService _browserService = new();

    private bool _closeToTray = true;
    private bool _startMinimized;
    private bool _showOverlay = true;
    private bool _requireApprovalForEdits;
    private bool _requireApprovalForUnlock;
    private bool _checkForUpdates = true;
    private string _defaultGrace = "5";
    private string _defaultMaxGrace = "2";
    private string _warningThreshold = "80";
    private string _browserProcesses = string.Empty;
    private string _platformStatus = string.Empty;
    private string _adminStatus = string.Empty;
    private string _extensionStatus = "Nicht verbunden";
    private string _extensionDetail = string.Empty;
    private string _processStatus = string.Empty;
    private string _startupStatus = string.Empty;
    private string _dataPath = string.Empty;
    private bool _adminOk;
    private bool _extensionOk;
    private bool _processOk;
    private bool _startupOk;
    private bool _startupInstalled;
    private bool _canInstallStartup;
    private bool _isDemo;
    private bool _browsersLoaded;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        Update = main.Update;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ex => main.ShowToast(ex.Message, error: true));
        InstallStartupCommand = new AsyncRelayCommand(InstallStartupAsync, ex => main.ShowToast(ex.Message, error: true));
        RemoveStartupCommand = new AsyncRelayCommand(RemoveStartupAsync, ex => main.ShowToast(ex.Message, error: true));
        OpenDataFolderCommand = new RelayCommand(() => _main.Runtime.RevealDataFolder());
        RefreshBrowsersCommand = new RelayCommand(LoadBrowsers);
        OpenExtensionFolderCommand = new RelayCommand(OpenExtensionFolder);
        ResetCommand = new AsyncRelayCommand(ResetAsync, ex => main.ShowToast(ex.Message, error: true));
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, ex => main.ShowToast(ex.Message, error: true));

        LoadDraft();
        Refresh();
    }

    public ICommand SaveCommand { get; }
    public ICommand InstallStartupCommand { get; }
    public ICommand RemoveStartupCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand RefreshBrowsersCommand { get; }
    public ICommand OpenExtensionFolderCommand { get; }
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

    public string ExtensionStatus
    {
        get => _extensionStatus;
        private set => Set(ref _extensionStatus, value);
    }

    public string ExtensionDetail
    {
        get => _extensionDetail;
        private set => Set(ref _extensionDetail, value);
    }

    public ObservableCollection<BrowserRowViewModel> Browsers { get; } = new();

    public bool HasBrowsers => Browsers.Count > 0;

    public bool CanRefreshBrowsers => !IsDemo;

    public string BrowserEmptyText => IsDemo
        ? "Im Demo-Modus werden keine Browser erkannt."
        : "Kein unterstützter Browser gefunden. Chrome, Edge, Brave, Vivaldi, Opera, Firefox, Zen, LibreWolf, Waterfox und Floorp werden erkannt.";

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

    public bool ExtensionOk
    {
        get => _extensionOk;
        private set => Set(ref _extensionOk, value);
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
                Raise(nameof(CanRefreshBrowsers));
                Raise(nameof(BrowserEmptyText));
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

        var client = _main.ExtensionBridge?.LastClient;
        var connected = _main.ExtensionBridge?.IsConnected == true;
        ExtensionOk = IsDemo || connected;
        ExtensionStatus = IsDemo ? "Demo" : connected ? "Verbunden" : "Nicht verbunden";
        ExtensionDetail = IsDemo
            ? "Im Demo-Modus wird keine Browser-Erweiterung verbunden."
            : client is null
                ? "Noch keine Verbindung. Richte die Erweiterung unten für deinen Browser ein."
                : connected
                    ? $"{client.Name} {client.Version} · zuletzt {client.SeenAt.LocalDateTime:HH:mm}"
                    : $"{client.Name} · zuletzt {client.SeenAt.LocalDateTime:dd.MM. HH:mm}";

        if (!_browsersLoaded && !IsDemo)
        {
            _browsersLoaded = true;
            LoadBrowsers();
        }
    }

    private void LoadBrowsers()
    {
        Browsers.Clear();
        if (!IsDemo)
        {
            foreach (var browser in _browserService.DetectInstalled())
            {
                Browsers.Add(new BrowserRowViewModel(
                    browser,
                    SetupBrowserAsync,
                    ex => _main.ShowToast(ex.Message, error: true)));
            }
        }

        Raise(nameof(HasBrowsers));
    }

    private async Task SetupBrowserAsync(DetectedBrowser browser)
    {
        var description = await Task.Run(() => _browserService.Describe(browser));
        if (!description.Ok)
        {
            await _main.InfoAsync("Browser-Schutz einrichten", description.Message);
            return;
        }

        var confirmed = await _main.ConfirmAsync(
            "Browser-Schutz einrichten",
            description.Message,
            "Jetzt öffnen");
        if (!confirmed)
        {
            return;
        }

        var result = await Task.Run(() => _browserService.Open(browser));
        if (!result.Ok)
        {
            await _main.InfoAsync("Browser-Schutz einrichten", result.Message);
        }
        else
        {
            _main.ShowToast($"{browser.Name} geöffnet. Folge den Schritten im Browser.");
        }

        Refresh();
    }

    private void OpenExtensionFolder()
    {
        _browserService.OpenExtensionFolder();
        _main.ShowToast("Erweiterungsordner geöffnet.");
    }

    private void LoadDraft()
    {
        var settings = _main.Runtime.State.Settings;
        CloseToTray = settings.CloseToTray;
        StartMinimized = settings.StartMinimized;
        ShowOverlay = settings.ShowBlockedOverlay;
        RequireApprovalForEdits = settings.RequireApprovalForEdits;
        RequireApprovalForUnlock = settings.RequireApprovalForUnlock;
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
            "Still wird beendet, alte hosts-Einträge und der Autostart werden entfernt. " +
            "Danach öffnet sich der Windows-Deinstaller. Die Browser-Erweiterung muss im Browser " +
            "selbst entfernt werden. Regeln und Statistiken bleiben im Datenordner und können dort " +
            "manuell gelöscht werden.",
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

public sealed class BrowserRowViewModel
{
    public BrowserRowViewModel(
        DetectedBrowser browser,
        Func<DetectedBrowser, Task> setup,
        Action<Exception> onError)
    {
        Browser = browser;
        Name = browser.Name;
        Detail = browser.EngineLabel + " · " + browser.ExecutablePath;
        SetupCommand = new AsyncRelayCommand(() => setup(browser), onError);
    }

    public DetectedBrowser Browser { get; }
    public string Name { get; }
    public string Detail { get; }
    public ICommand SetupCommand { get; }
}
