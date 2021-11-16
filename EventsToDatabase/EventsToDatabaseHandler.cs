using DPA.Adapter.Contracts;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;

namespace Xtensive.Project109.Host.DPA
{
	public class EventsToDatabaseHandler : Signals2HandlerBase
	{
		private readonly DatabaseAdapter dbAdapter = new DatabaseAdapter(EventsToDatabaseSensitiveConfig.TARGET_DATABASE_CONNECTION);

		public override async Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var newEvent = ((ObjectChanged<SharedEventInfo>)args.Obj).NewValue;
			var workcenterName = Query.All<Equipment>()
				.Where(x => x.DriverIdentifier == newEvent.DriverIdentifier)
				.Select(x => x.Name)
				.Single();

			Dictionary<Guid, Func<SharedEventInfo, string, DataTable>> builders;
			if (EventsToDatabaseConfig.TableBuilders.TryGetValue(newEvent.DriverIdentifier, out builders)){
				foreach (var builder in builders) {
					var table = builder.Value(newEvent, workcenterName);
					await  dbAdapter.WriteAsync(table);
				}
			}
		}
	}
}