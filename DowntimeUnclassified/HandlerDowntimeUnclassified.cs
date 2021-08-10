using DPA.Messenger.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
namespace Xtensive.Project109.Host.DPA
{
	public class ZFHandlerDowntimeUnclassified : Signals2HandlerBase
	{
		private ZFHandlerDowntimeUnclassifiedLevelSetting[] levelSettings = new[] { 
			//0 level
			new ZFHandlerDowntimeUnclassifiedLevelSetting {
				PersonnelNumbers = new string[] { "2015984033", "234" },
				TemplateId = 3639519
			},
			//1 level
			 new ZFHandlerDowntimeUnclassifiedLevelSetting {
				PersonnelNumbers = new string[] { "123", "234" },
				TemplateId = 3639519
			}
		};
		private readonly NotificationMessageTaskBuilder notificationMessageTaskBuilder;
		private readonly IHostLog<ZFHandlerDowntimeUnclassified> logger;
		public ZFHandlerDowntimeUnclassified(IServiceProvider serviceProvider)
		{
			notificationMessageTaskBuilder = serviceProvider.GetRequiredService<NotificationMessageTaskBuilder>();
			logger = serviceProvider.GetRequiredService<IHostLog<ZFHandlerDowntimeUnclassified>>();
		}
		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.Info(args);
			if (args.Obj is ZFDowntimeUnclassified) {
				var obj = (ZFDowntimeUnclassified)args.Obj;

				var reason = Query.SingleOrDefault<DowntimeReason>(obj.ReasonId);
				if (reason != null){
					var record =reason.DowntimeInfo.Record;
					if (record != null && record.Type == Xtensive.DPA.Contracts.MachineStateType.SwitchedOn) {
						var settings = levelSettings[obj.LevelId];// maybe exception
						var users = Query.All<DpaUser>().Where(x => x.PersonnelNumber.In(settings.PersonnelNumbers));
						var equipmentName = Query.Single<Equipment>(obj.EquipmentId).Name;
						var template = Query.All<MessageTemplate>().Single(t => t.Id == settings.TemplateId);

						notificationMessageTaskBuilder.BuildAndScheduleMessages(
						MessageTransportType.Email,
						template,
						users,
						() => new Dictionary<string, string> { { "EquipmentName", equipmentName }, { "DonwtimeReasonForSignal.EventStartTime", obj.StartDate.ToString() } },
						"Signals 2.0"
						);
					}
					else {
						logger.Info(string.Format("skip MachineStateRecordId [{0}]", obj.ReasonId));
					}
				}
				else {
					logger.Info(string.Format("skip MachineStateRecordId [{0}]", obj.ReasonId));
				}
			}
			return Task.CompletedTask;
		}

		class ZFHandlerDowntimeUnclassifiedLevelSetting
		{
			public string[] PersonnelNumbers { get; set; }
			public long TemplateId { get; set; }
		}
	}
}