using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HitAnalyzer2.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    private void NavigateToSection(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string anchor })
            this.FindControl<Control>("Help_" + anchor.TrimStart('#').Replace('-', '_'))?.BringIntoView();
    }
}
