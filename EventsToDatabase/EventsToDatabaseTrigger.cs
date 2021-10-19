using DPA.Adapter.Contracts;
using DPA.Adapter.Contracts.Compare;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;

namespace Xtensive.Project109.Host.DPA.Tests.Signals2.Scripts.DPA.SignalsScripts.EventsToDatabase
{
	public class EventsToDatabaseTrigger : Signals2TriggerBase
	{
		private readonly IEventSource eventSource;
		private IDisposable subscription;
		private readonly SharedEventInfoComparator comparator = new SharedEventInfoComparator();

		public EventsToDatabaseTrigger(IEventSource eventSource)
		{
			this.eventSource = eventSource;
		}

		public override Task StartAsync()
		{
			subscription?.Dispose();
			subscription = eventSource
				.EventsOf<ObjectChanged<SharedEventInfo>>()
				.Where(RequiresToSave)
				.Subscribe(OnSignal);
			return Task.CompletedTask;
		}

		private bool RequiresToSave(ObjectChanged<SharedEventInfo> changedData)
		{
			if (changedData?.NewValue?.EventInfo == null) {
				return false;
			}
			if (!EventsToDatabaseConfig.DriversForMonitoring.Contains(changedData.NewValue.DriverIdentifier)) {
				return false;
			}
			if (!EventsToDatabaseConfig.TableBuilders.ContainsKey(changedData.NewValue.EventName)) {
				return false;
			}
			return !comparator.AreEqual(changedData.OldValue, changedData.NewValue);
		}

		public override Task StopAsync()
		{
			subscription?.Dispose();
			subscription = null;
			return Task.CompletedTask;
		}
	}
}
