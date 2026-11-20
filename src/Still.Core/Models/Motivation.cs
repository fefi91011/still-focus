using System.Security.Cryptography;

namespace Still.Core.Models;

/// <summary>A task that can be completed for extra daily time.</summary>
public sealed class FocusTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Details { get; set; }
    public int RewardMinutes { get; set; } = 10;
    public int MaxPerDay { get; set; } = 1;
    public Guid? RuleId { get; set; }
    public bool RequireReflection { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public FocusTask Clone() => new()
    {
        Id = Id,
        Title = Title,
        Details = Details,
        RewardMinutes = RewardMinutes,
        MaxPerDay = MaxPerDay,
        RuleId = RuleId,
        RequireReflection = RequireReflection,
        Enabled = Enabled,
        CreatedAt = CreatedAt
    };
}

public sealed class TaskCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.Now;
    public string? Reflection { get; set; }
    public int MinutesGranted { get; set; }
}

public sealed class Contact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsApprover { get; set; }
    public string? CodeHash { get; set; }
    public string? CodeSalt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool HasCode => !string.IsNullOrEmpty(CodeHash) && !string.IsNullOrEmpty(CodeSalt);

    public Contact Clone() => new()
    {
        Id = Id,
        Name = Name,
        Note = Note,
        Email = Email,
        Phone = Phone,
        IsApprover = IsApprover,
        CodeHash = CodeHash,
        CodeSalt = CodeSalt,
        CreatedAt = CreatedAt
    };
}

public static class ApprovalCodes
{
    private const int Iterations = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static bool IsUsable(string? code) => !string.IsNullOrWhiteSpace(code) && code.Trim().Length >= 4;

    public static (string Hash, string Salt) Create(string code)
    {
        var normalized = code?.Trim() ?? string.Empty;
        if (normalized.Length < 4)
        {
            throw new ArgumentException("Der Code muss mindestens 4 Zeichen haben.", nameof(code));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(normalized, salt, Iterations, HashBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string? code, string? hash, string? salt)
    {
        if (!IsUsable(code) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
        {
            return false;
        }

        try
        {
            var normalized = code?.Trim() ?? string.Empty;
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(hash);
            var actual = Derive(normalized, saltBytes, Iterations, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string GenerateCode()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(4);
        var chars = bytes.Select(b => alphabet[b % alphabet.Length]).ToArray();
        return $"{new string(chars, 0, 2)}-{new string(chars, 2, 2)}";
    }

    private static byte[] Derive(string code, byte[] salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(
            code.Trim(),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            length);
}
