using System;
using System.Collections.Generic;
using System.Text;

namespace HitAnalyzer2.Services.Shooting
{
	internal class SessionLimits
	{
		public int HitCountInitial = 40;

		public bool IsHitLimitEnabled = false;

		public int TimeLimitSeconds = 60;

		public bool IsTimeLimitEnabled = false;

	}
}
