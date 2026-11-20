using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Still.App.Views;

public partial class ContactsView : UserControl
{
    public ContactsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
