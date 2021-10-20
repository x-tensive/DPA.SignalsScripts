using DPA.Adapter.Contracts;
using DPA.Adapter.Contracts.Compare;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Project109.Host.Base;

namespace Xtensive.Project109.Host.DPA.Tests.Signals2.Scripts.DPA.SignalsScripts.EventsToDatabase
{
	public class EventsToDatabaseTrigger : Signals2TriggerBase
	{
		private readonly IEventSource eventSource;
		private readonly IHostLog<EventsToDatabaseTrigger> logger;
		private IDisposable subscription;
		private readonly SharedEventInfoComparator comparator = new SharedEventInfoComparator();

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
			if (changedData == null || changedData.NewValue == null || changedData.NewValue.EventInfo == null) {
				return false;
			}
			
			if (!EventsToDatabaseConfig.DriversForMonitoring.Contains(changedData.NewValue.DriverIdentifier)) {
				return false;
			}

			if (!EventsToDatabaseConfig.TableBuilders.ContainsKey(changedData.NewValue.EventIdentifier)) {
				return false;
			}
			return !comparator.AreEqual(changedData.OldValue, changedData.NewValue);
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
