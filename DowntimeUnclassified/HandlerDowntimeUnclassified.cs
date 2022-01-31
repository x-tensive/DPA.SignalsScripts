using DPA.Messenger.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerDowntimeUnclassified : Signals2HandlerBase
	{
		private SettingsDowntimeUnclassified settings;
		private readonly NotificationMessageTaskBuilder notificationMessageTaskBuilder;
		private readonly ILogger<HandlerDowntimeUnclassified> logger;
		public HandlerDowntimeUnclassified(IServiceProvider serviceProvider)
		{
			notificationMessageTaskBuilder = serviceProvider.GetRequiredService<NotificationMessageTaskBuilder>();
			logger = serviceProvider.GetRequiredService<ILogger<HandlerDowntimeUnclassified>>();
			settings = new SettingsDowntimeUnclassified();
			settings.EquipmentsSettings = settings.EquipmentsSettings.ToDictionary(x => x.Key, x => x.Value.OrderBy(y => y.Duration).ToList());
		}
		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.Info(args);
			if (args.Obj is CommonDowntimeUnclassified) {
				var obj = (CommonDowntimeUnclassified)args.Obj;

				var reason = Query.SingleOrDefault<DowntimeReason>(obj.ReasonId);
				if (reason != null) {
					var record = reason.DowntimeInfo.Record;
					if (record != null && record.Type == Xtensive.DPA.Contracts.MachineStateType.SwitchedOn) {
						List<EquipmentSettingsDowntimeUnclassified> equipmentSettings;
						if (settings.EquipmentsSettings.TryGetValue(obj.EquipmentId, out equipmentSettings)) {
							var levelSettings = equipmentSettings[obj.LevelId];
							var personnelNumbers = new List<string>();
							if (levelSettings.PersonnelNumbers != null && levelSettings.PersonnelNumbers.Any())
								personnelNumbers.AddRange(levelSettings.PersonnelNumbers);
							if (levelSettings.GroupId.HasValue) {
								var group = Query.Single<Xtensive.Project109.Host.Security.Group>(levelSettings.GroupId);
								var pn = group.Childs.OfType<DpaUser>().Select(c => c.PersonnelNumber).ToArray();
								if (pn != null && pn.Any())
									personnelNumbers.AddRange(pn);
							}

							var users = Query.All<DpaUser>().Where(x => x.PersonnelNumber.In(personnelNumbers));
							var equipmentName = Query.Single<Equipment>(obj.EquipmentId).Name;
							var template = Query.All<MessageTemplate>().Single(t => t.Id == levelSettings.TemplateId);

							notificationMessageTaskBuilder.BuildAndScheduleMessages(
								MessageTransportType.Email,
								template,
								users.Distinct(),
								() => new Dictionary<string, string> { { "EquipmentName", equipmentName }, { "DonwtimeReasonForSignal.EventStartTime", obj.StartDate.ToString() } },
								"Signals 2.0"
							);
						}
						else {
							logger.Info(string.Format("not found settings for equipmentId [{0}]", obj.EquipmentId));
						}
					}
					else {
						logger.Info(string.Format("skip DowntimeReasonId [{0}],  record type [{1}]", obj.ReasonId, record.Type));
					}
				}
				else {
					logger.Info(string.Format("not found DowntimeReasonId [{0}]", obj.ReasonId));
				}
			}
			return Task.CompletedTask;
		}
	}
}
