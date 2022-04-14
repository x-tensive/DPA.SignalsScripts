using DPA.Adapter.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFTrigger2 : Signals2TriggerBase
	{
		private Guid driverId = Guid.Parse("0ebdd7aa-2504-416f-87a9-c4b63a42e38b");
		private readonly ILogger<ZFTrigger2> logger;
		private readonly IEventSource eventSource;
		private IDisposable sub;

		public ZFTrigger2(ILogger<ZFTrigger2> logger, IEventSource eventSource)
		{
			this.eventSource = eventSource;
			this.logger = logger;
		}

		public override Task StartAsync()
		{
			sub = new ParameterValidationSubscription(driverId, logger, eventSource, OnSignal);
			return Task.CompletedTask;
		}

		public override Task StopAsync()
		{
			sub.Dispose();
			return Task.CompletedTask;
		}
	}
}