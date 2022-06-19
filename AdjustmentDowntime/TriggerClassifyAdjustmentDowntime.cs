using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Reactive.Linq;
using Xtensive.Orm;

namespace Xtensive.Project109.Host.DPA
{
	public class TriggerClassifyAdjustmentDowntime : Signals2TriggerBase
	{
		private readonly ILogger<TriggerClassifyAdjustmentDowntime> logger;
		private readonly IDowntimeInfoService downtimeInfoService;
		private readonly DpaSettings dpaSettings;
		private IDisposable subscription;

		public TriggerClassifyAdjustmentDowntime(IServiceProvider serviceProvider)
		{
			dpaSettings = serviceProvider.GetRequiredService<DpaSettings>();
			downtimeInfoService = serviceProvider.GetRequiredService<IDowntimeInfoService>();
			logger = serviceProvider.GetRequiredService<ILogger<TriggerClassifyAdjustmentDowntime>>();
		}

		public override Task StartAsync()
		{
			subscription = downtimeInfoService.Subscribe(HandleInfoEvent);

			return Task.CompletedTask;
		}

		private void HandleInfoEvent(DowntimeInfoActionDto action)
		{
			try {
				if (!(action is DowntimeInfoClosedActionDto)) {
					return;
				}

				var equipmentId = action.Reasons.First().EquipmentId;
				logger.LogInformation(string.Format("DowntimeInfo equipment: {0}", equipmentId));

				if (!dpaSettings.UseDowntimeClassificationForAdjustment.Value) {
					logger.LogError(string.Format("Script failed to start. Use downtime classification settings is disabled"));
					return;
				}

				OnSignal(action);
			}
			catch (Exception e) {
				logger.LogError(e, "Error");
				OnSignalError(e.Message);
			}
		}

		public override Task StopAsync()
		{
			subscription.Dispose();
			return Task.CompletedTask;
		}
	}
}
