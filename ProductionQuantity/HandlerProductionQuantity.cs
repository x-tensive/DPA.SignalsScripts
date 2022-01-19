using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;


namespace Xtensive.Project109.Host.DPA
{
	public class ZFHandlerProductionQuantity : Signals2HandlerBase
	{
		private readonly IJobService jobService;
		private readonly IHostLog<ZFHandlerProductionQuantity> logger;

		public ZFHandlerProductionQuantity(IServiceProvider serviceProvider)
		{
			//equipmentService = serviceProvider.GetRequiredService<IEquipmentService>();
			jobService = serviceProvider.GetRequiredService<IJobService>();
			logger = serviceProvider.GetRequiredService<IHostLog<ZFHandlerProductionQuantity>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.Info(args);

			if (args.Obj is ZFProductionQuantity) {
				var productionQuantity = (ZFProductionQuantity)args.Obj;
				var job = jobService.GetActiveProduction(productionQuantity.EquipmentId);
				if (job != null) {
					var user = Query.All<JobRunPeriod>().SingleOrDefault(rp => rp.End == null && rp.Job.Id == job.Id).StartOperator;
					jobService.AppendQuantity(job.Id, productionQuantity.QuantityModel, Array.Empty<OperatorComponentConsumptionDto>(), "signals2", null, DateTimeOffset.UtcNow, user);
				}
			}
			return Task.CompletedTask;
		}
	}
}
