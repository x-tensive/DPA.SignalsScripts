using DPA.Adapter.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class TriggerWriteOperationInRregister : Signals2TriggerBase
	{
		private readonly ILogger<TriggerWriteOperationInRregister> logger;
		private IEventSource generalEventSource;
		private IDisposable sub;
		private long equipmentId;

		public TriggerWriteOperationInRregister(IServiceProvider serviceProvider)
		{
			generalEventSource = serviceProvider.GetRequiredService<IEventSource>();
			logger = serviceProvider.GetRequiredService<ILogger<TriggerWriteOperationInRregister>>();
		}
		public override Task StartAsync()
		{
			equipmentId = Query.All<Equipment>().Where(x => x.Name == "VD10").Select(x => x.Id).First();
			sub = generalEventSource
				.EventsOf<ObjectChanged<AxisLoadEventInfo>>()
				.WithEventId(Guid.Parse("33dfb299-f03b-460a-a70f-3e361a07b9d2"))
				.Where(x => x.OldValue.GetFieldValue("Axis load, %") != x.NewValue.GetFieldValue("Axis load, %"))
				.Subscribe(HandleIndicatorEvent);

			logger.LogInformation("Subscription for equipment " + equipmentId + " started");
			return Task.CompletedTask;
		}

		private void HandleIndicatorEvent(ObjectChanged<AxisLoadEventInfo> obj)
		{
			logger.LogInformation("trigger fired " + obj.NewValue.EventIdentifier + " - " + obj.NewValue.GetFieldValue("Axis load, %"));
			OnSignal(Tuple.Create(equipmentId, obj.NewValue.Number));
		}

		public override Task StopAsync()
		{
			sub.Dispose();
			return Task.CompletedTask;
		}
	}
}
