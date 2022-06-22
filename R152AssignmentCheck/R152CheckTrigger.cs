using DPA.Adapter.Contracts;
using DPA.Adapter.Dto;
using DPA.Core.Repository.Commands;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.Host.Contracts;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class R152CheckTrigger : Signals2TriggerBase
	{
		private readonly ILogger<R152CheckTrigger> logger;
		private IEventSource generalEventSource;
		private IDisposable sub;

		public R152CheckTrigger(IServiceProvider serviceProvider)
		{
			generalEventSource = serviceProvider.GetRequiredService<IEventSource>();
			logger = serviceProvider.GetRequiredService<ILogger<R152CheckTrigger>>();
		}

		public override Task StartAsync()
		{
			sub = generalEventSource
				.EventsOf<DownloadProgramRecordModel>()
				.Subscribe(HandleIndicatorEvent);
			return Task.CompletedTask;
		}

		private void HandleIndicatorEvent(DownloadProgramRecordModel dto)
		{
			try {
				OnSignal(dto);
			}
			catch (Exception e)
			{
				logger.LogError("Failed to check CP source code for R152 register incrementation", e);
			}
		}

		public override Task StopAsync()
		{
			sub.Dispose();
			return Task.CompletedTask;
		}
	}
}