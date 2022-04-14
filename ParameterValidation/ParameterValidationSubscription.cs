using DPA.Adapter.Contracts;
using System;
using System.Linq;
using System.Reactive.Linq;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ParameterValidationSubscription : IDisposable
	{
		private readonly IDisposable sub;
		private readonly ILogger logger;
		private readonly Action<object> sendSignal;

		public ParameterValidationSubscription(Guid driverId, ILogger logger, IEventSource eventSource, Action<object> sendSignal)
		{
			sub = eventSource
				.EventsOf<ObjectChanged<EventInfo>>()
				.WithDriverId(driverId)
				.Where(RequiresValidation)
				.Subscribe(HandleIndicatorEvent);

			logger.LogInformation(string.Format("Subscription for driver {0} has started", driverId));
			this.logger = logger;
			this.sendSignal = sendSignal;
		}

		public void Dispose()
		{
			sub.Dispose();
		}

		private bool RequiresValidation(ObjectChanged<EventInfo> x)
		{
			if (x.NewValue == null) {
				return false;
			}

			logger.LogInformation(x.NewValue.EventName);

			if (x.NewValue.EventName != ZF_Config.VALIDATION_TRIGGER_EVENT_NAME.ToString()) {
				return false;
			}

			try {
				var previousValue = x.OldValue == null ? string.Empty : ExtractValue(x.OldValue);
				var newValue = ExtractValue(x.NewValue);
				return previousValue != newValue && newValue == ZF_Config.VALIDATION_TRIGGER_VALUE;
			}
			catch (Exception ex) {
				logger.LogError(ex, "Error");
				return false;
			}
		}

		private string ExtractValue(EventInfo eventInfo)
		{
			return eventInfo.GetFieldValue(ZF_Config.VALIDATION_TRIGGER).ToString();
		}

		private void HandleIndicatorEvent(ObjectChanged<EventInfo> obj)
		{
			try {
				if (obj.NewValue == null) {
					return;
				}

				var equipments = Query.All<Equipment>()
					.Where(x => x.DriverIdentifier == obj.NewValue.DriverIdentifier)
					.Select(x => new { x.Id, x.Name })
					.ToArray();

				foreach (var equipment in equipments) {
					logger.LogInformation(string.Format("Trigger fired for event '{0}'({1}) of equipment '{2}'({3}) with value = '{4}'", obj.NewValue.EventName, obj.NewValue.EventIdentifier, equipment.Name, equipment.Id, ExtractValue(obj.NewValue)));
					sendSignal(Tuple.Create(equipment.Id, ZF_Config.VALIDATION_CHANNEL, obj.NewValue.TimeStamp));
				}
			}
			catch (Exception ex) {
				logger.LogError(ex, string.Format("Unable to handle trigger for event '{0}'({1}) of driver '{2}'", obj.NewValue.EventName, obj.NewValue.EventIdentifier, obj.NewValue.DriverIdentifier));
			}
		}
	}
}