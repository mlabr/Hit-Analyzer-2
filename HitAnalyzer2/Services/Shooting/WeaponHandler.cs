using System;

namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class WeaponHandler
    {
        internal const int MinimumOffset = -100;
        internal const int MaximumOffset = 100;

        private readonly AimPositionHandler _aimPositionHandler;
        private readonly object _caliberGate = new();
        private decimal _caliberDiameterMm;

        internal WeaponHandler(WeaponSettings weapon, AimPositionHandler aimPositionHandler)
        {
            _aimPositionHandler = aimPositionHandler;
            SetOffset(weapon.OffsetX, weapon.OffsetY);
            SetCaliber((weapon as WeaponProfile)?.Caliber ?? 0);
        }

        internal (int X, int Y) Offset { get; private set; }
        internal decimal CaliberDiameterMm
        {
            get
            {
                lock (_caliberGate)
                    return _caliberDiameterMm;
            }
        }

        internal void SetCaliber(decimal diameterMm)
        {
            lock (_caliberGate)
                _caliberDiameterMm = Math.Clamp(diameterMm, 0, 20);
        }

        internal void SetOffset(int x, int y)
        {
            Offset = (x, y);
            _aimPositionHandler.SetOffset(x, y);
        }
    }
}
