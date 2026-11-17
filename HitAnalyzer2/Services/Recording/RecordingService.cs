using HitAnalyzer2.Models;
using HitAnalyzer2.Services.Target;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HitAnalyzer2.Services.Recording
{
	internal class RecordingService
	{
		
		private readonly object _recordsGate = new();
		private readonly List<HitRecord> hitRecordNormalizedList = new();
		private readonly TargetCalibrationService _calibration;
		private TargetPreviewModel _target;

		internal RecordingService(TargetCalibrationService calibration)
		{
			_calibration = calibration;
			_target = calibration.Settings;
			calibration.CalibrationGeometryChanged += ClearNormalizedHitRecordList;
		}

		internal TargetModel Target
		{
			get
			{
				lock (_recordsGate)
					return _target;
			}
		}

		internal double? MmToPixel
		{
			get
			{
				lock (_recordsGate)
					return _target.MmToPixel;
			}
		}

		internal event Action? HitCountChanged;

		internal int GetHitRecordCount()
		{
			lock (_recordsGate)
				return hitRecordNormalizedList.Count;
		}

		internal double? GetLastHitDistanceFromCenterMm()
		{
			lock (_recordsGate)
				return hitRecordNormalizedList.Count > 0 ? hitRecordNormalizedList[^1].DistanceFromCenterMm : null;
		}

		internal double GetLastHitScore()
		{
			lock (_recordsGate)
				return hitRecordNormalizedList.Count > 0 ? hitRecordNormalizedList[^1].HitPosition.Score : 0;
		}

		internal double GetTotalScore()
		{
			lock (_recordsGate)
				return hitRecordNormalizedList.Sum(record => record.HitPosition.Score);
		}

		internal void AddNormalizedHitRecord(HitRecord hitRecord, TargetPreviewModel calibrationAtHit)
		{
			lock (_recordsGate)
			{
				if (!ReferenceEquals(calibrationAtHit, _target) ||
					!ReferenceEquals(calibrationAtHit, _calibration.Settings))
					return;

				hitRecordNormalizedList.Add(hitRecord);
			}

			HitCountChanged?.Invoke();
		}

		public void ClearNormalizedHitRecordList()
		{
			lock (_recordsGate)
			{
				_target = _calibration.Settings;
				if (hitRecordNormalizedList.Count == 0)
					return;

				hitRecordNormalizedList.Clear();
			}
			
			HitCountChanged?.Invoke();
		}

		public IReadOnlyList<HitRecord> GetNormalizedHitRecordList()
		{
			lock (_recordsGate)
				return Array.AsReadOnly(hitRecordNormalizedList.ToArray());
		}


	}
}
