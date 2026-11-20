using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Still.App.Views;

public partial class TasksView : UserControl
{
    public TasksView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
