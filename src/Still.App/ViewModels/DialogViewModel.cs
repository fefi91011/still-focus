namespace Still.App.ViewModels;

public sealed class DialogOptions
{
    public required string Title { get; init; }
    public string? Body { get; init; }
    public string ConfirmText { get; init; } = "Bestätigen";
    public string CancelText { get; init; } = "Abbrechen";
    public string? InputLabel { get; init; }
    public string? InputPlaceholder { get; init; }
    public bool IsPassword { get; init; }
    public bool IsMultiline { get; init; }
    public string? InitialInput { get; init; }
    public string? Hint { get; init; }
    public Func<string?, string?>? Validate { get; init; }
    public bool ShowCancel { get; init; } = true;
}

public sealed record DialogResult(bool Confirmed, string Input);

public sealed class DialogViewModel : ObservableObject
{
    private TaskCompletionSource<DialogResult?>? _pending;
    private Func<string?, string?>? _validate;
    private bool _isOpen;
    private string _title = string.Empty;
    private string _body = string.Empty;
    private string _confirmText = "Bestätigen";
    private string _cancelText = "Abbrechen";
    private string? _inputLabel;
    private string? _inputPlaceholder;
    private string _input = string.Empty;
    private string? _hint;
    private string? _error;
    private bool _isPassword;
    private bool _isMultiline;
    private bool _showCancel = true;

    public DialogViewModel()
    {
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (Set(ref _isOpen, value))
            {
                Raise(nameof(ShowInput));
            }
        }
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Body
    {
        get => _body;
        private set
        {
            if (Set(ref _body, value))
            {
                Raise(nameof(HasBody));
            }
        }
    }

    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public string ConfirmText
    {
        get => _confirmText;
        private set => Set(ref _confirmText, value);
    }

    public string CancelText
    {
        get => _cancelText;
        private set => Set(ref _cancelText, value);
    }

    public bool ShowCancel
    {
        get => _showCancel;
        private set => Set(ref _showCancel, value);
    }

    public string? InputLabel
    {
        get => _inputLabel;
        private set
        {
            if (Set(ref _inputLabel, value))
            {
                Raise(nameof(ShowInput));
                Raise(nameof(ShowPlain));
                Raise(nameof(ShowPassword));
                Raise(nameof(ShowMultiline));
            }
        }
    }

    public string? InputPlaceholder
    {
        get => _inputPlaceholder;
        private set => Set(ref _inputPlaceholder, value);
    }

    public string Input
    {
        get => _input;
        set
        {
            if (Set(ref _input, value) && Error is not null)
            {
                Error = null;
            }
        }
    }

    public string? Hint
    {
        get => _hint;
        private set
        {
            if (Set(ref _hint, value))
            {
                Raise(nameof(HasHint));
            }
        }
    }

    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

    public string? Error
    {
        get => _error;
        set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public bool IsPassword
    {
        get => _isPassword;
        private set
        {
            if (Set(ref _isPassword, value))
            {
                Raise(nameof(ShowPassword));
                Raise(nameof(ShowPlain));
            }
        }
    }

    public bool IsMultiline
    {
        get => _isMultiline;
        private set
        {
            if (Set(ref _isMultiline, value))
            {
                Raise(nameof(ShowMultiline));
                Raise(nameof(ShowPlain));
            }
        }
    }

    public bool ShowInput => !string.IsNullOrWhiteSpace(InputLabel);
    public bool ShowPassword => ShowInput && IsPassword;
    public bool ShowPlain => ShowInput && !IsPassword && !IsMultiline;
    public bool ShowMultiline => ShowInput && IsMultiline;

    public Task<DialogResult?> ShowAsync(DialogOptions options)
    {
        _pending?.TrySetResult(null);

        Title = options.Title;
        Body = options.Body ?? string.Empty;
        ConfirmText = options.ConfirmText;
        CancelText = options.CancelText;
        InputLabel = options.InputLabel;
        InputPlaceholder = options.InputPlaceholder;
        Input = options.InitialInput ?? string.Empty;
        Hint = options.Hint;
        IsPassword = options.IsPassword;
        IsMultiline = options.IsMultiline;
        ShowCancel = options.ShowCancel;
        Error = null;
        _validate = options.Validate;
        IsOpen = true;

        _pending = new TaskCompletionSource<DialogResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.Task;
    }

    private void Confirm()
    {
        var error = _validate?.Invoke(Input);
        if (!string.IsNullOrWhiteSpace(error))
        {
            Error = error;
            return;
        }

        Finish(new DialogResult(true, Input.Trim()));
    }

    private void Cancel() => Finish(null);

    private void Finish(DialogResult? result)
    {
        IsOpen = false;
        var pending = _pending;
        _pending = null;
        _validate = null;
        pending?.TrySetResult(result);
    }
}
