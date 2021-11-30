using DPA.Adapter.Contracts;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Project109.Host.Base;

namespace Xtensive.Project109.Host.DPA
{
	public class EventsToDatabaseTrigger : Signals2TriggerBase
	{
		private readonly IEventSource eventSource;
		private readonly IHostLog<EventsToDatabaseTrigger> logger;
		private IDisposable subscription;

		public EventsToDatabaseTrigger(IEventSource eventSource, IHostLog<EventsToDatabaseTrigger> logger)
		{
			this.eventSource = eventSource;
			this.logger = logger;
		}

		public override Task StartAsync()
		{
			if (subscription != null) {
				subscription.Dispose();
			}
			subscription = eventSource
				.EventsOf<ObjectChanged<SharedEventInfo>>()
				.Where(RequiresToSave)
				.Subscribe(Fire);
			return Task.CompletedTask;
		}

		private void Fire(ObjectChanged<SharedEventInfo> changedData)
		{
			logger.Info(string.Format("Fired for event {0}", changedData.NewValue.EventName));
			OnSignal(changedData);
		}

		private bool RequiresToSave(ObjectChanged<SharedEventInfo> changedData)
		{
			try {
				if (changedData == null || changedData.NewValue == null || changedData.NewValue.EventInfo == null) {
					return false;
				}

				if (!EventsToDatabaseConfig.TableBuilders.ContainsKey(changedData.NewValue.DriverIdentifier)) {
					return false;
				}

				if (changedData.NewValue.EventIdentifier != Guid.Parse(EventsToDatabaseConfig.TriggerEventName)) {
					return false;
				}

				var oldValue = changedData.OldValue.GetFieldValue(EventsToDatabaseConfig.TriggerValueName);
				var newValue = changedData.NewValue.GetFieldValue(EventsToDatabaseConfig.TriggerValueName);
				if (!object.Equals(oldValue, newValue) && object.Equals(newValue, EventsToDatabaseConfig.TriggerExpectedValue)) {
					return true;
				}
			}
			catch (Exception ex) {
				logger.Error(ex);
			}

			return false;
		}

		public override Task StopAsync()
		{
			if (subscription != null) {
				subscription.Dispose();
				subscription = null;
			}
			return Task.CompletedTask;
		}
	}
}
