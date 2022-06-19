using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Microsoft.Extensions.Logging;
using Xtensive.DPA.Contracts;
using System.Collections.Generic;
using Xtensive.Project109.Host.Security;
using Xtensive.Core;
using Xtensive.Project109.Host.Base;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerClassifyAdjustmentDowntime : Signals2HandlerBase
	{
		private readonly ILogger<HandlerClassifyAdjustmentDowntime> logger;
		private readonly IIndicatorContext indicatorContext;
		private readonly IDowntimeInfoService downtimeInfoService;

		public HandlerClassifyAdjustmentDowntime(IServiceProvider serviceProvider)
		{
			logger = serviceProvider.GetRequiredService<ILogger<HandlerClassifyAdjustmentDowntime>>();
			indicatorContext = serviceProvider.GetRequiredService<IIndicatorContext>();
			downtimeInfoService = serviceProvider.GetRequiredService<IDowntimeInfoService>();
		}
		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			logger.LogInformation(args.ToString());
			if (args.Obj is DowntimeInfoActionDto dto) {
				
				if (!dto.Reasons.IsNullOrEmpty()) {
					var equipmentId = dto.Reasons.First().EquipmentId;
					ClassifySegments(dto.RecordId, equipmentId);
				}
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

			if (!downtimeInfo.Record.EndDate.HasValue) {
				logger.LogError(string.Format("Trying to classify not closed periods. DowntimeInfo: {0}", downtimeInfo.Id));
				return;
			}

			var sourceDowntimeReasons = downtimeInfo.DowntimeReasons.ToList();
			var downtimeSegments = new DateTimeSegments(sourceDowntimeReasons.Select(r => new DateTimeSegment(r.StartDate, r.EndDate.Value)));

			var classifiedDowntimeSegments = new DateTimeSegments();
			var downtimeInfoModel = new DowntimeInfoModel(downtimeInfo, loadReasons: false);

			foreach (var reason in AdjustmentDowntimeCreator.Reasons) {
				AdjustmentDowntimeReason adjustmentDowntimeReason = AdjustmentDowntimeCreator
					.Create(indicatorContext, reason, equipmentId, downtimeInfo.Record.StartDate, downtimeInfo.Record.EndDate.Value);

				if (!adjustmentDowntimeReason.Segments.IsNullOrEmpty()) {
					classifiedDowntimeSegments.AddRange(adjustmentDowntimeReason.Segments);
					foreach (var dtSegment in adjustmentDowntimeReason.Segments) {
						var newDtReason = new DowntimeReasonModel() {
							ReasonId = adjustmentDowntimeReason.Reason.Id,
							EquipmentId = equipmentId,
							StartDate = dtSegment.StartDate,
							EndDate = dtSegment.EndDate
						};

						downtimeInfoModel.Reasons.Add(newDtReason);
					}
				}
			}

			if (classifiedDowntimeSegments.Any()) {
				downtimeSegments.Exclude(classifiedDowntimeSegments);
				foreach (var dtSegment in downtimeSegments) {
					var sourceDtReasons = sourceDowntimeReasons
						.Select(r => {
							var dt = new DateTimeSegment(r.StartDate, r.EndDate.Value);
							var intersection = dtSegment.Intersect(dt);
							return new { reason = r, intersection?.StartDate, intersection?.EndDate };
						})
						.Where(r => r.StartDate != null);

					if (sourceDtReasons.IsNullOrEmpty()) {
						logger.LogError("Can't find source downtime reason for period");
						return;
					}

					foreach (var item in sourceDtReasons) {
						var newDtReason = new DowntimeReasonModel(item.reason) {
							StartDate = item.StartDate.Value,
							EndDate = item.EndDate.Value,
							TimeStamp = DateTime.UtcNow
						};

						if (item.reason.User != null) {
							newDtReason.User = new UserModel(item.reason.User);
						}

						downtimeInfoModel.Reasons.Add(newDtReason);
					}
					
				}
			}

			if (downtimeInfoModel.Reasons.Any()) {
				downtimeInfoService.Update(downtimeInfo.Record.Id, downtimeInfoModel);
			}
		}
	}
}
