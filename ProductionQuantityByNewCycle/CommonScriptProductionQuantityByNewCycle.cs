using System;
using Xtensive.DPA.Host.Contracts;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ProductionQuantityByNewCycle 
	{ 
		public long EquipmentId { get; set; }
		public long JobId { get; set; }
		public QuantityModel QuantityModel { get; set; }

		public override string ToString()
		{
			return string.Format("{0} {1} {2} {3} {4}", EquipmentId, JobId, QuantityModel.Accepted, QuantityModel.Rejected, QuantityModel.Undefined);
		}
	}
}
