using System;
using System.Globalization;

namespace HitAnalyzer2.Services.AppLink
{
    internal static class HipMessages
    {
        internal static string Sid { get; set; } = AppLinkSettings.DefaultSid;

        // Obsolete HIP 0100 format for legacy receivers. Coordinates are corrected pixels
        // relative to the top-left corner of the 640 × 480 reference image.
        [Obsolete("HIP 0100 is intended for legacy receivers. Use GetHitFromTargetCenter for HIP 0110.")]
        public static string GetHitFromTopLeftCorner(double x, double y, double points, double timeMilliseconds)
        {
            double timeSeconds = timeMilliseconds / 1000;
            return FormattableString.Invariant($"HA-0100-{Sid}-{x:0000.0}-{y:0000.0}-{points:00.0}-{timeSeconds:0000.00}");
        }

		//110 Coordinates are in millimeters from the center of the target.
		public static string GetHitFromTargetCenter(double x, double y, double timeMilliseconds)
        {
            double timeSeconds = timeMilliseconds / 1000;
            string formattedTime = timeSeconds.ToString("F1", CultureInfo.InvariantCulture);
            return $"HA-0110-{Sid}-{FormatCoordinate(x)}-{FormatCoordinate(y)}-{formattedTime}";
        }


        //101 Monoscope
        public static string GetHitMonoscope(double x, double y)
		{
			
			return $"HA-0101-{Sid}-{FormatCoordinate(x)}-{FormatCoordinate(y)}";
		}

        //199
        public static string GetHitMonoscopeOutOfRange(double x, double y)
		{
			return $"HA-0199-{Sid}-Hit is out of range!";
		}

		private static string FormatCoordinate(double coordinate)
        {
            string magnitude = Math.Abs(coordinate).ToString("F1", CultureInfo.InvariantCulture);
            return coordinate < 0 && magnitude != "0.0" ? $"N{magnitude}" : magnitude;
        }

        public static string GetMonoscopeHit(double x, double y) =>
            FormattableString.Invariant($"HA-0101-{Sid}-{x:0000.0}-{y:0000.0}");

        public static string GetMonoscopeOutOfRange() =>
            $"HA-0199-{Sid}-Hit is out of range.";






		//200
		public static string GetTest() =>
            $"HA-0200-{Sid}-Hello world!";



    }
}
