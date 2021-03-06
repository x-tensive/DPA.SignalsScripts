using DPA.Adapter.Contracts;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class EventsToDatabaseHandler : Signals2HandlerBase
	{
		private readonly DatabaseAdapter dbAdapter = new DatabaseAdapter(EventsToDatabaseSensitiveConfig.TARGET_DATABASE_CONNECTION);
		private readonly IEventSource eventSource;
		private readonly IDpaChannelManagerResolver managerResolver;
		private readonly ILogger<EventsToDatabaseHandler> logger;

		public void WriteSuccessToDriver(Equipment equipment)
		{
			var driverId = equipment.DriverIdentifier;
			var serverName = equipment.Server.Name;
			var driverManager = managerResolver.GetChannelManager(serverName);
			driverManager.WriteVariableByUrl(driverId, EventsToDatabaseConfig.SuccessResultUrl, new[] { EventsToDatabaseConfig.SuccessResultValue });
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var newEvent = ((ObjectChanged<EventInfo>)args.Obj).NewValue;
			var workcenter = Query.All<Equipment>()
				.Where(x => x.DriverIdentifier == newEvent.DriverIdentifier)
				.Single();

			Dictionary<Guid, Func<EventInfo, string, DataTable>> builders;
			if (EventsToDatabaseConfig.TableBuilders.TryGetValue(newEvent.DriverIdentifier, out builders)) {
				foreach (var builder in builders) {
					var lastEvent = eventSource
						.PreviousEventsOf<EventInfo>()
						.Where(x => x.EventIdentifier == builder.Key)
						.LastOrDefault();
					var table = builder.Value(lastEvent, workcenter.Name);
					dbAdapter.WriteAsync(table).Wait();
				}
				WriteSuccessToDriver(workcenter);
			}
			return Task.CompletedTask;
		}

		public EventsToDatabaseHandler(IEventSource eventSource, IDpaChannelManagerResolver managerResolver, ILogger<EventsToDatabaseHandler> logger)
		{
			this.eventSource = eventSource;
			this.managerResolver = managerResolver;
			this.logger = logger;
		}
	}
}