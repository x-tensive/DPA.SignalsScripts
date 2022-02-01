using DPA.Planning.Client;
using DPA.Planning.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFHandlerProductionQuantity : Signals2HandlerBase
	{
		private readonly IJobDataClient jobClient;
		private readonly IOperatorJobClient jobManageClient;
		private readonly ILogger<ZFHandlerProductionQuantity> logger;

		public ZFHandlerProductionQuantity(IServiceProvider serviceProvider)
		{
			jobClient = serviceProvider.GetRequiredService<IJobDataClient>();
			jobManageClient = serviceProvider.GetRequiredService<IOperatorJobClient>();
			logger = serviceProvider.GetRequiredService<ILogger<ZFHandlerProductionQuantity>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.LogInformation(args.ToString());

			if (args.Obj is ZFProductionQuantity) {
				var productionQuantity = (ZFProductionQuantity)args.Obj;
				var job = jobClient.GetStartedProduction(productionQuantity.EquipmentId).FirstOrDefault();
				if (job != null) {
					////todo: ust
					//var user = jobClient.GetActiveJobLastOperatorName(new[] { job.Id })
					//	.Select(x => x.OperatorId)
					//	.FirstOrDefault();
					//jobClient.AppendQuantity(job.Id, productionQuantity.QuantityModel, Array.Empty<OperatorComponentConsumptionDto>(), "signals2", null, DateTimeOffset.UtcNow, user);
					
					var model = new AppendQuantityModel {
						JobId = job.Id,
						Quantity = productionQuantity.Quantity,
						Quality = productionQuantity.Quality,
						ComponentConsumptions = Array.Empty<ComponentConsumptionModel>(),
						Comment = "signals2",
					};
					jobManageClient.AppendQuantity(model);
				}
			}
			return Task.CompletedTask;
		}
	}
}
