using DPA.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;
namespace Xtensive.Project109.Host.DPA
{
	public class ZFTriggerDowntimeUnclassified : Signals2TriggerBase
	{
		private TimeSpan workerDelay = TimeSpan.FromMinutes(1);
		private TimeSpan unclassifiedIgnoreDuration = TimeSpan.FromMinutes(7);
		private TimeSpan[] unclassifiedLevelDuration = new[] { TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(240) };
		private readonly IHostLog<ZFTriggerDowntimeUnclassified> logger;
		private readonly IDateTimeOffsetProvider dateTimeOffsetProvider;
		private readonly IDowntimeReasonService downtimeReasonService;
		private ConcurrentDictionary<long, ConcurrentDictionary<long, ZFTriggerDowntimeUnclassifiedItem>> equipmentUnclassifiedReasons;
		private IDisposable subscription;
		private CancellationTokenSource cts;
		private Task watcherTask;

		public ZFTriggerDowntimeUnclassified(IServiceProvider serviceProvider)
		{
			downtimeReasonService = serviceProvider.GetRequiredService<IDowntimeReasonService>();
			logger = serviceProvider.GetRequiredService<IHostLog<ZFTriggerDowntimeUnclassified>>();
			dateTimeOffsetProvider = serviceProvider.GetRequiredService<IDateTimeOffsetProvider>();
		}
		public override Task StartAsync()
		{
			cts = new CancellationTokenSource();
			equipmentUnclassifiedReasons = new ConcurrentDictionary<long,
			ConcurrentDictionary<long, ZFTriggerDowntimeUnclassifiedItem>>();
			subscription = downtimeReasonService.Subscribe(HandleReasonEvent);
			watcherTask = Task.Run(() => Worker(cts.Token), cts.Token);
			return Task.CompletedTask;
		}
		private void HandleReasonEvent(DowntimeReasonActionDto action)
		{
			logger.Info(string.Format("DowntimeReason {0} {1} {2}", action.Id, action.StartDate, action.EndDate));
			ConcurrentDictionary<long, ZFTriggerDowntimeUnclassifiedItem> reasons;
			if (!equipmentUnclassifiedReasons.TryGetValue(action.EquipmentId, out reasons)) {
				reasons = new ConcurrentDictionary<long, ZFTriggerDowntimeUnclassifiedItem>();
				equipmentUnclassifiedReasons.TryAdd(action.EquipmentId, reasons);
			}
			if (action.Status == DowntimeStatus.Ignored) {
				if (action is DowntimeReasonCreatedActionDto) {
					reasons.TryAdd(action.Id, new ZFTriggerDowntimeUnclassifiedItem(action));
				}
				else if (action is DowntimeReasonChangedActionDto) {
					reasons.AddOrUpdate(action.Id, new ZFTriggerDowntimeUnclassifiedItem(action), (rId, a) => { a.Reason = action; return a; });
				}
				else if (action is DowntimeReasonRemovedActionDto) {
					ZFTriggerDowntimeUnclassifiedItem dto;
					reasons.TryRemove(action.Id, out dto);
				}
			}
			else {
				ZFTriggerDowntimeUnclassifiedItem dto;
				reasons.TryRemove(action.Id, out dto);
			}
		}
		private async Task Worker(CancellationToken token)
		{
			while (true) {
				token.ThrowIfCancellationRequested();
				try {
					var now = dateTimeOffsetProvider.Now;
					foreach (var equipmentReasons in equipmentUnclassifiedReasons) {
						var equipmentId = equipmentReasons.Key;
						var deleteIds = new List<long>();
						try {
							foreach (var reason in equipmentReasons.Value) {
								token.ThrowIfCancellationRequested();
								var t = now - reason.Value.Reason.StartDate;//EndDate
								var end = reason.Value.Reason.EndDate.HasValue ? reason.Value.Reason.EndDate.Value : now;
								var isIgnore = (end - reason.Value.Reason.StartDate) < unclassifiedIgnoreDuration;

								for (var i = unclassifiedLevelDuration.Length-1; i >=0; i--) {
									if (t > unclassifiedLevelDuration[i]) {
										logger.Info(equipmentId);
										if (reason.Value.LastLevelIdSend == i - 1 && !isIgnore) {
											OnSignal(new ZFDowntimeUnclassified {
												EquipmentId = equipmentId,
												StartDate=reason.Value.Reason.StartDate,
												ReasonId = reason.Value.Reason.ReasonId,
												LevelId = i
											});
											reason.Value.LastLevelIdSend = i;
										}
										if(i == unclassifiedLevelDuration.Length - 1)
											deleteIds.Add(reason.Key);
									}
								}
							}
							foreach (var rId in deleteIds) {
								ZFTriggerDowntimeUnclassifiedItem dto;
								equipmentReasons.Value.TryRemove(rId, out dto);
							}
						}
						catch (Exception e) {
							logger.Error(e);
							OnSignalError(e.Message);
						}
					}
				}
				catch (Exception ex) {
					logger.Error(ex);
					OnSignalError(ex.Message);
				}
				await Task.Delay(workerDelay, token);
			}
		}
		public override Task StopAsync()
		{
			if (cts != null)
				cts.Cancel();
			if (subscription != null)
				subscription.Dispose();
			subscription = null;
			equipmentUnclassifiedReasons = null;
			if (cts != null)
				cts.Dispose();
			return Task.CompletedTask;
		}
		private class ZFTriggerDowntimeUnclassifiedItem
		{
			public DowntimeReasonActionDto Reason { get; set; }
			public int LastLevelIdSend { get; set; }
			public ZFTriggerDowntimeUnclassifiedItem(DowntimeReasonActionDto reason, int lastLevelIdSend = -1)
			{
				Reason = reason;
				LastLevelIdSend = lastLevelIdSend;
			}
		}
	}
}