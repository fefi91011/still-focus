using Still.Core.Models;

namespace Still.Core.Engine;

public sealed class UsageBook
{
    private readonly AppState _state;
    private readonly Dictionary<(Guid RuleId, DateOnly Date), UsageEntry> _index = new();

    public UsageBook(AppState state)
    {
        _state = state;
        Rebuild();
    }

    public void Rebuild()
    {
        _index.Clear();
        foreach (var entry in _state.Usage)
        {
            _index[(entry.RuleId, entry.Date)] = entry;
        }
    }

    public UsageEntry For(Guid ruleId, DateOnly date)
    {
        var key = (ruleId, date);
        if (!_index.TryGetValue(key, out var entry))
        {
            entry = new UsageEntry { RuleId = ruleId, Date = date };
            _state.Usage.Add(entry);
            _index[key] = entry;
        }

        return entry;
    }

    public UsageEntry? Find(Guid ruleId, DateOnly date) =>
        _index.TryGetValue((ruleId, date), out var entry) ? entry : null;

    public IEnumerable<UsageEntry> ForDate(DateOnly date) => _state.Usage.Where(u => u.Date == date);
}
