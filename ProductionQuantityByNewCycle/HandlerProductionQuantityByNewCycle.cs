using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerProductionQuantityByNewCycle : Signals2HandlerBase
	{
		private readonly IJobService jobService;
		private readonly IHostLog<HandlerProductionQuantityByNewCycle> logger;

		public HandlerProductionQuantityByNewCycle(IServiceProvider serviceProvider)
		{
			//equipmentService = serviceProvider.GetRequiredService<IEquipmentService>();
			jobService = serviceProvider.GetRequiredService<IJobService>();
			logger = serviceProvider.GetRequiredService<IHostLog<HandlerProductionQuantityByNewCycle>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.Info(args);

			if (args.Obj is ProductionQuantityByNewCycle) {
				var productionQuantity = (ProductionQuantityByNewCycle)args.Obj;
				var jobId = productionQuantity.JobId;
				jobService.AppendQuantity(jobId, productionQuantity.QuantityModel, Array.Empty<OperatorComponentConsumptionDto>(), "signals2", null, DateTimeOffset.UtcNow, null);
			}
			return Task.CompletedTask;
		}
	}
}
