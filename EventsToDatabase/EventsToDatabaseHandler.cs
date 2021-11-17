using DPA.Adapter.Contracts;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;

namespace Xtensive.Project109.Host.DPA
{
	public class EventsToDatabaseHandler : Signals2HandlerBase
	{
		private readonly DatabaseAdapter dbAdapter = new DatabaseAdapter(EventsToDatabaseSensitiveConfig.TARGET_DATABASE_CONNECTION);
		private readonly IEventSource eventSource;
		private readonly IDpaChannelManagerResolver managerResolver;
		private readonly IHostLog<EventsToDatabaseTrigger> logger;

		public void WriteSuccessToDriver(Equipment equipment)
		{
			var driverId = equipment.DriverIdentifier;
			var serverName = equipment.Server.Name;
			var driverManager = managerResolver.GetChannelManager(serverName);
			driverManager.WriteVariableByUrl(driverId, EventsToDatabaseConfig.SuccessResultUrl, new[] { EventsToDatabaseConfig.SuccessResultValue });
		}

		public override async Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var newEvent = ((ObjectChanged<SharedEventInfo>)args.Obj).NewValue;
			var workcenter = Query.All<Equipment>()
				.Where(x => x.DriverIdentifier == newEvent.DriverIdentifier)
				.Single();

			Dictionary<Guid, Func<SharedEventInfo, string, DateTimeOffset, DataTable>> builders;
			if (EventsToDatabaseConfig.TableBuilders.TryGetValue(newEvent.DriverIdentifier, out builders)) {
				foreach (var builder in builders) {
					var lastEvent = eventSource
						.PreviousEventsOf<SharedEventInfo>()
						.Where(x => x.EventIdentifier == builder.Key)
						.LastOrDefault();
					var table = builder.Value(lastEvent, workcenter.Name, newEvent.TimeStamp);
					await dbAdapter.WriteAsync(table);
				}
				WriteSuccessToDriver(workcenter);
			}
		}

		public EventsToDatabaseHandler(IEventSource eventSource, IDpaChannelManagerResolver managerResolver, IHostLog<EventsToDatabaseTrigger> logger)
		{
			this.eventSource = eventSource;
			this.managerResolver = managerResolver;
			this.logger = logger;
		}
	}
}