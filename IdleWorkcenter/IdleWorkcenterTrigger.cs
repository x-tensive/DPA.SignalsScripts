using DPA.Adapter.Contracts;
using DPA.Adapter.Contracts.Compare;
using DPA.Core.Contracts.Planning;
using DPA.Core.DependencyInjection;
using DPA.Core.Services;
using DPA.Planning.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Timers;
using Xtensive.DPA.EventManager;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	/// <summary>
	/// Fires if workcenter has started job, but workcenter itself does not sends any events to dpa host during period of {MAX_IDLE_TIME_IN_SECONDS} seconds 
	/// </summary>
	public class IdleWorkcenterTrigger : Signals2TriggerBase
	{
		/// <summary>
		/// Maximum allowed idle duration
		/// </summary>
		public int MAX_IDLE_TIME_IN_SECONDS = 15 * 60;
		private TimeSpan GetMaxIdleDuration()
		{
			return TimeSpan.FromSeconds(MAX_IDLE_TIME_IN_SECONDS);
		}

		private class DriverState : IDisposable
		{
			private readonly Timer timer;
			private bool jobIsRunning = false;
			private static readonly SharedEventInfoComparator sharedEventComparator = new SharedEventInfoComparator();
			private DateTimeOffset eventTimeStamp = DateTimeOffset.MinValue;

			internal DriverState JobStopped()
			{
				lock (timer) {
					jobIsRunning = false;
					timer.Stop();
				}
				return this;
			}

			internal DriverState JobStarted()
			{
				lock (timer) {
					jobIsRunning = true;
					timer.Start();
				}
				return this;
			}

			private bool AreEquals(EventInfo left, EventInfo right)
			{
				var asSharedEvent = left as SharedEventInfo;
				if (asSharedEvent != null) {
					return sharedEventComparator.AreEqual(asSharedEvent, (SharedEventInfo)right);
				}
				//all events except SharedEventInfo are compared automatically
				return false;
			}

			internal DriverState ProcessEvent(ObjectChanged<EventInfo> eventInfo)
			{
				var hasChanged = !AreEquals(eventInfo.OldValue, eventInfo.NewValue);
				if (hasChanged) {
					lock (timer) {
						eventTimeStamp = eventInfo.NewValue.TimeStamp;
						if (jobIsRunning) {
							timer.Stop();
							timer.Start();
						}
					}
				}
				return this;
			}

			public void Dispose()
			{
				timer.Stop();
				timer.Dispose();
			}

			public DriverState(
				TimeSpan maxIdleDuration,
				Action<object> handler,
				Guid driverId,
				ILogger logger,
				bool jobIsRunning = false)
			{
				timer = new Timer(maxIdleDuration.TotalMilliseconds) { AutoReset = true };
				timer.Elapsed += (sender, e) => {
					logger.LogDebug("Driver is not responding for too long " + driverId.ToString());
					handler(Tuple.Create(driverId, eventTimeStamp));
				};
				if (jobIsRunning) {
					this.jobIsRunning = true;
					timer.Start();
				}
			}
		}

		private readonly IEventSource eventSource;
		private readonly IJobNotificationService jobNotificationService;
		private readonly IJobDataClient jobClient;
		private readonly ILogger<IdleWorkcenterTrigger> logger;
		private readonly IDateTimeOffsetProvider timeProvider;
		private readonly IInScopeExecutor<IEquipmentDataService> equipmentService;
		private readonly ConcurrentDictionary<Guid, DriverState> driversStates = new ConcurrentDictionary<Guid, DriverState>();
		private readonly ConcurrentDictionary<long, Guid> equipments = new ConcurrentDictionary<long, Guid>();
		private readonly List<IDisposable> subscriptions = new List<IDisposable>();

		public IdleWorkcenterTrigger(
			IEventSource eventSource,
			IJobNotificationService jobNotificationService,
			IJobDataClient jobClient,
			ILogger<IdleWorkcenterTrigger> logger,
			IDateTimeOffsetProvider timeProvider,
			IInScopeExecutor<IEquipmentDataService> equipmentService)
		{
			this.eventSource = eventSource;
			this.jobNotificationService = jobNotificationService;
			this.jobClient = jobClient;
			this.logger = logger;
			this.timeProvider = timeProvider;
			this.equipmentService = equipmentService;
		}

		public override Task StartAsync()
		{
			Func<JobEventBase, bool> isProduction = x => x.Job.JobType == JobType.Production;

			subscriptions.Add(jobNotificationService.Subscribe<JobRunnedEvent>(isProduction, JobStarted));
			subscriptions.Add(jobNotificationService.Subscribe<JobResumedEvent>(isProduction, JobStarted));

			subscriptions.Add(jobNotificationService.Subscribe<JobSuspendedEvent>(isProduction, JobStopped));
			subscriptions.Add(jobNotificationService.Subscribe<JobCompletedEvent>(isProduction, JobStopped));

			subscriptions.Add(eventSource.EventsOf<ObjectChanged<EventInfo>>().Where(x => !(x.NewValue is MessageEventInfo)).Subscribe(CheckEvent));

			Initialize();

			return Task.CompletedTask;
		}

		private void AddOrUpdateDriverState(Guid driverId, Func<DriverState, DriverState> updater)
		{
			DriverState tempState = null;
			var resultState = driversStates.AddOrUpdate(
				driverId,
				(key) => {
					if (tempState != null) {
						tempState.Dispose();
					}
					tempState = new DriverState(GetMaxIdleDuration(), OnSignal, driverId, logger);
					return updater(tempState);
				},
				(key, old) => updater(old)
			);
			if (tempState != null && resultState != tempState) {
				tempState.Dispose();
			}
		}

		private void JobChanged(long equipmentId, Func<DriverState, DriverState> handler)
		{
			Guid driverId;
			if (!equipments.TryGetValue(equipmentId, out driverId)) {
				driverId = equipmentService.ExecuteRead(x => {
					if (x.TryGetAvailableEquipment<EquipmentThinModel>(equipmentId, out var equipment)) {
						return equipment.DriverIdentifier;
					}
					throw new HasNoRightsForEquipment(equipmentId);
				});
				equipments.TryAdd(equipmentId, driverId);
			}
			AddOrUpdateDriverState(driverId, x => handler(x));
		}

		private void Initialize()
		{
			var equipmentList = equipmentService.ExecuteRead(x => x.GetAvailableEquipments<EquipmentThinModel>().ToDictionary(eq => eq.Id, eq => eq.DriverIdentifier));
			var equipmentsWithStartedJobs = jobClient
				.GetStartedProductionBy(equipmentList.Keys)
				.Select(x => new { Id = x.Equipment.Value, DriverIdentifier = equipmentList[x.Equipment.Value] })
				.Distinct()
				.ToArray();

			foreach (var equipment in equipmentsWithStartedJobs) {
				equipments.TryAdd(equipment.Id, equipment.DriverIdentifier);
				var newDriverState = new DriverState(GetMaxIdleDuration(), OnSignal, equipment.DriverIdentifier, logger, true);
				if (!driversStates.TryAdd(equipment.DriverIdentifier, newDriverState)) {
					newDriverState.Dispose();
				}
			}
		}

		private void JobStarted(JobEventBase job)
		{
			JobChanged(job.Job.EquipmentId.Value, driverState => driverState.JobStarted());
		}

		private void JobStopped(JobEventBase job)
		{
			JobChanged(job.Job.EquipmentId.Value, driverState => driverState.JobStopped());
		}

		public void CheckEvent(ObjectChanged<EventInfo> eventInfo)
		{
			AddOrUpdateDriverState(eventInfo.NewValue.DriverIdentifier, x => x.ProcessEvent(eventInfo));
		}

		public override Task StopAsync()
		{
			subscriptions.ForEach(x => x.Dispose());
			subscriptions.Clear();

			driversStates.Values.ToList().ForEach(x => x.Dispose());
			driversStates.Clear();
			equipments.Clear();

			return Task.CompletedTask;
		}
	}
}
