using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Text;

namespace HitAnalyzer2.Models
{
	internal class FpsValueModel
	{
		private int _frameCount = 0;
		private int _elapsedMilliseconds = 0;
		private int? _lastMeasuringTimestampMs;
		private int _measuringIntervalMs = 1500;

		private double _fps = 0;
		public double GetFps()
		{
			return _fps; 
		}

		public string GetFpsString()
		{
			return _fps.ToString("0");
		}

		public void Update(int? currentTimestampNullable)
		{
			if(currentTimestampNullable == null)
			{
				return;
			}
			int currentTimestamp = (int)currentTimestampNullable;

			if (_lastMeasuringTimestampMs is not { } previousTimestamp)
			{
				_lastMeasuringTimestampMs = currentTimestamp;
				return;
			}

			_elapsedMilliseconds = currentTimestamp - previousTimestamp;
			_frameCount++;

			if (_elapsedMilliseconds >= _measuringIntervalMs)
			{
				_fps = _frameCount / (_elapsedMilliseconds / 1000.0);
				_lastMeasuringTimestampMs = currentTimestamp;
				_frameCount = 0;
			}

		}


	}
}
