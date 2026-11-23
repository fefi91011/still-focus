using System.Globalization;
using System.Windows.Input;
using Still.Core.Models;

namespace Still.App.ViewModels;

/// <summary>
/// Exception editor. Mirrors the rule editor's target picking, but stays
/// shorter: name, what is allowed, until when, and whether it is active.
/// </summary>
public sealed class AllowanceEditorViewModel : ObservableObject
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly RulesViewModel _owner;

    private bool _isOpen;
    private bool _isNew;
    private Guid _id;
    private DateTimeOffset _createdAt = DateTimeOffset.Now;
    private string _name = string.Empty;
    private string _expiresText = string.Empty;
    private bool _enabled = true;
    private string? _note;
    private string? _errorText;

    public AllowanceEditorViewModel(RulesViewModel owner)
    {
        _owner = owner;
        Selection = new TargetSelectionViewModel(() => ErrorText = null, SetError);

        SaveCommand = new AsyncRelayCommand(SaveAsync, ex => SetError(ex.Message));
        CancelCommand = new RelayCommand(Close);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, ex => SetError(ex.Message));
    }

    public TargetSelectionViewModel Selection { get; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

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

    public string EditorTitle => IsNew ? "Neue Ausnahme" : "Ausnahme bearbeiten";
    public string EditorLead => "Hebt Sperren für die gewählten Ziele auf, solange die Ausnahme gilt.";
    public bool CanDelete => !IsNew;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string ExpiresText
    {
        get => _expiresText;
        set => Set(ref _expiresText, value);
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

    public void Open(Allowance allowance, bool isNew)
    {
        Id = allowance.Id;
        _createdAt = allowance.CreatedAt == default ? DateTimeOffset.Now : allowance.CreatedAt;
        _note = allowance.Note;
        IsNew = isNew;
        Name = allowance.Name;
        ExpiresText = allowance.ExpiresAt?.LocalDateTime.ToString("dd.MM.yyyy", German) ?? string.Empty;
        Enabled = allowance.Enabled;
        ErrorText = null;
        Selection.Load(allowance.Targets);
        IsOpen = true;
        Raise(nameof(EditorTitle));
        Raise(nameof(EditorLead));
        Raise(nameof(CanDelete));
    }

    public void Close()
    {
        IsOpen = false;
        ErrorText = null;
        _owner.Refresh();
    }

    public void SetError(string? message) => ErrorText = message;

    private async Task SaveAsync()
    {
        ErrorText = null;
        var allowance = Build();
        if (allowance is null)
        {
            return;
        }

        await _owner.SaveAllowanceAsync(allowance);
    }

    private async Task DeleteAsync()
    {
        if (!IsNew)
        {
            await _owner.DeleteAllowanceAsync(Id, Name);
        }
    }

    private Allowance? Build()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Bitte gib der Ausnahme einen Namen.");
        }

        if (Selection.Targets.Count == 0)
        {
            errors.Add("Füge mindestens ein Ziel hinzu, das erlaubt sein soll.");
        }

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(ExpiresText))
        {
            if (!TryParseDate(ExpiresText, out var date))
            {
                errors.Add("Das Ablaufdatum ist ungültig (z. B. 30.11.2026).");
            }
            else
            {
                var local = date.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified);
                expiresAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                if (expiresAt <= DateTimeOffset.Now)
                {
                    errors.Add("Das Ablaufdatum liegt in der Vergangenheit.");
                }
            }
        }

        var allowance = new Allowance
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            Name = Name.Trim(),
            Note = _note,
            Enabled = Enabled,
            Targets = Selection.Targets.Select(t => t.Target.Clone()).ToList(),
            Schedule = Schedule.Always(),
            ExpiresAt = expiresAt,
            CreatedAt = _createdAt
        };

        errors.AddRange(RuleValidator.Validate(allowance).Errors);

        if (errors.Count > 0)
        {
            SetError(string.Join(" ", errors.Distinct()));
            return null;
        }

        return allowance;
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParse(text.Trim(), German, DateTimeStyles.None, out date)
        || DateOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
