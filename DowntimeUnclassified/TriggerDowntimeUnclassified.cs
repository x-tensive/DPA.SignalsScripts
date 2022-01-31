using DPA.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class TriggerDowntimeUnclassified : Signals2TriggerBase
	{
		private SettingsDowntimeUnclassified settings;
		private readonly ILogger<TriggerDowntimeUnclassified> logger;
		private readonly IDateTimeOffsetProvider dateTimeOffsetProvider;
		private readonly IDowntimeReasonService downtimeReasonService;
		private ConcurrentDictionary<long, ConcurrentDictionary<long, TriggerDowntimeUnclassifiedItem>> equipmentUnclassifiedReasons;
		private IDisposable subscription;
		private CancellationTokenSource cts;
		private Task watcherTask;

		public TriggerDowntimeUnclassified(IServiceProvider serviceProvider)
		{
			downtimeReasonService = serviceProvider.GetRequiredService<IDowntimeReasonService>();
			logger = serviceProvider.GetRequiredService<ILogger<TriggerDowntimeUnclassified>>();
			dateTimeOffsetProvider = serviceProvider.GetRequiredService<IDateTimeOffsetProvider>();
			settings = new SettingsDowntimeUnclassified();
			settings.EquipmentsSettings = settings.EquipmentsSettings.ToDictionary(x => x.Key, x => x.Value.OrderBy(y => y.Duration).ToList());
		}
		public override Task StartAsync()
		{
			cts = new CancellationTokenSource();
			equipmentUnclassifiedReasons = new ConcurrentDictionary<long,
			ConcurrentDictionary<long, TriggerDowntimeUnclassifiedItem>>();
			subscription = downtimeReasonService.Subscribe(HandleReasonEvent);
			watcherTask = Task.Run(() => Worker(cts.Token), cts.Token);
			return Task.CompletedTask;
		}
		private void HandleReasonEvent(DowntimeReasonActionDto action)
		{
			logger.Info(string.Format("DowntimeReason {0} {1} {2}", action.Id, action.StartDate, action.EndDate));
			if (!settings.EquipmentsSettings.ContainsKey(action.EquipmentId)) {
				logger.Info(string.Format("skip equipmentId [{0}]", action.EquipmentId));
				return;
			}

			ConcurrentDictionary<long, TriggerDowntimeUnclassifiedItem> reasons;
			if (!equipmentUnclassifiedReasons.TryGetValue(action.EquipmentId, out reasons)) {
				reasons = new ConcurrentDictionary<long, TriggerDowntimeUnclassifiedItem>();
				equipmentUnclassifiedReasons.TryAdd(action.EquipmentId, reasons);
			}
			if (action.Status == DowntimeStatus.Ignored) {
				if (action is DowntimeReasonCreatedActionDto) {
					reasons.TryAdd(action.Id, new TriggerDowntimeUnclassifiedItem(action));
				}
				else if (action is DowntimeReasonChangedActionDto) {
					reasons.AddOrUpdate(action.Id, new TriggerDowntimeUnclassifiedItem(action), (rId, a) => { a.Reason = action; return a; });
				}
				else if (action is DowntimeReasonRemovedActionDto) {
					TriggerDowntimeUnclassifiedItem dto;
					reasons.TryRemove(action.Id, out dto);
				}
			}
			else {
				TriggerDowntimeUnclassifiedItem dto;
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
						List<EquipmentSettingsDowntimeUnclassified> equipmentSettings;
						if (settings.EquipmentsSettings.TryGetValue(equipmentId, out equipmentSettings)) {
							var deleteIds = new List<long>();
							try {
								foreach (var reason in equipmentReasons.Value) {
									token.ThrowIfCancellationRequested();
									var t = now - reason.Value.Reason.StartDate;//EndDate
									var end = reason.Value.Reason.EndDate.HasValue ? reason.Value.Reason.EndDate.Value : now;
									var isIgnore = (end - reason.Value.Reason.StartDate) < settings.UnclassifiedIgnoreDuration;

									for (var i = equipmentSettings.Count - 1; i >= 0; i--) {
										if (t > equipmentSettings[i].Duration) {
											logger.Info(equipmentId);
											if (reason.Value.LastLevelIdSend == i - 1 && !isIgnore) {
												OnSignal(new CommonDowntimeUnclassified {
													EquipmentId = equipmentId,
													StartDate = reason.Value.Reason.StartDate,
													ReasonId = reason.Value.Reason.Id,
													LevelId = i
												});
												reason.Value.LastLevelIdSend = i;
											}
											if (i == equipmentSettings.Count - 1)
												deleteIds.Add(reason.Key);
										}
									}
								}
								foreach (var rId in deleteIds) {
									TriggerDowntimeUnclassifiedItem dto;
									equipmentReasons.Value.TryRemove(rId, out dto);
								}
							}
							catch (Exception e) {
								logger.Error(e);
								OnSignalError(e.Message);
							}
						}
					}
				}
				catch (Exception ex) {
					logger.Error(ex);
					OnSignalError(ex.Message);
				}
				await Task.Delay(settings.WorkerDelay, token);
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
		private class TriggerDowntimeUnclassifiedItem
		{
			public DowntimeReasonActionDto Reason { get; set; }
			public int LastLevelIdSend { get; set; }
			public TriggerDowntimeUnclassifiedItem(DowntimeReasonActionDto reason, int lastLevelIdSend = -1)
			{
				Reason = reason;
				LastLevelIdSend = lastLevelIdSend;
			}
		}
	}
}
