using System;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFDowntimeUnclassified
	{ 
		public long EquipmentId { get; set; }
		public DateTimeOffset StartDate { get; set; }
		public long ReasonId { get; set; }
		public int LevelId { get; set; }

		public override string ToString()
		{
			return string.Format("{0} {1} {2} {3}", EquipmentId, LevelId, ReasonId, StartDate);
		}
	}
}
