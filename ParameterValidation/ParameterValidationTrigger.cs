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

		public ZFTrigger2(IHostLog<ZFTrigger2> logger, IEventSource eventSource)
		{
			this.eventSource = eventSource;
			this.logger = logger;
		}

		public override Task StartAsync()
		{
			sub = eventSource
				.EventsOf<ObjectChanged<EventInfo>>()
				.WithEventId(ZF_Config.VALIDATION_TRIGGER_EVENT_ID)
				.Where(RequiresValidation)
				.Subscribe(HandleIndicatorEvent);

			logger.Info(string.Format("Subscription for event {0} has started", ZF_Config.VALIDATION_TRIGGER_EVENT_ID));
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

			try {
				var previousValue = x.OldValue == null ? string.Empty : ExtractValue(x.OldValue);
				var newValue = ExtractValue(x.NewValue);
				return previousValue != newValue && newValue == ZF_Config.VALIDATION_TRIGGER_VALUE;
			}
			catch (Exception ex) {
				logger.Error(ex);
				return false;
			}
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

			var equipments = Query.All<Equipment>()
				.Where(x => x.DriverIdentifier == obj.NewValue.DriverIdentifier)
				.Select(x => new { x.Id, x.Name })
				.ToArray();

			foreach (var equipment in equipments) {
				logger.Info(string.Format("Trigger fired for event '{0}'({1}) of equipment '{2}'({3}) with value = '{4}'", obj.NewValue.EventName, obj.NewValue.EventIdentifier, equipment.Name, equipment.Id, ExtractValue(obj.NewValue)));
				OnSignal(Tuple.Create(equipment.Id, ZF_Config.VALIDATION_CHANNEL, obj.NewValue.TimeStamp));
			}
		}
	}
}