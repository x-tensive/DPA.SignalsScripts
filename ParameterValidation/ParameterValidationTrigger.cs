using DPA.Adapter.Contracts;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFTrigger2 : Signals2TriggerBase
	{
		private readonly IHostLog<ZFTrigger2> logger;
		private readonly IEventSource eventSource;
		private IDisposable sub;
		private long equipmentId;

		public ZFTrigger2(IHostLog<ZFTrigger2> logger, IEventSource eventSource)
		{
			this.eventSource = eventSource;
			this.logger = logger;
		}

		public override Task StartAsync()
		{
			equipmentId = Query.All<Equipment>().Where(x => x.Name == "LR018").Select(x => x.Id).First();
			sub = eventSource
				.EventsOf<ObjectChanged<EventInfo>>()
				.WithEventId(Guid.Parse("fef321ed-6a5e-4538-afb8-711bfee7351e"))
				.Where(RequiresValidation)
				.Subscribe(HandleIndicatorEvent);

			logger.Info(string.Format("Subscription for equipment {0} has started", equipmentId));
			return Task.CompletedTask;
		}

		public override Task StopAsync()
		{
			sub.Dispose();
			return Task.CompletedTask;
		}

		private bool RequiresValidation(ObjectChanged<EventInfo> x)
		{
			if (x.NewValue == null) {
				return false;
			}

			var previousValue = x.OldValue == null ? string.Empty : ExtractValue(x.OldValue);
			var newValue = ExtractValue(x.NewValue);
			return previousValue != newValue && newValue == ZF_Config.VALIDATION_TRIGGER_VALUE;
		}

		private string ExtractValue(EventInfo eventInfo)
		{
			return eventInfo.GetFieldValue(ZF_Config.VALIDATION_TRIGGER).ToString();
		}

		private void HandleIndicatorEvent(ObjectChanged<EventInfo> obj)
		{
			if (obj.NewValue == null) {
				return;
			}

			logger.Info("Trigger fired " + obj.NewValue.EventIdentifier + " - " + ExtractValue(obj.NewValue));
			OnSignal(Tuple.Create(equipmentId, 1));
		}
	}
}