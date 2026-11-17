using System;
using System.Collections.Generic;
using System.Threading;

namespace HitAnalyzer2.Models
{
	internal class HitRecord(LaserPosition hitPosition, IReadOnlyList<LaserPosition> aimPathBefore)
	{
		public LaserPosition HitPosition { get; } = hitPosition;

		public IReadOnlyList<LaserPosition> AimPathBefore { get; } = aimPathBefore;

		private List<LaserPosition>? _collectingAimPathAfter;
		private IReadOnlyList<LaserPosition>? _completedAimPathAfter = Array.Empty<LaserPosition>();

		// Null while the capture thread is still collecting the path.
		public IReadOnlyList<LaserPosition>? AimPathAfter => Volatile.Read(ref _completedAimPathAfter);

		internal List<LaserPosition> BeginAimPathAfter()
		{
			var path = new List<LaserPosition>();
			_collectingAimPathAfter = path;
			Volatile.Write(ref _completedAimPathAfter, null);
			return path;
		}

		internal void CompleteAimPathAfter()
		{
			if (_collectingAimPathAfter is not { } path)
				return;

			Volatile.Write(ref _completedAimPathAfter, Array.AsReadOnly(path.ToArray()));
			_collectingAimPathAfter = null;
		}
	}
}
