using System.Collections.ObjectModel;
using System.Windows.Input;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class RulesViewModel : ObservableObject, IPageViewModel
{
    private readonly MainViewModel _main;
    private string _signature = "init";
    private bool _isRulesTab = true;
    private bool _isAllowancesTab;

    public RulesViewModel(MainViewModel main)
    {
        _main = main;
        Editor = new RuleEditorViewModel(this);
        AllowanceEditor = new AllowanceEditorViewModel(this);

        NewRuleCommand = new RelayCommand(OpenNewRule);
        NewAllowanceCommand = new RelayCommand(OpenNewAllowance);
        ShowRulesTabCommand = new RelayCommand(() =>
        {
            IsRulesTab = true;
            IsAllowancesTab = false;
        });
        ShowAllowancesTabCommand = new RelayCommand(() =>
        {
            IsRulesTab = false;
            IsAllowancesTab = true;
        });

        Refresh();
    }

    public ObservableCollection<RuleRowViewModel> Rules { get; } = new();
    public ObservableCollection<AllowanceRowViewModel> Allowances { get; } = new();
    public RuleEditorViewModel Editor { get; }
    public AllowanceEditorViewModel AllowanceEditor { get; }

    public ICommand NewRuleCommand { get; }
    public ICommand NewAllowanceCommand { get; }
    public ICommand ShowRulesTabCommand { get; }
    public ICommand ShowAllowancesTabCommand { get; }

    public bool IsRulesTab
    {
        get => _isRulesTab;
        private set
        {
            if (Set(ref _isRulesTab, value))
            {
                RaiseEmptyStates();
            }
        }
    }

    public bool IsAllowancesTab
    {
        get => _isAllowancesTab;
        private set
        {
            if (Set(ref _isAllowancesTab, value))
            {
                RaiseEmptyStates();
            }
        }
    }

    public bool HasRules => Rules.Count > 0;
    public bool HasAllowances => Allowances.Count > 0;
    public bool IsEmpty => IsRulesTab ? Rules.Count == 0 : Allowances.Count == 0;
    public bool ShowRulesEmpty => IsRulesTab && Rules.Count == 0;
    public bool ShowAllowancesEmpty => IsAllowancesTab && Allowances.Count == 0;
    public bool NoEditorOpen => !Editor.IsOpen && !AllowanceEditor.IsOpen;
    public string RulesCountText => Rules.Count == 1 ? "1 Regel" : $"{Rules.Count} Regeln";
    public string AllowancesCountText => Allowances.Count == 1 ? "1 Ausnahme" : $"{Allowances.Count} Ausnahmen";

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var snapshot = _main.Runtime.Snapshot;
        var now = DateTimeOffset.Now;

        var sig = string.Join("|", state.Rules.Select(r =>
                       $"{r.Id}:{r.Name}:{r.Enabled}:{r.DailyLimitMinutes}:{r.GraceMinutes}:{r.MaxGraceUsesPerDay}:{r.Schedule.Describe()}:{TargetsSignature(r.Targets)}"))
                  + "§" + string.Join("|", snapshot.Rules.Select(r =>
                       $"{r.RuleId}:{r.Blocked}:{r.Armed}:{r.UsedMinutes}:{r.RemainingMinutes}:{r.GraceUses}"))
                  + "§" + string.Join("|", state.Allowances.Select(a =>
                       $"{a.Id}:{a.Name}:{a.Enabled}:{a.IsActiveAt(now)}:{a.ExpiresAt}:{TargetsSignature(a.Targets)}"))
                  + $"§{Editor.IsOpen}:{Editor.Id}§{AllowanceEditor.IsOpen}:{AllowanceEditor.Id}";

        if (sig == _signature)
        {
            return;
        }

        _signature = sig;

        Rules.Clear();
        foreach (var rule in state.Rules.OrderByDescending(r => r.Enabled).ThenBy(r => r.Name))
        {
            var status = snapshot.FindRule(rule.Id);
            var limit = rule.DailyLimitMinutes is int value && value > 0 ? value : (int?)null;
            var blocked = status?.Blocked == true;
            var stateLabel = !rule.Enabled
                ? "Pausiert"
                : blocked
                    ? "Gesperrt"
                    : status?.Armed == true ? "Aktiv" : "Geplant";

            var targets = rule.DistinctTargets()
                .Select(t => t.Label ?? t.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var targetText = string.Join(" · ", targets.Take(4));
            if (targets.Count > 4)
            {
                targetText += $" +{targets.Count - 4}";
            }

            var budget = limit is null
                ? "Ohne Limit"
                : $"{status?.UsedMinutes ?? 0} / {limit + (status?.BonusMinutes ?? 0) + (status?.GraceBonusMinutes ?? 0)} Min";

            Rules.Add(new RuleRowViewModel
            {
                Id = rule.Id,
                Name = rule.Name,
                TargetsText = targetText,
                ScheduleText = rule.Schedule.Describe(),
                LimitText = budget,
                StateLabel = stateLabel,
                IsPaused = !rule.Enabled,
                IsBlocked = blocked,
                IsSelected = Editor.IsOpen && Editor.Id == rule.Id,
                OpenCommand = new RelayCommand(() => OpenRule(rule.Id))
            });
        }

        Allowances.Clear();
        foreach (var allowance in state.Allowances.OrderByDescending(a => a.Enabled).ThenBy(a => a.Name))
        {
            var active = allowance.IsActiveAt(now);
            var availability = !allowance.Enabled
                ? "Pausiert"
                : active
                    ? allowance.ExpiresAt is null ? "Jetzt aktiv" : $"Aktiv bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}"
                    : allowance.ExpiresAt is null ? "Gerade nicht aktiv" : $"Bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}";

            Allowances.Add(new AllowanceRowViewModel
            {
                Id = allowance.Id,
                Name = allowance.Name,
                TargetsText = string.Join(" · ", allowance.Targets.Select(t => t.Label ?? t.Value).Take(4)),
                AvailabilityText = availability,
                IsActive = active,
                IsPaused = !allowance.Enabled,
                IsSelected = AllowanceEditor.IsOpen && AllowanceEditor.Id == allowance.Id,
                OpenCommand = new RelayCommand(() => OpenAllowance(allowance.Id))
            });
        }

        Raise(nameof(HasRules));
        Raise(nameof(HasAllowances));
        Raise(nameof(IsEmpty));
        Raise(nameof(RulesCountText));
        Raise(nameof(AllowancesCountText));
        RaiseEmptyStates();
        Raise(nameof(NoEditorOpen));
    }

    private void RaiseEmptyStates()
    {
        Raise(nameof(ShowRulesEmpty));
        Raise(nameof(ShowAllowancesEmpty));
    }

    internal async Task SaveRuleAsync(BlockRule rule)
    {
        var approved = await _main.EnsureApprovalAsync("diese Regel");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.SaveRule(rule, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task DeleteRuleAsync(Guid ruleId, string name)
    {
        if (!await _main.ConfirmAsync("Regel löschen", $"„{name}“ wirklich löschen?", "Löschen"))
        {
            return;
        }

        var approved = await _main.EnsureApprovalAsync("das Löschen dieser Regel");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.DeleteRule(ruleId, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task SaveAllowanceAsync(Allowance allowance)
    {
        var approved = await _main.EnsureApprovalAsync("diese Ausnahme");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.SaveAllowance(allowance, approved: true);
        if (!result.Ok)
        {
            AllowanceEditor.SetError(result.Message);
            return;
        }

        AllowanceEditor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task DeleteAllowanceAsync(Guid allowanceId, string name)
    {
        if (!await _main.ConfirmAsync("Ausnahme löschen", $"„{name}“ wirklich löschen?", "Löschen"))
        {
            return;
        }

        var approved = await _main.EnsureApprovalAsync("das Löschen dieser Ausnahme");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.DeleteAllowance(allowanceId, approved: true);
        if (!result.Ok)
        {
            AllowanceEditor.SetError(result.Message);
            return;
        }

        AllowanceEditor.Close();
        _main.Notify(result);
        Refresh();
    }

    private void OpenNewRule()
    {
        IsRulesTab = true;
        IsAllowancesTab = false;
        var settings = _main.Runtime.State.Settings;
        Editor.Open(new BlockRule
        {
            Name = string.Empty,
            Enabled = true,
            Schedule = Schedule.Always(),
            DailyLimitMinutes = 60,
            GraceMinutes = settings.DefaultGraceMinutes,
            MaxGraceUsesPerDay = settings.DefaultMaxGraceUses
        }, isNew: true);
        Refresh();
    }

    private void OpenRule(Guid ruleId)
    {
        var rule = _main.Runtime.State.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
        {
            return;
        }

        Editor.Open(rule.Clone(), isNew: false);
        Refresh();
    }

    private void OpenNewAllowance()
    {
        IsRulesTab = false;
        IsAllowancesTab = true;
        AllowanceEditor.Open(new Allowance
        {
            Name = string.Empty,
            Enabled = true,
            Schedule = Schedule.Always()
        }, isNew: true);
        Refresh();
    }

    private void OpenAllowance(Guid allowanceId)
    {
        var allowance = _main.Runtime.State.Allowances.FirstOrDefault(a => a.Id == allowanceId);
        if (allowance is null)
        {
            return;
        }

        AllowanceEditor.Open(allowance.Clone(), isNew: false);
        Refresh();
    }

    private static string TargetsSignature(IEnumerable<Target> targets) =>
        string.Join(",", targets.Select(t => $"{(int)t.Kind}:{t.Value}:{(t.IncludeSubdomains ? 1 : 0)}"));
}

public sealed class RuleRowViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public string ScheduleText { get; init; } = string.Empty;
    public string LimitText { get; init; } = string.Empty;
    public string StateLabel { get; init; } = string.Empty;
    public bool IsPaused { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsSelected { get; init; }
    public ICommand OpenCommand { get; init; } = new RelayCommand(() => { });
}

public sealed class AllowanceRowViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public string AvailabilityText { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsPaused { get; init; }
    public bool IsSelected { get; init; }
    public ICommand OpenCommand { get; init; } = new RelayCommand(() => { });
}
