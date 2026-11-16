using System;
using System.Collections.Generic;

namespace HitAnalyzer2.Models
{
	internal class HitRecord
	{
		public LaserPosition? Position { get; }
		public TimeSpan Time { get; }

		public IReadOnlyList<LaserPosition> AimPathBefore { get; }

		public IReadOnlyList<LaserPosition> AimPathAfter { get; internal set; } = Array.Empty<LaserPosition>();

		internal HitRecord(LaserPosition? position, TimeSpan time, IReadOnlyList<LaserPosition> aimPathBefore)
		{
			Position = position;
			Time = time;
			AimPathBefore = aimPathBefore;
		}
	}
}
