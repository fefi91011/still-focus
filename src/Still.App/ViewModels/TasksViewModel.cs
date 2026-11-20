using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class TaskRuleOption
{
    public Guid? RuleId { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class TasksViewModel : ObservableObject, IPageViewModel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly MainViewModel _main;
    private string _signature = "init";
    private string _summaryText = string.Empty;
    private bool _empty = true;
    private bool _hasCompletions;

    public TasksViewModel(MainViewModel main)
    {
        _main = main;
        Editor = new TaskEditorViewModel(this, main);
        NewTaskCommand = new RelayCommand(OpenNewTask);
        Refresh();
    }

    public ObservableCollection<TaskCardViewModel> Tasks { get; } = new();
    public ObservableCollection<TaskCompletionRowViewModel> CompletionsToday { get; } = new();
    public ObservableCollection<TaskRuleOption> RuleOptions { get; } = new();
    public TaskEditorViewModel Editor { get; }
    public ICommand NewTaskCommand { get; }

    public string SummaryText
    {
        get => _summaryText;
        private set => Set(ref _summaryText, value);
    }

    public bool Empty
    {
        get => _empty;
        private set => Set(ref _empty, value);
    }

    public bool HasCompletions
    {
        get => _hasCompletions;
        private set => Set(ref _hasCompletions, value);
    }

    public bool IsEditing => Editor.IsOpen;

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var today = DateOnly.FromDateTime(DateTime.Now);

        var sig = string.Join("|", state.Tasks.Select(t =>
                      $"{t.Id}:{t.Title}:{t.Details}:{t.RewardMinutes}:{t.MaxPerDay}:{t.RuleId}:{t.RequireReflection}:{t.Enabled}"))
                  + "§" + string.Join("|", state.Completions.Where(c =>
                      DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today).Select(c =>
                      $"{c.Id}:{c.TaskId}:{c.CompletedAt:HH:mm}:{c.MinutesGranted}"))
                  + "§" + string.Join("|", state.Rules.Select(r => $"{r.Id}:{r.Name}:{r.DailyLimitMinutes}"))
                  + $"§{Editor.IsOpen}:{Editor.Id}";

        if (sig == _signature)
        {
            return;
        }

        _signature = sig;

        var selectedRuleId = Editor.SelectedRule?.RuleId;
        RuleOptions.Clear();
        RuleOptions.Add(new TaskRuleOption { RuleId = null, Name = "Alle Regeln mit Tageslimit" });
        foreach (var rule in state.Rules.Where(r => r.DailyLimitMinutes is > 0).OrderBy(r => r.Name))
        {
            RuleOptions.Add(new TaskRuleOption { RuleId = rule.Id, Name = rule.Name });
        }

        if (Editor.IsOpen)
        {
            Editor.ReselectRule(selectedRuleId);
        }

        Tasks.Clear();
        foreach (var task in state.Tasks.OrderBy(t => t.Title))
        {
            var done = state.Completions.Count(c =>
                c.TaskId == task.Id && DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today);
            var ruleName = task.RuleId is Guid ruleId
                ? state.Rules.FirstOrDefault(r => r.Id == ruleId)?.Name ?? "Gelöschte Regel"
                : "Alle Regeln mit Tageslimit";

            Tasks.Add(new TaskCardViewModel
            {
                Id = task.Id,
                Title = task.Title,
                Details = task.Details ?? string.Empty,
                HasDetails = !string.IsNullOrWhiteSpace(task.Details),
                RewardText = $"+{task.RewardMinutes} Min",
                TargetText = ruleName,
                ProgressText = $"{done} / {task.MaxPerDay} heute",
                IsPaused = !task.Enabled,
                IsReflection = task.RequireReflection,
                CanComplete = task.Enabled && done < task.MaxPerDay,
                CompleteCommand = new AsyncRelayCommand(
                    () => _main.CompleteTaskAsync(task.Id, task.Title, task.RequireReflection),
                    ex => _main.ShowToast(ex.Message, error: true)),
                EditCommand = new RelayCommand(() => OpenTask(task.Id))
            });
        }

        CompletionsToday.Clear();
        foreach (var completion in state.Completions
                     .Where(c => DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today)
                     .OrderByDescending(c => c.CompletedAt))
        {
            CompletionsToday.Add(new TaskCompletionRowViewModel
            {
                Title = completion.TaskTitle,
                TimeText = completion.CompletedAt.LocalDateTime.ToString("HH:mm", German),
                MinutesText = $"+{completion.MinutesGranted} Min",
                Reflection = completion.Reflection ?? string.Empty,
                HasReflection = !string.IsNullOrWhiteSpace(completion.Reflection)
            });
        }

        HasCompletions = CompletionsToday.Count > 0;
        var available = state.Tasks.Count(t => t.Enabled);
        var doneToday = CompletionsToday.Count;
        SummaryText = available == 0
            ? "Noch keine Aufgaben"
            : $"{available} Aufgaben · {doneToday} heute erledigt";
        Empty = state.Tasks.Count == 0;

        Raise(nameof(IsEditing));
    }

    internal async Task SaveTaskAsync(FocusTask task)
    {
        var approved = await _main.EnsureApprovalAsync("diese Aufgabe");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.SaveTask(task, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task DeleteTaskAsync(Guid taskId, string title)
    {
        if (!await _main.ConfirmAsync("Aufgabe löschen", $"„{title}“ wirklich löschen?", "Löschen"))
        {
            return;
        }

        var approved = await _main.EnsureApprovalAsync("das Löschen dieser Aufgabe");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.DeleteTask(taskId, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    private void OpenNewTask()
    {
        Editor.Open(new FocusTask { Title = string.Empty, RewardMinutes = 15, MaxPerDay = 1 }, true);
        Refresh();
    }

    private void OpenTask(Guid taskId)
    {
        var task = _main.Runtime.State.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            return;
        }

        Editor.Open(task.Clone(), false);
        Refresh();
    }
}

public sealed class TaskCardViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public bool HasDetails { get; init; }
    public string RewardText { get; init; } = string.Empty;
    public string TargetText { get; init; } = string.Empty;
    public string ProgressText { get; init; } = string.Empty;
    public bool IsPaused { get; init; }
    public bool IsReflection { get; init; }
    public bool CanComplete { get; init; }
    public ICommand CompleteCommand { get; init; } = new RelayCommand(() => { });
    public ICommand EditCommand { get; init; } = new RelayCommand(() => { });
}

public sealed class TaskCompletionRowViewModel
{
    public string Title { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public string MinutesText { get; init; } = string.Empty;
    public string Reflection { get; init; } = string.Empty;
    public bool HasReflection { get; init; }
}

public sealed class TaskEditorViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly TasksViewModel _owner;
    private readonly MainViewModel _main;

    private bool _isOpen;
    private bool _isNew;
    private Guid _id;
    private DateTimeOffset _createdAt = DateTimeOffset.Now;
    private string _title = string.Empty;
    private string _details = string.Empty;
    private string _rewardText = "15";
    private string _maxPerDayText = "1";
    private TaskRuleOption? _selectedRule;
    private bool _requireReflection;
    private bool _enabled = true;
    private string? _errorText;

    public TaskEditorViewModel(TasksViewModel owner, MainViewModel main)
    {
        _owner = owner;
        _main = main;
        SaveCommand = new AsyncRelayCommand(SaveAsync, ex => SetError(ex.Message));
        CancelCommand = new RelayCommand(Close);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, ex => SetError(ex.Message));
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (Set(ref _isOpen, value))
            {
                Raise(nameof(IsVisible));
            }
        }
    }

    public bool IsVisible => IsOpen;

    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (Set(ref _isNew, value))
            {
                Raise(nameof(CanDelete));
                Raise(nameof(EditorTitle));
            }
        }
    }

    public Guid Id
    {
        get => _id;
        private set => Set(ref _id, value);
    }

    public string EditorTitle => IsNew ? "Neue Aufgabe" : "Aufgabe bearbeiten";
    public bool CanDelete => !IsNew;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Details
    {
        get => _details;
        set => Set(ref _details, value);
    }

    public string RewardText
    {
        get => _rewardText;
        set => Set(ref _rewardText, value);
    }

    public string MaxPerDayText
    {
        get => _maxPerDayText;
        set => Set(ref _maxPerDayText, value);
    }

    public TaskRuleOption? SelectedRule
    {
        get => _selectedRule;
        set => Set(ref _selectedRule, value);
    }

    public bool RequireReflection
    {
        get => _requireReflection;
        set => Set(ref _requireReflection, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (Set(ref _errorText, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public void Open(FocusTask task, bool isNew)
    {
        Id = task.Id;
        _createdAt = task.CreatedAt == default ? DateTimeOffset.Now : task.CreatedAt;
        IsNew = isNew;
        Title = task.Title;
        Details = task.Details ?? string.Empty;
        RewardText = task.RewardMinutes.ToString(German);
        MaxPerDayText = task.MaxPerDay.ToString(German);
        RequireReflection = task.RequireReflection;
        Enabled = task.Enabled;
        SelectedRule = _owner.RuleOptions.FirstOrDefault(o => o.RuleId == task.RuleId) ?? _owner.RuleOptions.FirstOrDefault();
        ErrorText = null;
        IsOpen = true;
        Raise(nameof(EditorTitle));
        Raise(nameof(CanDelete));
    }

    public void Close()
    {
        IsOpen = false;
        ErrorText = null;
        _owner.Refresh();
    }

    public void SetError(string? message) => ErrorText = message;

    internal void ReselectRule(Guid? ruleId) =>
        SelectedRule = _owner.RuleOptions.FirstOrDefault(o => o.RuleId == ruleId)
                       ?? _owner.RuleOptions.FirstOrDefault();

    private async Task SaveAsync()
    {
        ErrorText = null;
        var task = Build();
        if (task is null)
        {
            return;
        }

        await _owner.SaveTaskAsync(task);
    }

    private async Task DeleteAsync()
    {
        if (!IsNew)
        {
            await _owner.DeleteTaskAsync(Id, Title);
        }
    }

    private FocusTask? Build()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Title))
        {
            errors.Add("Bitte gib der Aufgabe einen Titel.");
        }

        if (!int.TryParse(RewardText.Trim(), NumberStyles.Integer, German, out var reward) || reward is < 1 or > 240)
        {
            errors.Add("Die Belohnung muss zwischen 1 und 240 Minuten liegen.");
            reward = 15;
        }

        if (!int.TryParse(MaxPerDayText.Trim(), NumberStyles.Integer, German, out var max) || max is < 1 or > 20)
        {
            errors.Add("Erledigungen pro Tag: 1 bis 20.");
            max = 1;
        }

        if (errors.Count > 0)
        {
            SetError(string.Join(" ", errors));
            return null;
        }

        return new FocusTask
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            Title = Title.Trim(),
            Details = string.IsNullOrWhiteSpace(Details) ? null : Details.Trim(),
            RewardMinutes = reward,
            MaxPerDay = max,
            RuleId = SelectedRule?.RuleId,
            RequireReflection = RequireReflection,
            Enabled = Enabled,
            CreatedAt = _createdAt
        };
    }
}
