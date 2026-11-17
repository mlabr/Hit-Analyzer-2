using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using HitAnalyzer2.ViewModels;

namespace HitAnalyzer2.Views
{
    public partial class WeaponSettingsView : UserControl
    {
        public static FuncValueConverter<TriggerTypeEnum, string> TriggerLabel { get; } =
            new(trigger => trigger == TriggerTypeEnum.ExternalEvent ? "External Event" : trigger.ToString());

        public WeaponSettingsView()
        {
            InitializeComponent();
        }

        private void CommitCaliberOnLostFocus(object? sender, RoutedEventArgs e) => CommitCaliber(sender);

        private void CommitCaliberOnEnter(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            CommitCaliber(sender);
            e.Handled = true;
        }

        private void CommitCaliber(object? sender)
        {
            if (sender is TextBox editor && DataContext is WeaponSettingsViewModel settings)
            {
                settings.CommitCaliber(editor.Text);
                editor.SetCurrentValue(TextBox.TextProperty, settings.CaliberText);
            }
        }
    }
}
