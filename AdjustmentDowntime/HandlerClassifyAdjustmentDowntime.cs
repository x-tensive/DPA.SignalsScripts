using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Microsoft.Extensions.Logging;
using Xtensive.DPA.Contracts;
using System.Collections.Generic;
using Xtensive.Project109.Host.Security;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerClassifyAdjustmentDowntime : Signals2HandlerBase
	{
		private readonly ILogger<HandlerClassifyAdjustmentDowntime> logger;
		private readonly IIndicatorContext indicatorContext;
		private readonly IDowntimeReasonService downtimeReasonService;

		public HandlerClassifyAdjustmentDowntime(IServiceProvider serviceProvider)
		{
			logger = serviceProvider.GetRequiredService<ILogger<HandlerClassifyAdjustmentDowntime>>();
			indicatorContext = serviceProvider.GetRequiredService<IIndicatorContext>();
			downtimeReasonService = serviceProvider.GetRequiredService<IDowntimeReasonService>();
		}
		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.LogInformation(args.ToString());
			if (args.Obj is DowntimeInfoActionDto) {
				var action = (DowntimeInfoActionDto)args.Obj;
				var equipmentId = action.Reasons.First().EquipmentId;

				ClassifySegments(action.RecordId, equipmentId);
			}

			return Task.CompletedTask;
		}

		private void ClassifySegments(long recordId, long equipmentId)
		{
			var downtimeInfo = Query.All<DowntimeInfo>().FirstOrDefault(info => info.Record.Id == recordId);
			if (downtimeInfo == null) {
				return;
			}

			if (downtimeInfo.Record.Type != MachineStateType.Adjustment) {
				return;
			}

			var downtimeReasons = downtimeInfo.DowntimeReasons.ToList();
			var downtimeSegments = new DateTimeSegments(downtimeReasons.Select(r => new DateTimeSegment(r.StartDate, r.EndDate.Value)));

			var classifiedDowntimeSegments = new DateTimeSegments();

			RecordModel recordModel = new RecordModel(downtimeInfo);

			foreach (var reason in AdjustmentDowntimeCreator.Reasons) {
				AdjustmentDowntimeReason adjustmentDowntimeReason = AdjustmentDowntimeCreator
					.Create(indicatorContext, reason, equipmentId, downtimeInfo.Record.StartDate, downtimeInfo.Record.EndDate.Value);

				if (adjustmentDowntimeReason.Segments != null && adjustmentDowntimeReason.Segments.Count() > 0) {
					classifiedDowntimeSegments.AddRange(adjustmentDowntimeReason.Segments);
					foreach (var dtSegment in adjustmentDowntimeReason.Segments) {
						var newReason = new DowntimeReasonModel() {
							ReasonId = adjustmentDowntimeReason.Reason.Id,
							EquipmentId = equipmentId,
							StartDate = dtSegment.StartDate,
							EndDate = dtSegment.EndDate
						};
						recordModel.Reasons.Add(newReason);
					}
				}
			}

			if (classifiedDowntimeSegments.Count() > 0) {
				downtimeSegments.Exclude(classifiedDowntimeSegments);
				foreach (var dtSegment in downtimeSegments) {
					var dtReason = downtimeReasons.First(s => dtSegment.StartDate >= s.StartDate && dtSegment.EndDate <= s.EndDate);
					var reason = dtReason.Reason;
					var newReason = new DowntimeReasonModel(dtReason) {
						StartDate = dtSegment.StartDate,
						EndDate = dtSegment.EndDate,
						TimeStamp = DateTime.UtcNow
					};

					if (dtReason.User != null) {
						newReason.User = new UserModel(dtReason.User);
					}

					recordModel.Reasons.Add(newReason);
				}
			}

			if (recordModel.Reasons.Count() > 0) {
				downtimeReasonService.Remove(downtimeReasons);
				var refBookReasonsIds = recordModel.Reasons.Select(m => m.ReasonId).ToList();
				var refBookReasons = Query.All<ReferenceBookReasonsOfDowntime>().Where(r => refBookReasonsIds.Contains(r.Id)).ToList();
				foreach (var reasonModel in recordModel.Reasons.OrderBy(c => c.StartDate)) {
					var refBookReason = refBookReasons.First(r => r.Id == reasonModel.ReasonId);
					downtimeReasonService.Create(downtimeInfo, refBookReason, reasonModel.StartDate, reasonModel.EndDate, reasonModel.OperatorComment);
				}
			}
		}

		private class RecordModel
		{
			public long EquipmentId { get; }
			public DateTimeOffset Start { get; }
			public DateTimeOffset End { get; }
			public MachineStateType Type { get; }
			public List<DowntimeReasonModel> Reasons { get; set; } = new List<DowntimeReasonModel>();

			public RecordModel(DowntimeInfo downtimeInfo)
			{
				EquipmentId = downtimeInfo.Record.Equipment.Id;
				Start = downtimeInfo.Record.StartDate;
				End = downtimeInfo.Record.EndDate ?? throw new InvalidOperationException("This model should be used only for closed records");
				Type = downtimeInfo.Record.Type;
			}
		}
	}
}
