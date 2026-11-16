using HitAnalyzer2.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace HitAnalyzer2.Services.Recording
{
	internal class RecordingService
	{
		private List<HitRecord> hitRecords = new List<HitRecord>();

		public void AddHitRecord(HitRecord hitRecord)
		{
			hitRecords.Add(hitRecord);
		}

		public void ClearHitRecordsList()
		{
			hitRecords.Clear();
		}

		public IReadOnlyList<HitRecord> GetHitRecordsList()
		{
			return hitRecords.AsReadOnly();
		}


	}
}
