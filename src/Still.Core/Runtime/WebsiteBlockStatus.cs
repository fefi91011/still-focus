namespace Still.Core.Runtime;

/// <summary>Current website block state for the browser extension bridge.</summary>
public sealed record WebsiteBlockStatus(
    bool Blocked,
    string Host,
    string Subject,
    string KindLabel,
    IReadOnlyList<string> RuleNames,
    Guid? GraceRuleId,
    int GraceMinutes,
    int GraceUsesLeft,
    string Explanation,
    bool Lockdown)
{
    public bool GraceAvailable => GraceRuleId is not null && GraceMinutes > 0;
}