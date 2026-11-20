using System.Windows.Input;
using Still.Core.Runtime;

namespace Still.App.ViewModels;

public sealed class BlockedWindowViewModel
{
    public BlockedWindowViewModel(
        BlockNotice notice,
        Action close,
        Action<Guid> grantGrace,
        Action contact)
    {
        Subject = notice.Subject;
        KindLabel = notice.KindLabel;
        RulesText = notice.RuleNames.Count == 0
            ? "Eine Regel greift gerade."
            : "Regel: " + string.Join(", ", notice.RuleNames);
        Explanation = notice.GraceUsesLeft switch
        {
            > 1 => $"Noch {notice.GraceUsesLeft} Nachfristen heute verfügbar.",
            1 => "Noch 1 Nachfrist heute verfügbar.",
            _ => "Keine Nachfrist mehr übrig. Die Sperre bleibt."
        };
        GraceLabel = $"+{notice.GraceMinutes} Minuten";
        CanGrace = notice.GraceRuleId is not null && notice.GraceMinutes > 0;
        CloseLabel = notice.KindLabel.Contains("Website", StringComparison.OrdinalIgnoreCase)
            ? "Tab schließen"
            : "App schließen";
        ContactLabel = "Kontaktpersonen kontaktieren";
        CloseCommand = new RelayCommand(close);
        ContactCommand = new RelayCommand(contact);
        GraceCommand = new RelayCommand(() =>
        {
            if (notice.GraceRuleId is Guid ruleId)
            {
                grantGrace(ruleId);
            }
        });
    }

    public string Subject { get; }
    public string KindLabel { get; }
    public string RulesText { get; }
    public string Explanation { get; }
    public string GraceLabel { get; }
    public string CloseLabel { get; }
    public string ContactLabel { get; }
    public bool CanGrace { get; }
    public ICommand CloseCommand { get; }
    public ICommand ContactCommand { get; }
    public ICommand GraceCommand { get; }
}
