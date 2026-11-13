using Avalonia.Controls;
using Avalonia.Data.Converters;

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
    }
}
