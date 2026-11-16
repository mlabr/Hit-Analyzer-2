using HitAnalyzer2.Models;
using System;
using System.Collections.Generic;

namespace HitAnalyzer2.Services.Recording
{
	internal class RecordingService
	{
		private readonly object _recordsGate = new();
		private readonly List<HitRecord> hitRecordList = new();

		internal event Action? HitCountChanged;

		internal int GetHitRecordCount()
		{
			lock (_recordsGate)
				return hitRecordList.Count;
		}

		public void AddHitRecord(HitRecord hitRecord)
		{
			lock (_recordsGate)
				hitRecordList.Add(hitRecord);

			HitCountChanged?.Invoke();
		}

		public void ClearHitRecordsList()
		{
			lock (_recordsGate)
			{
				if (hitRecordList.Count == 0)
					return;

				hitRecordList.Clear();
			}

			HitCountChanged?.Invoke();
		}

		public IReadOnlyList<HitRecord> GetHitRecordsList()
		{
			lock (_recordsGate)
				return Array.AsReadOnly(hitRecordList.ToArray());
		}


	}
}
