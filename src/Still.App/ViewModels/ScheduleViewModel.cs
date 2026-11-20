using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using Still.App.Controls;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class ScheduleViewModel : ObservableObject, IPageViewModel
{
    private readonly MainViewModel _main;
    private string _signature = "init";
    private IReadOnlyList<ScheduleBlock> _blocks = Array.Empty<ScheduleBlock>();
    private string _activeNowText = string.Empty;
    private string _weekHint = "Zeitfenster von Montag bis Sonntag. Übernacht-Sperren werden auf beide Tage verteilt.";
    private bool _hasWeekly;

    public ScheduleViewModel(MainViewModel main)
    {
        _main = main;
        OpenRulesCommand = new RelayCommand(() => main.Navigate("rules"));
        Refresh();
    }

    public ObservableCollection<WeekLegendRowViewModel> Legend { get; } = new();
    public ObservableCollection<ExceptionRowViewModel> Exceptions { get; } = new();
    public ICommand OpenRulesCommand { get; }

    public IReadOnlyList<ScheduleBlock> Blocks
    {
        get => _blocks;
        private set => Set(ref _blocks, value);
    }

    public string ActiveNowText
    {
        get => _activeNowText;
        private set => Set(ref _activeNowText, value);
    }

    public string WeekHint
    {
        get => _weekHint;
        private set => Set(ref _weekHint, value);
    }

    public bool HasWeekly
    {
        get => _hasWeekly;
        private set => Set(ref _hasWeekly, value);
    }

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var snapshot = _main.Runtime.Snapshot;
        var now = DateTimeOffset.Now;

        var sig = string.Join("|", state.Rules.Select(r =>
                      $"{r.Id}:{r.Name}:{r.Enabled}:{r.Schedule.Mode}:{r.Schedule.Describe()}"))
                  + "§" + string.Join("|", snapshot.Rules.Select(r => $"{r.RuleId}:{r.Armed}"))
                  + "§" + string.Join("|", state.Allowances.Select(a => $"{a.Id}:{a.Name}:{a.IsActiveAt(now)}"));

        if (sig == _signature)
        {
            return;
        }

        _signature = sig;

        var blocks = new List<ScheduleBlock>();
        Legend.Clear();

        var enabled = state.Rules.Where(r => r.Enabled).ToList();
        foreach (var rule in enabled)
        {
            var color = UiBrushes.ColorForRule(rule.Id);
            var status = snapshot.FindRule(rule.Id);
            var targets = string.Join(" · ", rule.DistinctTargets().Select(t => t.Label ?? t.Value).Distinct().Take(3));

            Legend.Add(new WeekLegendRowViewModel
            {
                Name = rule.Name,
                ScheduleText = rule.Schedule.Describe(),
                TargetsText = targets,
                Color = Brush.Parse(color),
                IsActive = status?.Armed == true,
                IsAlways = rule.Schedule.Mode == ScheduleMode.Always
            });

            if (rule.Schedule.Mode != ScheduleMode.Weekly)
            {
                continue;
            }

            foreach (var window in rule.Schedule.Windows)
            {
                AddWindow(blocks, rule.Name, color, window);
            }
        }

        Blocks = blocks;
        HasWeekly = blocks.Count > 0;
        var activeNow = snapshot.Rules.Count(r => r.Enabled && r.ScheduleActive);
        ActiveNowText = activeNow == 0
            ? "Gerade greift kein Zeitplan"
            : activeNow == 1 ? "1 Zeitplan greift gerade" : $"{activeNow} Zeitpläne greifen gerade";

        Exceptions.Clear();
        foreach (var allowance in state.Allowances.Where(a => a.Enabled).OrderByDescending(a => a.IsActiveAt(now)))
        {
            Exceptions.Add(new ExceptionRowViewModel(
                allowance.Name,
                string.Join(" · ", allowance.Targets.Select(t => t.Label ?? t.Value).Distinct().Take(3)),
                allowance.IsActiveAt(now)
                    ? allowance.ExpiresAt is null ? "Jetzt aktiv" : $"Aktiv bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}"
                    : allowance.ExpiresAt is null ? "Gerade nicht aktiv" : $"Inaktiv · bis {allowance.ExpiresAt.Value.LocalDateTime:dd.MM.}",
                allowance.IsActiveAt(now)));
        }
    }

    private static void AddWindow(List<ScheduleBlock> blocks, string title, string color, TimeWindow window)
    {
        var dayIndex = ((int)window.Day + 6) % 7;
        var start = window.Start.Hour + window.Start.Minute / 60.0;
        var end = window.End.Hour + window.End.Minute / 60.0;
        var fullDay = window.Start == TimeOnly.MinValue && window.End == TimeOnly.MinValue;

        if (fullDay)
        {
            blocks.Add(new ScheduleBlock
            {
                DayIndex = dayIndex,
                StartHour = 0,
                EndHour = 24,
                Title = title,
                ColorHex = color
            });
            return;
        }

        if (window.End <= window.Start)
        {
            blocks.Add(new ScheduleBlock
            {
                DayIndex = dayIndex,
                StartHour = start,
                EndHour = 24,
                Title = title,
                ColorHex = color
            });

            if (end > 0)
            {
                blocks.Add(new ScheduleBlock
                {
                    DayIndex = (dayIndex + 1) % 7,
                    StartHour = 0,
                    EndHour = end,
                    Title = title,
                    ColorHex = color
                });
            }

            return;
        }

        blocks.Add(new ScheduleBlock
        {
            DayIndex = dayIndex,
            StartHour = start,
            EndHour = Math.Min(24, end),
            Title = title,
            ColorHex = color
        });
    }
}

public sealed class WeekLegendRowViewModel
{
    public string Name { get; init; } = string.Empty;
    public string ScheduleText { get; init; } = string.Empty;
    public string TargetsText { get; init; } = string.Empty;
    public IBrush Color { get; init; } = UiBrushes.Accent;
    public bool IsActive { get; init; }
    public bool IsAlways { get; init; }
}
