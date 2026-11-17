using System;

namespace HitAnalyzer2.Models.Shooting
{
	internal class HitPosition : LaserPosition
	{
		public HitPosition(double x, double y, TimeSpan time, double score) : base(x, y, time)
		{
			Score = score;
		}

		public double Score { get; }
	}
}
