using Still.Core.Models;

namespace Still.Core.Engine;

/// <summary>
/// Indexed per-day, per-process foreground time. Kept separate from rule usage:
/// this tracks every application, including apps without a rule or daily limit.
/// </summary>
public sealed class AppUsageBook
{
    private readonly AppState _state;
    private readonly Dictionary<(DateOnly Date, string Process), AppUsageEntry> _index = new();

    public AppUsageBook(AppState state)
    {
        _state = state;
        Rebuild();
    }

    public void Rebuild()
    {
        _index.Clear();
        foreach (var entry in _state.AppUsage)
        {
            var process = Target.NormalizeProcess(entry.ProcessName);
            if (process.Length == 0)
            {
                continue;
            }

            entry.ProcessName = process;
            _index[(entry.Date, process)] = entry;
        }
    }

    public AppUsageEntry For(DateOnly date, string processName)
    {
        var process = Target.NormalizeProcess(processName);
        var key = (date, process);
        if (_index.TryGetValue(key, out var entry))
        {
            return entry;
        }

        entry = new AppUsageEntry
        {
            Date = date,
            ProcessName = process
        };
        _state.AppUsage.Add(entry);
        _index[key] = entry;
        return entry;
    }
}
