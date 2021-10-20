using DPA.Adapter.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;

namespace Xtensive.Project109.Host.DPA
{
	public class EventsToDatabaseHandler : Signals2HandlerBase
	{
		private readonly DatabaseAdapter dbAdapter = new DatabaseAdapter(EventsToDatabaseSensitiveConfig.TARGET_DATABASE_CONNECTION);

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var newEvent = ((ObjectChanged<SharedEventInfo>)args.Obj).NewValue;
			var workcenterName = Query.All<Equipment>()
				.Where(x => x.DriverIdentifier == newEvent.DriverIdentifier)
				.Select(x => x.Name)
				.Single();

			Func<SharedEventInfo, string, System.Data.DataTable> builder;
			if (!EventsToDatabaseConfig.TableBuilders.TryGetValue(newEvent.EventIdentifier, out builder)) {
				return Task.CompletedTask;
			}

			var table = builder(newEvent, workcenterName);

			return dbAdapter.WriteAsync(table);
		}
	}
}