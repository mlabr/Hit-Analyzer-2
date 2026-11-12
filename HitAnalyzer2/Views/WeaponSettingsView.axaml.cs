using Avalonia.Controls;
using Avalonia.Data.Converters;
using HitAnalyzer2.Services.Shooting;

namespace HitAnalyzer2.Views
{
    public partial class WeaponSettingsView : UserControl
    {
        public static FuncValueConverter<HitTrigger, string> TriggerLabel { get; } =
            new(trigger => trigger == HitTrigger.ExternalEvent ? "External Event" : trigger.ToString());

        public WeaponSettingsView()
        {
            InitializeComponent();
        }
    }
}
