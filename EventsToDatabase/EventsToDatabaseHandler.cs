using DPA.Adapter.Contracts;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.EventManager;

namespace Xtensive.Project109.Host.DPA.Tests.Signals2.Scripts.DPA.SignalsScripts.EventsToDatabase
{
	public class EventsToDatabaseHandler : Signals2HandlerBase
	{
		private readonly DatabaseAdapter dbAdapter = new DatabaseAdapter(EventsToDatabaseSensitiveConfig.TARGET_DATABASE_CONNECTION);
		private readonly IEquipmentService equipmentService;
		public EventsToDatabaseHandler(IEquipmentService equipmentService)
		{
			this.equipmentService = equipmentService;
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var newEvent = ((ObjectChanged<SharedEventInfo>)args.Obj).NewValue;
			var workcenterName = equipmentService.Get()
				.Where(x => x.DriverIdentifier == newEvent.DriverIdentifier)
				.Select(x => x.Name)
				.Single();

			if (!EventsToDatabaseConfig.TableBuilders.TryGetValue(newEvent.EventName, out var build)) {
				return Task.CompletedTask;
			}

			var table = build(newEvent, workcenterName);

			return dbAdapter.WriteAsync(table);
		}
	}
}
