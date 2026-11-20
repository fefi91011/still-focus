using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Still.App.Controls;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class StatsViewModel : ObservableObject, IPageViewModel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly MainViewModel _main;
    private string _signature = "init";
    private string _focusToday = "0 Min";
    private string _focusWeek = "0 Min";
    private string _focusAverage = "0 Min";
    private string _screenTimeToday = "0 Min";
    private AppUsageSectionViewModel _appsToday = new();
    private AppUsageSectionViewModel _appsWeek = new();
    private string _appUsageSignature = "init";
    private string _usedTodaySummary = string.Empty;
    private string _blockedToday = "0";
    private string _tasksToday = "0";
    private string _rewardToday = "0 Min";
    private string _streak = "0 Tage";
    private bool _hasUsage;
    private bool _hasRules;
    private bool _hasCompletions;

    public StatsViewModel(MainViewModel main)
    {
        _main = main;
        Refresh();
    }

    public ObservableCollection<ChartBar> FocusBars { get; } = new();
    public ObservableCollection<UsageRowViewModel> UsageRows { get; } = new();
    public ObservableCollection<TaskCompletionRowViewModel> Completions { get; } = new();

    public string FocusToday
    {
        get => _focusToday;
        private set => Set(ref _focusToday, value);
    }

    public string FocusWeek
    {
        get => _focusWeek;
        private set => Set(ref _focusWeek, value);
    }

    public string FocusAverage
    {
        get => _focusAverage;
        private set => Set(ref _focusAverage, value);
    }

    public string ScreenTimeToday
    {
        get => _screenTimeToday;
        private set => Set(ref _screenTimeToday, value);
    }

    public AppUsageSectionViewModel AppsToday
    {
        get => _appsToday;
        private set => Set(ref _appsToday, value);
    }

    public AppUsageSectionViewModel AppsWeek
    {
        get => _appsWeek;
        private set => Set(ref _appsWeek, value);
    }

    public string UsedTodaySummary
    {
        get => _usedTodaySummary;
        private set => Set(ref _usedTodaySummary, value);
    }

    public string BlockedToday
    {
        get => _blockedToday;
        private set => Set(ref _blockedToday, value);
    }

    public string TasksToday
    {
        get => _tasksToday;
        private set => Set(ref _tasksToday, value);
    }

    public string RewardToday
    {
        get => _rewardToday;
        private set => Set(ref _rewardToday, value);
    }

    public string Streak
    {
        get => _streak;
        private set => Set(ref _streak, value);
    }

    public bool HasUsage
    {
        get => _hasUsage;
        private set => Set(ref _hasUsage, value);
    }

    public bool HasRules
    {
        get => _hasRules;
        private set => Set(ref _hasRules, value);
    }

    public bool HasCompletions
    {
        get => _hasCompletions;
        private set => Set(ref _hasCompletions, value);
    }

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var snapshot = _main.Runtime.Snapshot;
        var today = DateOnly.FromDateTime(DateTime.Now);
        UpdateAppUsage(state, today);

        var sig = string.Join("|", state.Days.Select(d => $"{d.Date}:{d.FocusMinutes}:{d.ScreenSeconds}:{d.BlockedAttempts}:{d.TasksCompleted}:{d.TaskRewardMinutes}"))
                  + "§" + string.Join("|", state.Usage.Select(u => $"{u.RuleId}:{u.Date}:{u.UsedSeconds}:{u.BonusMinutes}:{u.GraceBonusMinutes}"))
                  + "§" + string.Join("|", snapshot.Rules.Select(r => $"{r.RuleId}:{r.Blocked}:{r.Armed}:{r.RemainingMinutes}:{r.UsedMinutes}"))
                  + "§" + string.Join("|", state.Completions.Where(c => DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today).Select(c => c.Id))
                  + "§" + string.Join("|", state.Rules.Select(r => $"{r.Id}:{r.Name}:{r.Enabled}:{r.DailyLimitMinutes}"));

        if (sig == _signature)
        {
            return;
        }

        _signature = sig;

        var todayStat = state.Days.FirstOrDefault(d => d.Date == today);
        FocusToday = FormatMinutes(todayStat?.FocusMinutes ?? 0);
        var weekMinutes = Enumerable.Range(0, 7)
            .Select(offset => state.Days.FirstOrDefault(d => d.Date == today.AddDays(-offset))?.FocusMinutes ?? 0)
            .Sum();
        FocusWeek = FormatMinutes(weekMinutes);
        FocusAverage = FormatMinutes((int)Math.Round(weekMinutes / 7.0));
        BlockedToday = (todayStat?.BlockedAttempts ?? 0).ToString(German);
        TasksToday = (todayStat?.TasksCompleted ?? 0).ToString(German);
        RewardToday = FormatMinutes(todayStat?.TaskRewardMinutes ?? 0);
        ScreenTimeToday = FormatSeconds(todayStat?.ScreenSeconds ?? 0);

        var streak = 0;
        var cursor = today;
        if ((state.Days.FirstOrDefault(d => d.Date == today)?.FocusMinutes ?? 0) == 0)
        {
            cursor = today.AddDays(-1);
        }

        while (true)
        {
            var day = state.Days.FirstOrDefault(d => d.Date == cursor);
            if (day is null || day.FocusMinutes <= 0)
            {
                break;
            }

            streak++;
            cursor = cursor.AddDays(-1);
        }

        Streak = streak == 1 ? "1 Tag" : $"{streak} Tage";

        FocusBars.Clear();
        for (var offset = -6; offset <= 0; offset++)
        {
            var date = today.AddDays(offset);
            var minutes = state.Days.FirstOrDefault(d => d.Date == date)?.FocusMinutes ?? 0;
            FocusBars.Add(new ChartBar
            {
                Label = offset == 0 ? "Heute" : Schedule.ShortDay(date.DayOfWeek),
                Value = minutes,
                ValueLabel = minutes > 0 ? minutes.ToString() : "–",
                Highlight = offset == 0
            });
        }

        UsageRows.Clear();
        var todayUsage = state.Usage.Where(u => u.Date == today).ToList();
        var limitedRules = state.Rules.Where(r => r.Enabled && r.DailyLimitMinutes is > 0).ToList();
        if (limitedRules.Count == 0)
        {
            UsedTodaySummary = string.Empty;
        }
        else
        {
            var usedSeconds = limitedRules.Sum(r => todayUsage.FirstOrDefault(u => u.RuleId == r.Id)?.UsedSeconds ?? 0);
            var allowanceSeconds = limitedRules.Sum(r =>
            {
                var usage = todayUsage.FirstOrDefault(u => u.RuleId == r.Id);
                return (r.DailyLimitMinutes!.Value + (usage?.BonusMinutes ?? 0) + (usage?.GraceBonusMinutes ?? 0)) * 60;
            });
            UsedTodaySummary =
                $"{FormatSeconds(usedSeconds)} von {FormatSeconds(allowanceSeconds)} Regelbudget verbraucht · " +
                $"{FormatSeconds(Math.Max(0, allowanceSeconds - usedSeconds))} übrig";
        }

        foreach (var rule in state.Rules.Where(r => r.Enabled && r.DailyLimitMinutes is > 0).OrderBy(r => r.Name))
        {
            var usage = todayUsage.FirstOrDefault(u => u.RuleId == rule.Id);
            var status = snapshot.FindRule(rule.Id);
            var limit = rule.DailyLimitMinutes is int value && value > 0 ? value : (int?)null;
            var usedSeconds = usage?.UsedSeconds ?? 0;
            var allowance = limit is null ? 0 : (limit.Value + (usage?.BonusMinutes ?? 0) + (usage?.GraceBonusMinutes ?? 0)) * 60;
            var fraction = allowance <= 0 ? (status?.Blocked == true ? 1 : 0) : Math.Clamp((double)usedSeconds / allowance, 0, 1);

            UsageRows.Add(new UsageRowViewModel
            {
                Name = rule.Name,
                UsedText = $"{Math.Round(usedSeconds / 60.0)} Min",
                LimitText = limit is null ? "Ohne Limit" : $"von {limit + (usage?.BonusMinutes ?? 0) + (usage?.GraceBonusMinutes ?? 0)} Min",
                Fraction = fraction,
                IsBlocked = status?.Blocked == true,
                BarBrush = status?.Blocked == true ? UiBrushes.Danger : UiBrushes.Accent
            });
        }

        HasUsage = UsageRows.Count > 0;
        HasRules = state.Rules.Count > 0;

        Completions.Clear();
        HasCompletions = state.Completions.Any(c => DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today);
        foreach (var completion in state.Completions
                     .Where(c => DateOnly.FromDateTime(c.CompletedAt.LocalDateTime) == today)
                     .OrderByDescending(c => c.CompletedAt))
        {
            Completions.Add(new TaskCompletionRowViewModel
            {
                Title = completion.TaskTitle,
                TimeText = completion.CompletedAt.LocalDateTime.ToString("HH:mm", German),
                MinutesText = $"+{completion.MinutesGranted} Min",
                Reflection = completion.Reflection ?? string.Empty,
                HasReflection = !string.IsNullOrWhiteSpace(completion.Reflection)
            });
        }
    }

    private void UpdateAppUsage(AppState state, DateOnly today)
    {
        var signature = string.Join("|", state.AppUsage
            .Where(entry => entry.Date >= today.AddDays(-6) && entry.Date <= today)
            .Select(entry => $"{entry.Date}:{entry.ProcessName}:{entry.UsedSeconds / 10}"));

        if (signature == _appUsageSignature)
        {
            return;
        }

        _appUsageSignature = signature;
        AppsToday = AppUsageSectionViewModel.Create(
            state.AppUsage,
            today,
            today,
            "Heute",
            0,
            "Noch keine App-Zeit erfasst.");
        AppsWeek = AppUsageSectionViewModel.Create(
            state.AppUsage,
            today.AddDays(-6),
            today,
            "Letzte 7 Tage",
            0,
            "Noch keine App-Zeit in den letzten 7 Tagen.");
    }

    private static string FormatMinutes(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} Min";
        }

        return $"{minutes / 60} Std {minutes % 60:00} Min";
    }

    private static string FormatSeconds(int seconds)
    {
        var minutes = (int)Math.Round(Math.Max(0, seconds) / 60.0);
        return minutes < 60
            ? $"{minutes} Min"
            : $"{minutes / 60}:{minutes % 60:00} h";
    }
}

public sealed class UsageRowViewModel
{
    public string Name { get; init; } = string.Empty;
    public string UsedText { get; init; } = string.Empty;
    public string LimitText { get; init; } = string.Empty;
    public double Fraction { get; init; }
    public bool IsBlocked { get; init; }
    public IBrush BarBrush { get; init; } = UiBrushes.Accent;
}
