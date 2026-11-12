namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class WeaponHandler
    {
        internal const int MinimumOffset = -100;
        internal const int MaximumOffset = 100;

        private readonly AimHandler _aimHandler;

        internal WeaponHandler(SettingsService settings, AimHandler aimHandler)
        {
            _aimHandler = aimHandler;
            var weapon = settings.InitialWeapon;
            SetOffset(weapon.OffsetX, weapon.OffsetY);
        }

        internal (int X, int Y) Offset { get; private set; }

        internal void SetOffset(int x, int y)
        {
            Offset = (x, y);
            _aimHandler.SetOffset(x, y);
        }

    }
}
