using Still.Core.Engine;
using Still.Core.Models;
using Still.Core.Persistence;
using Still.Core.Time;

namespace Still.Core.Runtime;

/// <summary>
/// Owns the persisted state, evaluates it once per tick and applies the resulting
/// blocks through the current platform. All public operations are serialized.
/// </summary>
public sealed class StillRuntime : IDisposable
{
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "svchost", "dwm", "explorer", "sihost", "taskhostw", "runtimebroker",
        "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
        "fontdrvhost", "audiodg", "memory compression", "securityhealthsystray",
        "still", "still.app", "still.watchdog"
    };

    private readonly object _gate = new();
    private readonly AppState _state;
    private readonly IStateStore _store;
    private readonly IPlatform _platform;
    private readonly IClock _clock;
    private readonly FocusEngine _engine;
    private readonly Dictionary<string, DateTimeOffset> _lastNotice = new();
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
    private bool _initialized;
    private bool _disposed;
    private string? _persistenceError;

    public StillRuntime(
        AppState state,
        IStateStore store,
        IPlatform platform,
        IClock? clock = null,
        string? loadWarning = null)
    {
        _state = state;
        _store = store;
        _platform = platform;
        _clock = clock ?? SystemClock.Instance;
        LoadWarning = loadWarning;
        _state.Normalize();
        _engine = new FocusEngine(_state, _clock);
        Snapshot = _engine.Evaluate(ActivitySnapshot.Empty);
    }

    public AppState State => _state;

    public EngineSnapshot Snapshot { get; private set; }

    public string? LoadWarning { get; }

    public string PlatformName => _platform.DisplayName;

    public bool IsWindows => _platform.IsWindows;

    public bool IsElevated => _platform.IsElevated;

    public bool CanBlockProcesses => _platform.Processes.CanBlock;

    public string? ProcessBlockerReason => _platform.Processes.UnavailableReason;

    public bool CanInstallStartup => _platform.Startup.CanInstall;

    public bool IsStartupInstalled => _platform.Startup.IsInstalled;

    public string? StartupUnavailableReason => _platform.Startup.UnavailableReason;

    public string DataDirectory => _store.DataDirectory;

    public string? PersistenceError
    {
        get
        {
            lock (_gate)
            {
                return _persistenceError;
            }
        }
    }

    public bool IsLockdownActive => _state.Lockdown?.IsActiveAt(_clock.Now) == true;

    public bool HasApprover => _state.Contacts.Any(c => c.IsApprover && c.HasCode);

    public bool HasEnabledWebsiteRules
    {
        get
        {
            lock (_gate)
            {
                return _state.Rules.Any(rule =>
                    rule.Enabled && rule.Targets.Any(target => target.Kind == TargetKind.Website));
            }
        }
    }

    public bool RequiresApprovalForEdits => _state.Settings.RequireApprovalForEdits && HasApprover;

    public bool RequiresApprovalForUnlock => _state.Settings.RequireApprovalForUnlock && HasApprover;

    public event Action<EngineSnapshot>? SnapshotChanged;

    public event Action<BlockNotice>? BlockDetected;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized || _disposed)
            {
                return;
            }

            _initialized = true;
        }

        Tick();

        lock (_gate)
        {
            SaveLocked();
        }
    }

    /// <summary>One evaluation + enforcement pass. Called once per second by the app.</summary>
    public EngineSnapshot Tick()
    {
        EngineSnapshot snapshot;
        var notices = new List<BlockNotice>();

        lock (_gate)
        {
            if (_disposed)
            {
                return Snapshot;
            }

            var now = _clock.Now;
            var activity = ReadActivity();
            snapshot = _engine.Evaluate(activity);
            Snapshot = snapshot;

            CollectNotices(snapshot, activity, now, notices);

            if (now - _lastSave >= TimeSpan.FromSeconds(20))
            {
                SaveLocked();
            }

            if (now - _lastHeartbeat >= TimeSpan.FromSeconds(5))
            {
                _store.WriteHeartbeat(now);
                _lastHeartbeat = now;
            }
        }

        SnapshotChanged?.Invoke(snapshot);

        foreach (var notice in notices)
        {
            BlockDetected?.Invoke(notice);
        }

        return snapshot;
    }

    public OperationResult GrantGrace(Guid ruleId, int? minutes = null)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = _engine.GrantGrace(ruleId, minutes);
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    /// <summary>
    /// Returns the current block state for a host. The browser extension uses this
    /// to render the same card as the desktop block overlay.
    /// </summary>
    public WebsiteBlockStatus GetWebsiteStatus(string? host)
    {
        lock (_gate)
        {
            return BuildWebsiteStatusLocked(host);
        }
    }

    /// <summary>
    /// Allows the overlay to reappear immediately after the user deliberately
    /// leaves it for contacts. It does not remove the block itself.
    /// </summary>
    public void ResetBlockNotice(string? processName = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                _lastNotice.Clear();
                return;
            }

            _lastNotice.Remove(Target.NormalizeProcess(processName));
        }
    }

    /// <summary>
    /// Grants grace only when the rule actually blocks the requested host. This
    /// keeps the local extension bridge from unlocking an unrelated rule.
    /// </summary>
    public OperationResult GrantWebsiteGrace(string? host, Guid ruleId)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            if (_disposed)
            {
                return OperationResult.Failure("Still wird beendet.");
            }

            var status = BuildWebsiteStatusLocked(host);
            if (!status.Blocked)
            {
                return OperationResult.Failure("Diese Website ist gerade nicht gesperrt.");
            }

            if (status.GraceRuleId != ruleId)
            {
                return OperationResult.Failure("Für diese Website ist keine passende Nachfrist verfügbar.");
            }

            result = _engine.GrantGrace(ruleId);
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    /// <summary>
    /// Closes the foreground app only when the user explicitly asks for it from
    /// the block overlay. Automatic enforcement deliberately does not terminate
    /// processes anymore; the full-screen overlay is the visible block.
    /// </summary>
    public OperationResult CloseBlockedProcess(string? processName)
    {
        var name = Target.NormalizeProcess(processName);
        if (name.Length == 0)
        {
            return OperationResult.Failure("Die blockierte App ist unbekannt.");
        }

        if (!_platform.Processes.CanBlock)
        {
            return OperationResult.Failure(_platform.Processes.UnavailableReason
                                            ?? "Apps können auf diesem System nicht geschlossen werden.");
        }

        if (ProtectedProcesses.Contains(name))
        {
            return OperationResult.Failure("Diese System-App kann nicht geschlossen werden.");
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return OperationResult.Failure("Still wird beendet.");
            }

            if (!Snapshot.BlockedProcesses.Contains(name))
            {
                return OperationResult.Failure("Diese App ist gerade nicht gesperrt.");
            }

            IReadOnlyList<ProcessEntry> processes;
            try
            {
                processes = _platform.Processes.GetProcesses();
            }
            catch (Exception ex)
            {
                return OperationResult.Failure("Die App konnte nicht gefunden werden: " + ex.Message);
            }

            var self = Environment.ProcessId;
            var closed = 0;
            foreach (var process in processes)
            {
                if (process.ProcessId <= 4
                    || process.ProcessId == self
                    || !string.Equals(Target.NormalizeProcess(process.Name), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (_platform.Processes.Terminate(process.ProcessId))
                    {
                        closed++;
                    }
                }
                catch
                {
                    // A process can vanish between enumeration and termination.
                }
            }

            return closed > 0
                ? OperationResult.Success("App geschlossen.")
                : OperationResult.Failure("Die App läuft nicht mehr oder konnte nicht geschlossen werden.");
        }
    }

    public OperationResult CompleteTask(Guid taskId, string? reflection)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = _engine.CompleteTask(taskId, reflection);
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    public OperationResult StartPomodoro(PomodoroStartOptions options)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = _engine.StartPomodoro(options);
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    public OperationResult StopPomodoro()
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = _engine.StopPomodoro();
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    public OperationResult SkipBreak()
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = _engine.SkipBreak();
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    public OperationResult StartLockdown(
        TimeSpan duration,
        string? reason,
        bool blockBrowsers,
        bool allowEarlyExit,
        Guid? approverContactId = null)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = _engine.StartLockdown(duration, reason, blockBrowsers, allowEarlyExit, approverContactId);
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    public OperationResult EndLockdownEarly(string? approvalCode)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = _engine.EndLockdownEarly(approvalCode);
            if (result.Ok)
            {
                snapshot = RefreshLocked(save: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        return result;
    }

    public OperationResult SaveRule(BlockRule rule, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            var validation = RuleValidator.Validate(rule);
            if (!validation.IsValid)
            {
                return OperationResult.Failure(validation.ErrorText);
            }

            var clone = rule.Clone();
            clone.Name = clone.Name.Trim();
            if (clone.Id == Guid.Empty)
            {
                clone.Id = Guid.NewGuid();
            }

            if (clone.CreatedAt == default)
            {
                clone.CreatedAt = _clock.Now;
            }

            clone.Targets = clone.Targets
                .Select(t => new Target(t.Kind, t.Value, t.Label) { IncludeSubdomains = t.IncludeSubdomains })
                .Where(t => t.Value.Length > 0)
                .GroupBy(t => ((int)t.Kind, t.Value), StringPairComparer.Instance)
                .Select(g => g.First())
                .ToList();

            var index = _state.Rules.FindIndex(r => r.Id == clone.Id);
            if (index >= 0)
            {
                _state.Rules[index] = clone;
            }
            else
            {
                _state.Rules.Add(clone);
            }

            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Regel gespeichert.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult DeleteRule(Guid ruleId, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            var rule = _state.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is null)
            {
                return OperationResult.Failure("Die Regel wurde nicht gefunden.");
            }

            _state.Rules.Remove(rule);
            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Regel gelöscht.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult SetRuleEnabled(Guid ruleId, bool enabled, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            var rule = _state.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is null)
            {
                return OperationResult.Failure("Die Regel wurde nicht gefunden.");
            }

            rule.Enabled = enabled;
            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success(enabled ? "Regel aktiviert." : "Regel pausiert.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult SaveAllowance(Allowance allowance, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            var validation = RuleValidator.Validate(allowance);
            if (!validation.IsValid)
            {
                return OperationResult.Failure(validation.ErrorText);
            }

            var clone = allowance.Clone();
            clone.Name = clone.Name.Trim();
            clone.Targets = clone.Targets
                .Select(t => new Target(t.Kind, t.Value, t.Label) { IncludeSubdomains = t.IncludeSubdomains })
                .Where(t => t.Value.Length > 0)
                .GroupBy(t => ((int)t.Kind, t.Value), StringPairComparer.Instance)
                .Select(g => g.First())
                .ToList();

            var index = _state.Allowances.FindIndex(a => a.Id == clone.Id);
            if (index >= 0)
            {
                _state.Allowances[index] = clone;
            }
            else
            {
                _state.Allowances.Add(clone);
            }

            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Ausnahme gespeichert.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult DeleteAllowance(Guid allowanceId, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            var allowance = _state.Allowances.FirstOrDefault(a => a.Id == allowanceId);
            if (allowance is null)
            {
                return OperationResult.Failure("Die Ausnahme wurde nicht gefunden.");
            }

            _state.Allowances.Remove(allowance);
            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Ausnahme gelöscht.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult SaveTask(FocusTask task, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                return OperationResult.Failure("Bitte gib der Aufgabe einen Titel.");
            }

            if (task.RewardMinutes is < 1 or > 240)
            {
                return OperationResult.Failure("Die Belohnung muss zwischen 1 und 240 Minuten liegen.");
            }

            if (task.MaxPerDay is < 1 or > 20)
            {
                return OperationResult.Failure("Die Aufgabe kann 1- bis 20-mal pro Tag erledigt werden.");
            }

            if (task.RuleId is Guid ruleId && !_state.Rules.Any(r => r.Id == ruleId))
            {
                return OperationResult.Failure("Die gewählte Regel existiert nicht mehr.");
            }

            var clone = task.Clone();
            clone.Title = clone.Title.Trim();
            var index = _state.Tasks.FindIndex(t => t.Id == clone.Id);
            if (index >= 0)
            {
                _state.Tasks[index] = clone;
            }
            else
            {
                _state.Tasks.Add(clone);
            }

            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Aufgabe gespeichert.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult DeleteTask(Guid taskId, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            var task = _state.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is null)
            {
                return OperationResult.Failure("Die Aufgabe wurde nicht gefunden.");
            }

            _state.Tasks.Remove(task);
            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Aufgabe gelöscht.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult SaveContact(Contact contact, string? newCode, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            if (IsPinnedLockdownApprover(contact.Id))
            {
                return OperationResult.Failure(
                    "Diese Kontaktperson gibt die laufende Cold-Turkey-Sperre frei. Änderungen sind erst nach dem Ende möglich.");
            }

            if (string.IsNullOrWhiteSpace(contact.Name))
            {
                return OperationResult.Failure("Bitte gib einen Namen ein.");
            }

            var clone = contact.Clone();
            clone.Name = clone.Name.Trim();
            clone.Email = string.IsNullOrWhiteSpace(clone.Email) ? null : clone.Email.Trim();
            clone.Phone = string.IsNullOrWhiteSpace(clone.Phone) ? null : clone.Phone.Trim();
            clone.Note = string.IsNullOrWhiteSpace(clone.Note) ? null : clone.Note.Trim();

            if (!string.IsNullOrWhiteSpace(newCode))
            {
                if (!ApprovalCodes.IsUsable(newCode))
                {
                    return OperationResult.Failure("Der Freigabecode muss mindestens 4 Zeichen haben.");
                }

                var (hash, salt) = ApprovalCodes.Create(newCode);
                clone.CodeHash = hash;
                clone.CodeSalt = salt;
                clone.IsApprover = true;
            }

            if (!clone.IsApprover)
            {
                clone.CodeHash = null;
                clone.CodeSalt = null;
            }
            else if (!clone.HasCode)
            {
                return OperationResult.Failure(
                    "Für Freigaben braucht die Kontaktperson einen Code. Vergib jetzt einen.");
            }

            var index = _state.Contacts.FindIndex(c => c.Id == clone.Id);
            if (index >= 0)
            {
                _state.Contacts[index] = clone;
            }
            else
            {
                _state.Contacts.Add(clone);
            }

            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Kontakt gespeichert.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult DeleteContact(Guid contactId, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            var contact = _state.Contacts.FirstOrDefault(c => c.Id == contactId);
            if (contact is null)
            {
                return OperationResult.Failure("Der Kontakt wurde nicht gefunden.");
            }

            if (IsPinnedLockdownApprover(contact.Id))
            {
                return OperationResult.Failure(
                    "Diese Kontaktperson gibt die laufende Cold-Turkey-Sperre frei und kann jetzt nicht entfernt werden.");
            }

            _state.Contacts.Remove(contact);
            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Kontakt entfernt.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    /// <summary>
    /// Returns a copy of the website usage so the UI can enumerate it safely
    /// while extension reports arrive on the bridge thread.
    /// </summary>
    public IReadOnlyList<WebsiteUsageEntry> GetWebsiteUsageSnapshot()
    {
        lock (_gate)
        {
            return _state.WebsiteUsage
                .Select(entry => new WebsiteUsageEntry
                {
                    Host = entry.Host,
                    Date = entry.Date,
                    UsedSeconds = entry.UsedSeconds
                })
                .ToList();
        }
    }

    /// <summary>
    /// Adds active website time reported by the browser extension. Each report
    /// carries its local calendar date so queued reports survive midnight and
    /// days when the app was closed. This is not a user edit; it only updates
    /// the local usage history.
    /// </summary>
    public void RecordWebsiteUsage(IEnumerable<WebsiteUsageReport> reports)
    {
        var normalized = reports
            .Select(report => (
                report.Date,
                Host: Target.NormalizeHost(report.Host),
                Seconds: report.Seconds))
            .Where(report => report.Host.Length > 0 && report.Seconds > 0)
            .Select(report => (
                report.Date,
                report.Host,
                Seconds: Math.Clamp(report.Seconds, 1, 86_400)))
            .ToList();

        if (normalized.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var report in normalized)
            {
                var entry = _state.WebsiteUsage.FirstOrDefault(usage =>
                    usage.Date == report.Date
                    && usage.Host.Equals(report.Host, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    entry = new WebsiteUsageEntry
                    {
                        Date = report.Date,
                        Host = report.Host
                    };
                    _state.WebsiteUsage.Add(entry);
                }

                entry.UsedSeconds = Math.Min(int.MaxValue, entry.UsedSeconds + report.Seconds);
            }

            SaveLocked();
        }
    }

    /// <summary>
    /// Remembers when the update check last ran. This is not a user edit, so it
    /// neither needs an approval code nor touches any rules.
    /// </summary>
    public void RecordUpdateCheck(DateTimeOffset when)
    {
        lock (_gate)
        {
            _state.Settings.LastUpdateCheckUtc = when.ToUniversalTime();
            SaveLocked();
        }
    }

    public OperationResult SaveSettings(StillSettings draft, bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            if ((draft.RequireApprovalForEdits || draft.RequireApprovalForUnlock)
                && !_state.Contacts.Any(c => c.IsApprover && c.HasCode))
            {
                return OperationResult.Failure(
                    "Lege zuerst unter „Kontakte“ eine Freigabeperson mit Code an.");
            }

            _state.Settings = new StillSettings
            {
                CloseToTray = draft.CloseToTray,
                StartMinimized = draft.StartMinimized,
                LaunchAtLogon = draft.LaunchAtLogon,
                ShowBlockedOverlay = draft.ShowBlockedOverlay,
                RequireApprovalForEdits = draft.RequireApprovalForEdits,
                RequireApprovalForUnlock = draft.RequireApprovalForUnlock,
                DefaultGraceMinutes = draft.DefaultGraceMinutes,
                DefaultMaxGraceUses = draft.DefaultMaxGraceUses,
                WarningThresholdPercent = draft.WarningThresholdPercent,
                CheckForUpdates = draft.CheckForUpdates,
                LastUpdateCheckUtc = draft.LastUpdateCheckUtc,
                BrowserProcesses = draft.BrowserProcesses.ToList()
            };

            _state.Normalize();
            _engine.RebuildUsage();
            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Einstellungen gespeichert.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult ResetAll(bool approved)
    {
        OperationResult result;
        EngineSnapshot? snapshot = null;

        lock (_gate)
        {
            result = GuardEdit(approved);
            if (!result.Ok)
            {
                return result;
            }

            _state.Rules.Clear();
            _state.Allowances.Clear();
            _state.Tasks.Clear();
            _state.Completions.Clear();
            _state.Contacts.Clear();
            _state.Usage.Clear();
            _state.AppUsage.Clear();
            _state.Days.Clear();
            _state.Pomodoro = null;
            _state.Lockdown = null;
            _engine.RebuildUsage();
            snapshot = RefreshLocked(save: true);
            result = OperationResult.Success("Alle Regeln und Daten wurden zurückgesetzt.");
        }

        SnapshotChanged?.Invoke(snapshot!);
        return result;
    }

    public OperationResult InstallStartup()
    {
        lock (_gate)
        {
            return _disposed
                ? OperationResult.Failure("Still wird beendet.")
                : _platform.Startup.Install();
        }
    }

    public OperationResult RemoveStartup()
    {
        lock (_gate)
        {
            return _disposed
                ? OperationResult.Failure("Still wird beendet.")
                : _platform.Startup.Remove();
        }
    }

    public void OpenExternal(string target) => _platform.OpenExternal(target);

    public void RevealDataFolder() => _platform.RevealPath(_store.DataDirectory);

    public bool ValidateApproval(string? code)
    {
        if (!ApprovalCodes.IsUsable(code))
        {
            return false;
        }

        List<(string? Hash, string? Salt)> approvers;
        lock (_gate)
        {
            approvers = _state.Contacts
                .Where(c => c.IsApprover && c.HasCode)
                .Select(c => (c.CodeHash, c.CodeSalt))
                .ToList();
        }

        return approvers.Any(a => ApprovalCodes.Verify(code, a.Hash, a.Salt));
    }

    /// <summary>
    /// The single approver a running Cold Turkey lock depends on. Editing or
    /// deleting that contact would remove the only way out of the lock.
    /// </summary>
    private bool IsPinnedLockdownApprover(Guid contactId) =>
        _state.Lockdown is { AllowEarlyExit: true } lockdown
        && lockdown.IsActiveAt(_clock.Now)
        && lockdown.ApproverContactId == contactId;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                SaveLocked();
            }
            catch
            {
            }

            try
            {
                _store.ClearHeartbeat();
            }
            catch
            {
            }
        }
    }

    private ActivitySnapshot ReadActivity()
    {
        if (!_platform.Processes.CanBlock)
        {
            return ActivitySnapshot.Empty;
        }

        try
        {
            return ActivitySnapshot.From(_platform.Processes.GetForegroundProcesses());
        }
        catch
        {
            return ActivitySnapshot.Empty;
        }
    }

    private WebsiteBlockStatus BuildWebsiteStatusLocked(string? host)
    {
        var normalized = Target.NormalizeHost(host);
        var snapshot = Snapshot;
        if (normalized.Length == 0)
        {
            return new WebsiteBlockStatus(
                false, string.Empty, string.Empty, "Website gesperrt",
                Array.Empty<string>(), null, 0, 0, string.Empty, false);
        }

        var blocked = snapshot.BlockedDomains.Any(domain => HostMatches(normalized, domain));
        if (!blocked)
        {
            return new WebsiteBlockStatus(
                false, normalized, normalized, "Website gesperrt",
                Array.Empty<string>(), null, 0, 0, string.Empty, false);
        }

        var matchingRules = _state.Rules
            .Where(rule => rule.Enabled)
            .Where(rule => rule.Targets.Any(target =>
                target.Kind == TargetKind.Website && target.MatchesHost(normalized)))
            .ToList();

        var blockingRules = snapshot.LockdownActive
            ? matchingRules
            : matchingRules
                .Where(rule => snapshot.FindRule(rule.Id)?.Blocked == true)
                .ToList();

        List<string> ruleNames = snapshot.LockdownActive
            ? ["Cold Turkey"]
            : blockingRules
                .Select(rule => rule.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        if (ruleNames.Count == 0)
        {
            ruleNames = ["Website-Sperre"];
        }

        var matchingTargets = matchingRules
            .SelectMany(rule => rule.Targets)
            .Where(target => target.Kind == TargetKind.Website && target.MatchesHost(normalized))
            .ToList();
        var subject = matchingTargets
            .Select(target => target.Label)
            .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label));

        if (string.IsNullOrWhiteSpace(subject) && snapshot.Lockdown is { } lockdown)
        {
            subject = lockdown.Targets
                .Where(target => target.Kind == TargetKind.Website && target.MatchesHost(normalized))
                .Select(target => target.Label)
                .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label));
        }

        subject = string.IsNullOrWhiteSpace(subject) ? normalized : subject.Trim();

        (BlockRule Rule, RuleStatus Status)? grace = null;
        if (!snapshot.LockdownActive)
        {
            foreach (var rule in blockingRules)
            {
                var status = snapshot.FindRule(rule.Id);
                if (status?.CanGrantGrace == true)
                {
                    grace = (rule, status);
                    break;
                }
            }
        }

        var explanation = grace is { } available
            ? available.Status.GraceUsesLeft switch
            {
                > 1 => $"Noch {available.Status.GraceUsesLeft} Nachfristen heute verfügbar.",
                1 => "Noch 1 Nachfrist heute verfügbar.",
                _ => "Keine Nachfrist mehr übrig. Die Sperre bleibt."
            }
            : snapshot.LockdownActive
                ? "Cold Turkey: keine Nachfrist."
                : "Für diese Sperre gibt es keine Nachfrist.";

        return new WebsiteBlockStatus(
            true,
            normalized,
            subject,
            "Website gesperrt",
            ruleNames,
            grace?.Rule.Id,
            grace?.Rule.GraceMinutes ?? 0,
            grace?.Status.GraceUsesLeft ?? 0,
            explanation,
            snapshot.LockdownActive);
    }

    private static bool HostMatches(string host, string target)
    {
        var exact = target.StartsWith(Target.ExactHostPrefix, StringComparison.Ordinal);
        if (exact)
        {
            target = target[Target.ExactHostPrefix.Length..];
        }

        return host.Equals(target, StringComparison.OrdinalIgnoreCase)
               || (!exact && host.EndsWith("." + target, StringComparison.OrdinalIgnoreCase));
    }

    private void CollectNotices(
        EngineSnapshot snapshot,
        ActivitySnapshot activity,
        DateTimeOffset now,
        List<BlockNotice> notices)
    {
        if (!_state.Settings.ShowBlockedOverlay)
        {
            return;
        }

        // A notice is only throttled while the blocked app stays in the foreground.
        // When it leaves (closed, hidden or replaced by another app), a later return
        // must produce a fresh overlay immediately instead of waiting out the throttle.
        var foregroundNames = activity.ForegroundProcesses
            .Select(Target.NormalizeProcess)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _lastNotice.Keys
                     .Where(name => !foregroundNames.Contains(name))
                     .ToList())
        {
            _lastNotice.Remove(stale);
        }

        foreach (var processName in activity.ForegroundProcesses)
        {
            var name = Target.NormalizeProcess(processName);
            if (name.Length == 0 || !snapshot.BlockedProcesses.Contains(name))
            {
                continue;
            }

            if (ProtectedProcesses.Contains(name))
            {
                continue;
            }

            if (_lastNotice.TryGetValue(name, out var last) && now - last < TimeSpan.FromSeconds(20))
            {
                continue;
            }

            var blockingRules = _state.Rules
                .Where(r => r.Enabled && r.Targets.Any(t => t.MatchesProcess(name)))
                .ToList();
            var ruleNames = blockingRules
                .Select(r => r.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (snapshot.BlockBrowsers
                && _state.Settings.BrowserProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                ruleNames = ["Cold Turkey"];
            }

            if (ruleNames.Count == 0)
            {
                continue;
            }

            Guid? graceRuleId = null;
            var graceMinutes = 0;
            var graceUsesLeft = 0;

            if (!snapshot.LockdownActive)
            {
                foreach (var status in snapshot.Rules.Where(r => r.Blocked && r.CanGrantGrace))
                {
                    var rule = _state.Rules.FirstOrDefault(r => r.Id == status.RuleId);
                    if (rule is null || !rule.Targets.Any(t => t.MatchesProcess(name)))
                    {
                        continue;
                    }

                    graceRuleId = status.RuleId;
                    graceMinutes = rule.GraceMinutes;
                    graceUsesLeft = status.GraceUsesLeft;
                    break;
                }
            }

            var display = _state.Rules
                .SelectMany(r => r.Targets)
                .FirstOrDefault(t => t.MatchesProcess(name))
                ?.Label;

            if (string.IsNullOrWhiteSpace(display))
            {
                display = char.ToUpperInvariant(name[0]) + name[1..];
            }

            _lastNotice[name] = now;
            notices.Add(new BlockNotice(
                display!,
                "Programm gesperrt",
                ruleNames,
                graceRuleId,
                graceMinutes,
                graceUsesLeft,
                name));
        }
    }

    private EngineSnapshot RefreshLocked(bool save)
    {
        Snapshot = _engine.Evaluate(ReadActivity());
        if (save)
        {
            SaveLocked();
        }

        return Snapshot;
    }

    private OperationResult GuardEdit(bool approved)
    {
        if (_disposed)
        {
            return OperationResult.Failure("Still wird beendet.");
        }

        if (_state.Lockdown?.IsActiveAt(_clock.Now) == true)
        {
            return OperationResult.Failure(
                "Cold Turkey läuft. Änderungen sind bis zum Ende der Sperre gesperrt.");
        }

        if (RequiresApprovalForEdits && !approved)
        {
            return OperationResult.Failure(
                "Für diese Änderung ist die Freigabe einer Kontaktperson nötig.");
        }

        return OperationResult.Success();
    }

    private void SaveLocked()
    {
        try
        {
            _store.Save(_state);
            _lastSave = _clock.Now;
            _persistenceError = null;
        }
        catch (Exception ex)
        {
            _persistenceError = "Speichern fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            // The store normalizes the state before serializing it. Rebuild the
            // indexed usage books so subsequent ticks write into the persisted objects.
            _engine.RebuildUsage();
        }
    }

    private sealed class StringPairComparer : IEqualityComparer<(int Kind, string Value)>
    {
        public static StringPairComparer Instance { get; } = new();

        public bool Equals((int Kind, string Value) x, (int Kind, string Value) y) =>
            x.Kind == y.Kind && string.Equals(x.Value, y.Value, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((int Kind, string Value) obj) =>
            HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Value));
    }
}
