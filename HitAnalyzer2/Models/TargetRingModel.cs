using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;

namespace HitAnalyzer2.Models
{
	internal class TargetRingModel
	{
		public double Diameter { get; init; }

		public double Radius => Diameter / 2;

		public double PointValue { get; init; }
	}
}
