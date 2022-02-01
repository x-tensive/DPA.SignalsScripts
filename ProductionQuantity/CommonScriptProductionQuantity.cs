using DPA.Core.Contracts;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFProductionQuantity 
	{ 
		public long EquipmentId { get; set; }

		public decimal Quantity { get; set; }
		public ReleaseQualityMark Quality { get; set; }

		public override string ToString()
		{
			return string.Format("{0} {1} of {2}", EquipmentId, Quantity, Quality);
		}
	}
}
