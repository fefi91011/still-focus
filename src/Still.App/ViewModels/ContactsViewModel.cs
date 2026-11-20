using System.Collections.ObjectModel;
using System.Windows.Input;
using Still.Core.Models;

namespace Still.App.ViewModels;

public sealed class ContactsViewModel : ObservableObject, IPageViewModel
{
    private readonly MainViewModel _main;
    private string _signature = "init";
    private bool _empty = true;

    public ContactsViewModel(MainViewModel main)
    {
        _main = main;
        Editor = new ContactEditorViewModel(this, main);
        NewContactCommand = new RelayCommand(OpenNew);
        Refresh();
    }

    public ObservableCollection<ContactCardViewModel> Contacts { get; } = new();
    public ContactEditorViewModel Editor { get; }
    public ICommand NewContactCommand { get; }

    public bool Empty
    {
        get => _empty;
        private set => Set(ref _empty, value);
    }

    public bool IsEditing => Editor.IsOpen;
    public string SummaryText => Contacts.Count == 0
        ? "Noch keine Kontakte"
        : $"{Contacts.Count} Kontakte · {Contacts.Count(c => c.IsApprover)} " +
          (Contacts.Count(c => c.IsApprover) == 1 ? "Freigabeperson" : "Freigabepersonen");

    public void Refresh()
    {
        var state = _main.Runtime.State;
        var sig = string.Join("|", state.Contacts.Select(c =>
            $"{c.Id}:{c.Name}:{c.Email}:{c.Phone}:{c.IsApprover}:{c.HasCode}"))
            + $"§{Editor.IsOpen}:{Editor.Id}§{state.Settings.RequireApprovalForEdits}";

        if (sig == _signature)
        {
            return;
        }

        _signature = sig;

        Contacts.Clear();
        foreach (var contact in state.Contacts.OrderByDescending(c => c.IsApprover).ThenBy(c => c.Name))
        {
            var contactLine = new[] { contact.Email, contact.Phone }
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? contact.Note ?? "Keine Kontaktdaten";
            var initial = string.IsNullOrWhiteSpace(contact.Name)
                ? "?"
                : contact.Name.Trim()[0].ToString().ToUpperInvariant();

            Contacts.Add(new ContactCardViewModel
            {
                Id = contact.Id,
                Name = contact.Name,
                Initial = initial,
                RoleText = contact.IsApprover ? "Freigabeperson" : "Kontakt",
                IsApprover = contact.IsApprover,
                HasCode = contact.HasCode,
                CodeText = contact.HasCode ? "Freigabecode gesetzt" : "Kein Freigabecode",
                ContactLine = contactLine,
                MailCommand = new RelayCommand(() => RequestApproval(contact)),
                EditCommand = new RelayCommand(() => Open(contact.Id))
            });
        }

        Empty = Contacts.Count == 0;
        Raise(nameof(IsEditing));
        Raise(nameof(SummaryText));
    }

    internal async Task SaveContactAsync(Contact contact, string? code)
    {
        var approved = await _main.EnsureApprovalAsync("diesen Kontakt");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.SaveContact(contact, code, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    internal async Task DeleteContactAsync(Guid contactId, string name)
    {
        if (!await _main.ConfirmAsync("Kontakt entfernen", $"„{name}“ wirklich entfernen?", "Entfernen"))
        {
            return;
        }

        var approved = await _main.EnsureApprovalAsync("das Entfernen dieses Kontakts");
        if (!approved)
        {
            return;
        }

        var result = _main.Runtime.DeleteContact(contactId, approved: true);
        if (!result.Ok)
        {
            Editor.SetError(result.Message);
            return;
        }

        Editor.Close();
        _main.Notify(result);
        Refresh();
    }

    private void RequestApproval(Contact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.Email))
        {
            _main.ShowToast("Hinterlege zuerst eine E-Mail-Adresse für die Freigabeanfrage.", error: true);
            return;
        }

        var subject = Uri.EscapeDataString("Freigabe für meine Fokus-App");
        var body = Uri.EscapeDataString(
            $"Hey {contact.Name},\n\n" +
            "ich nutze Still, um fokussierter zu arbeiten. " +
            "Kannst du mir kurz einen Freigabecode schicken, wenn ich eine Sperre wirklich ändern muss?\n\n" +
            "Danke dir!");
        _main.OpenExternal($"mailto:{contact.Email}?subject={subject}&body={body}");
    }

