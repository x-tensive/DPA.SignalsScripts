using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFTriggerDowntimeUnclassified2 : Signals2TriggerBase
	{
		private readonly ILogger<ZFTriggerDowntimeUnclassified2> logger;
		private CancellationTokenSource cts = new CancellationTokenSource();
		private Task watcherTask;

		private TimeSpan workerDelay = TimeSpan.FromSeconds(10);

		public ZFTriggerDowntimeUnclassified2(IServiceProvider serviceProvider)
		{
			logger = serviceProvider.GetRequiredService<ILogger<ZFTriggerDowntimeUnclassified2>>();
		}
		public override Task StartAsync()
		{
			cts = new CancellationTokenSource();
			watcherTask = Task.Run(() => Worker(cts.Token), cts.Token);

			return Task.CompletedTask;
		}

		private async Task Worker(CancellationToken token)
		{
			while (true)
			{
				token.ThrowIfCancellationRequested();

				try
				{
					OnSignal(new ZFDowntimeUnclassified
					{
						EquipmentId = 34035,
						LevelId = 1
					});
				}
				catch (Exception ex)
				{
					logger.Error(ex);
					OnSignalError(ex.Message);
				}
				await Task.Delay(workerDelay, token);
			}
		}

		public override Task StopAsync()
		{
			return Task.CompletedTask;
		}
	}
}
