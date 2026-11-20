using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;

namespace Still.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Action<Exception>? _onError;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Action<Exception>? onError = null)
    {
        _execute = execute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public interface IPageViewModel
{
    void Refresh();
}

public static class UiBrushes
{
    public static IBrush Accent { get; } = Brush.Parse("#C4F5A9");
    public static IBrush AccentDeep { get; } = Brush.Parse("#A9E58F");
    public static IBrush Text { get; } = Brush.Parse("#F1F4F7");
    public static IBrush Muted { get; } = Brush.Parse("#99A3B2");
    public static IBrush Dim { get; } = Brush.Parse("#6C7686");
    public static IBrush Warning { get; } = Brush.Parse("#F3C978");
    public static IBrush Danger { get; } = Brush.Parse("#F08A82");
    public static IBrush Info { get; } = Brush.Parse("#8FB8F0");

    public static readonly string[] RulePalette =
    [
        "#C4F5A9", "#8FB8F0", "#E0B98A", "#C7A3E8", "#79C6B2", "#E39B9B", "#B9C27F"
    ];

    public static string ColorForRule(Guid ruleId)
    {
        var hash = ruleId.GetHashCode() & int.MaxValue;
        return RulePalette[hash % RulePalette.Length];
    }

    public static IBrush FromHex(string hex) => Brush.Parse(hex);
}
