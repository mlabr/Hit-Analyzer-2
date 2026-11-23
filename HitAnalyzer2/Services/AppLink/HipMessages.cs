using System;
using System.Globalization;

namespace HitAnalyzer2.Services.AppLink
{
    internal static class HipMessages
    {
        internal static string Sid { get; set; } = AppLinkSettings.DefaultSid;

        // Coordinates are in millimeters from the center of the target.
        public static string GetHitFromTargetCenter(double x, double y, double timeMilliseconds)
        {
            double timeSeconds = timeMilliseconds / 1000;
            string formattedTime = timeSeconds.ToString("F1", CultureInfo.InvariantCulture);
            return $"HA-0110-{Sid}-{FormatCoordinate(x)}-{FormatCoordinate(y)}-{formattedTime}";
        }

        private static string FormatCoordinate(double coordinate)
        {
            string magnitude = Math.Abs(coordinate).ToString("F1", CultureInfo.InvariantCulture);
            return coordinate < 0 && magnitude != "0.0" ? $"N{magnitude}" : magnitude;
        }

        //100
        public static string GetHit(double x, double y, double time)
        {
            string timeString = time.ToString("F2").Replace(',', '.');
			return $"HA-0100-{Sid}-{x:F1}-{y:F1}-{timeString}";
        }




		//200
		public static string GetTest() =>
            $"HA-0200-{Sid}-Hello world!";



    }
}