    private void OpenNew()
    {
        Editor.Open(new Contact { Name = string.Empty }, true);
        Refresh();
    }

    private void Open(Guid contactId)
    {
        var contact = _main.Runtime.State.Contacts.FirstOrDefault(c => c.Id == contactId);
        if (contact is null)
        {
            return;
        }

        Editor.Open(contact.Clone(), false);
        Refresh();
    }
}

public sealed class ContactCardViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Initial { get; init; } = "?";
    public string RoleText { get; init; } = string.Empty;
    public bool IsApprover { get; init; }
    public bool HasCode { get; init; }
    public string CodeText { get; init; } = string.Empty;
    public string ContactLine { get; init; } = string.Empty;
    public ICommand MailCommand { get; init; } = new RelayCommand(() => { });
    public ICommand EditCommand { get; init; } = new RelayCommand(() => { });
}

public sealed class ContactEditorViewModel : ObservableObject
{
    private readonly ContactsViewModel _owner;
    private readonly MainViewModel _main;

    private bool _isOpen;
    private bool _isNew;
    private Guid _id;
    private DateTimeOffset _createdAt = DateTimeOffset.Now;
    private string _name = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _note = string.Empty;
    private bool _isApprover;
    private string _code = string.Empty;
    private string? _existingHash;
    private string? _existingSalt;
    private string? _errorText;

    public ContactEditorViewModel(ContactsViewModel owner, MainViewModel main)
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

    public string EditorTitle => IsNew ? "Neuer Kontakt" : "Kontakt bearbeiten";
    public bool CanDelete => !IsNew;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string Email
    {
        get => _email;
        set => Set(ref _email, value);
    }

    public string Phone
    {
        get => _phone;
        set => Set(ref _phone, value);
    }

    public string Note
    {
        get => _note;
        set => Set(ref _note, value);
    }

    public bool IsApprover
    {
        get => _isApprover;
        set
        {
            if (Set(ref _isApprover, value))
            {
                Raise(nameof(CodeHint));
            }
        }
    }

    public string Code
    {
        get => _code;
        set => Set(ref _code, value);
    }

    public string CodeHint => _existingHash is not null
        ? "Der Code ist gesetzt. Ein neuer Code ersetzt ihn."
        : "Die Person sollte diesen Code selbst vergeben. Du brauchst ihn für Freigaben.";

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

    public void Open(Contact contact, bool isNew)
    {
        Id = contact.Id;
        _createdAt = contact.CreatedAt == default ? DateTimeOffset.Now : contact.CreatedAt;
        _existingHash = contact.CodeHash;
        _existingSalt = contact.CodeSalt;
        IsNew = isNew;
        Name = contact.Name;
        Email = contact.Email ?? string.Empty;
        Phone = contact.Phone ?? string.Empty;
        Note = contact.Note ?? string.Empty;
        IsApprover = contact.IsApprover;
        Code = string.Empty;
        ErrorText = null;
        IsOpen = true;
        Raise(nameof(CodeHint));
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

    private async Task SaveAsync()
    {
        ErrorText = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            SetError("Bitte gib einen Namen ein.");
            return;
        }

        var contact = new Contact
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            Name = Name.Trim(),
            Email = Email.Trim(),
            Phone = Phone.Trim(),
            Note = Note.Trim(),
            IsApprover = IsApprover,
            CodeHash = _existingHash,
            CodeSalt = _existingSalt,
            CreatedAt = _createdAt
        };

        await _owner.SaveContactAsync(contact, string.IsNullOrWhiteSpace(Code) ? null : Code);
    }

    private async Task DeleteAsync()
    {
        if (!IsNew)
        {
            await _owner.DeleteContactAsync(Id, Name);
        }
    }
}
