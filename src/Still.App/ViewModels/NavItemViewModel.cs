using System.Windows.Input;
using Avalonia.Media;

namespace Still.App.ViewModels;

public sealed class NavItemViewModel : ObservableObject
{
    private bool _isActive;

    public NavItemViewModel(string label, string route, string iconPath, ICommand command)
    {
        Label = label;
        Route = route;
        Icon = StreamGeometry.Parse(iconPath);
        Command = command;
    }

    public string Label { get; }
    public string Route { get; }
    public Geometry Icon { get; }
    public ICommand Command { get; }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }
}
