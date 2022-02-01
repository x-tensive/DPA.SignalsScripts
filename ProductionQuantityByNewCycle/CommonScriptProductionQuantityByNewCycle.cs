using DPA.Core.Contracts;

namespace Xtensive.Project109.Host.DPA
{
	public class ProductionQuantityByNewCycle
	{
		public long EquipmentId { get; set; }
		public long JobId { get; set; }
		public decimal Quantity { get; set; }
		public ReleaseQualityMark Quality { get; set; }

		public override string ToString()
		{
			return string.Format("{0} {1} {2} of {3}", EquipmentId, JobId, Quantity, Quality);
		}
	}
}
