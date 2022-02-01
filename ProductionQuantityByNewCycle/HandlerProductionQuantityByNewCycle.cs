using DPA.Core.Contracts;
using DPA.Planning.Client;
using DPA.Planning.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerProductionQuantityByNewCycle : Signals2HandlerBase
	{
		private readonly IOperatorJobClient jobService;
		private readonly ILogger<HandlerProductionQuantityByNewCycle> logger;

		public HandlerProductionQuantityByNewCycle(IServiceProvider serviceProvider)
		{
			jobService = serviceProvider.GetRequiredService<IOperatorJobClient>();
			logger = serviceProvider.GetRequiredService<ILogger<HandlerProductionQuantityByNewCycle>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.LogInformation(args.ToString());

			if (args.Obj is ProductionQuantityByNewCycle) {
				var productionQuantity = (ProductionQuantityByNewCycle)args.Obj;
				var jobId = productionQuantity.JobId;

				var model = new AppendQuantityModel {
					JobId = jobId,
					Quantity = productionQuantity.Quantity,
					Quality = productionQuantity.Quality,
					ComponentConsumptions = Array.Empty<ComponentConsumptionModel>(),
					Comment = "signals2",
				};
				jobService.AppendQuantity(model);
			}
			return Task.CompletedTask;
		}
	}
}
