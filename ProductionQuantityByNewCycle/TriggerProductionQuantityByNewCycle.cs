using DPA.Core.Contracts;
using DPA.Core.Contracts.Planning;
using DPA.Planning.Client;
using DPA.Planning.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;
using Xtensive.Project109.Host.Security;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class TriggerProductionQuantityByNewCycle : Signals2TriggerBase
	{
		private readonly ILogger<TriggerProductionQuantityByNewCycle> logger;
		private readonly ISystemSessionExecutor systemSession;
		private IJobNotificationService jobNotificationService;
		private IJobDataClient jobClient;
		private IDisposable subUpdated;
		private IDisposable subRunned;
		private IDisposable subStopped;
		private IDisposable subRemoved;

		private ConcurrentDictionary<long, JobCompletionCycleRunModel[]> ActiveJobs = new ConcurrentDictionary<long, JobCompletionCycleRunModel[]>();

		public TriggerProductionQuantityByNewCycle(IServiceProvider serviceProvider)
		{
			jobNotificationService = serviceProvider.GetService<IJobNotificationService>();
			jobClient = serviceProvider.GetRequiredService<IJobDataClient>();
			logger = serviceProvider.GetRequiredService<ILogger<TriggerProductionQuantityByNewCycle>>();
			systemSession = serviceProvider.GetRequiredService<ISystemSessionExecutor>();
		}
		public override Task StartAsync()
		{
			subUpdated = jobNotificationService.Subscribe<JobUpdatedEvent>(HandleUpdate);

			subRemoved = jobNotificationService.Subscribe<JobsRemovedEvent>((dto) => {
				foreach (var jobId in dto.JobIds) {
					TryRemove(jobId);
				}
			});
			subStopped = jobNotificationService.Subscribe<JobStoppedEvent>((dto) => {
				TryRemove(dto.Job.Id);
			});
			subRunned = jobNotificationService.Subscribe<JobRunnedEvent>((dto) => {
				var jobInfo = jobClient.GetJobCompletionInfo(dto.Job.Id);
				ActiveJobs.TryAdd(dto.Job.Id, jobInfo.CycleRuns);
			});

			return Task.CompletedTask;
		}

		private void TryRemove(long jobId)
		{
			JobCompletionCycleRunModel[] cycleRuns;
			ActiveJobs.TryRemove(jobId, out cycleRuns);
		}

		private void HandleUpdate(JobUpdatedEvent dto)
		{
			systemSession.Execute(() => {
				JobCompletionCycleRunModel[] cycleRuns;
				var jobCurrent = jobClient.GetJobCompletionInfo(dto.Job.Id);
				if (ActiveJobs.TryGetValue(dto.Job.Id, out cycleRuns)) {
					if (cycleRuns != null && jobCurrent.CycleRuns.Length > cycleRuns.Length) {
						if (dto.Job.EquipmentId != null)
							Handler(dto.Job.EquipmentId.Value, jobCurrent.Id, 1, ReleaseQualityMark.Undefined);
					}
					ActiveJobs[dto.Job.Id] = jobCurrent.CycleRuns;
				}
				else {
					if (jobCurrent != null)
						ActiveJobs.TryAdd(dto.Job.Id, jobCurrent.CycleRuns);
				}
			});
		}

		private void Handler(long equipmentId, long jobId, decimal quantity, ReleaseQualityMark quality)
		{
			logger.LogInformation(equipmentId.ToString());

			OnSignal(new ProductionQuantityByNewCycle {
				EquipmentId = equipmentId,
				JobId = jobId,
				Quantity = quantity,
				Quality = quality,
			});
		}

		public override Task StopAsync()
		{
			subRemoved.Dispose();
			subRunned.Dispose();
			subStopped.Dispose();
			subUpdated.Dispose();

			ActiveJobs.Clear();

			return Task.CompletedTask;
		}
	}
}
