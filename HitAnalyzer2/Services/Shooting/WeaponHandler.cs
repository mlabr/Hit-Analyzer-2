namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class WeaponHandler
    {
        internal const int MinimumOffset = -100;
        internal const int MaximumOffset = 100;

        private readonly AimPositionHandler _aimPositionHandler;

        internal WeaponHandler(SettingsService settings, AimPositionHandler aimPositionHandler)
        {
            _aimPositionHandler = aimPositionHandler;
            var weapon = settings.InitialWeapon;
            SetOffset(weapon.OffsetX, weapon.OffsetY);
        }

        internal (int X, int Y) Offset { get; private set; }

        internal void SetOffset(int x, int y)
        {
            Offset = (x, y);
            _aimPositionHandler.SetOffset(x, y);
        }

    }
}
