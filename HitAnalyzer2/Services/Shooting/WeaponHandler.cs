namespace HitAnalyzer2.Services.Shooting
{
    internal sealed class WeaponHandler
    {
        internal const int MinimumOffset = -100;
        internal const int MaximumOffset = 100;

        private readonly AimPositionHandler _aimPositionHandler;

        internal WeaponHandler(int offsetX, int offsetY, AimPositionHandler aimPositionHandler)
        {
            _aimPositionHandler = aimPositionHandler;
            SetOffset(offsetX, offsetY);
        }

        internal (int X, int Y) Offset { get; private set; }

        internal void SetOffset(int x, int y)
        {
            Offset = (x, y);
            _aimPositionHandler.SetOffset(x, y);
        }
    }
}
