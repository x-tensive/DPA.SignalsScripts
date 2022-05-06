using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.Host.Contracts;
using Xtensive.Orm;
using Xtensive.Project109.Host.Security;

namespace Xtensive.Project109.Host.DPA
{
	public class TriggerProductionQuantityByNewCycle : Signals2TriggerBase
	{
		private readonly ILogger<TriggerProductionQuantityByNewCycle> logger;
		private readonly ISystemSessionExecutor systemSession;
		private IJobNotificationService jobNotificationService;
		private IDisposable subUpdated;
		private IDisposable subRunned;
		private IDisposable subStopped;
		private IDisposable subRemoved;

		private ConcurrentDictionary<long, JobTechnologyCycleRun[]> ActiveJobs = new ConcurrentDictionary<long, JobTechnologyCycleRun[]>();

		public TriggerProductionQuantityByNewCycle(IServiceProvider serviceProvider)
		{
			jobNotificationService = serviceProvider.GetRequiredService<IJobNotificationService>();
			logger = serviceProvider.GetRequiredService<ILogger<TriggerProductionQuantityByNewCycle>>();
			systemSession = serviceProvider.GetRequiredService<ISystemSessionExecutor>();
		}
		public override Task StartAsync()
		{
			subUpdated = jobNotificationService.Subscribe<JobUpdatedActionDto>(HandleUpdate);

			subRemoved = jobNotificationService.Subscribe<JobsRemovedActionDto>((dto) => {
				foreach (var job in dto.Jobs) {
					TryRemove(job.Id);
				}
			});
			subStopped = jobNotificationService.Subscribe<JobStoppedActionDto>((dto) => {
				TryRemove(dto.Job.Id);
			});
			subRunned = jobNotificationService.Subscribe<JobRunnedActionDto>((dto) => {
				systemSession.Execute(() => {
					var jobCurrent = Query.Single<ProductionJob>(dto.Job.Id);
					ActiveJobs.TryAdd(dto.Job.Id, jobCurrent.JobTechnology.CycleRuns.ToArray());
				});
			});

			return Task.CompletedTask;
		}

		private void TryRemove(long jobId)
		{
			JobTechnologyCycleRun[] cycleRuns;
			ActiveJobs.TryRemove(jobId, out cycleRuns);
		}

		private void HandleUpdate(JobUpdatedActionDto dto)
		{
			systemSession.Execute(() => {
				JobTechnologyCycleRun[] cycleRuns;
				if (ActiveJobs.TryGetValue(dto.Job.Id, out cycleRuns)) {
					var jobCurrent = Query.Single<ProductionJob>(dto.Job.Id);
					if (cycleRuns != null && jobCurrent.JobTechnology.CycleRuns != null && jobCurrent.JobTechnology.CycleRuns.Count > cycleRuns.Length) {
						if (dto.Job.EquipmentId != null)
							Handler(dto.Job.EquipmentId.Value, jobCurrent.Id, 0, 1, 0);
					}
					ActiveJobs[dto.Job.Id] = jobCurrent.JobTechnology.CycleRuns.ToArray();
				}
				else {
					var job = Query.SingleOrDefault<ProductionJob>(dto.Job.Id);
					if (job != null)
						ActiveJobs.TryAdd(dto.Job.Id, job.JobTechnology.CycleRuns.ToArray());
				}
			});
		}

		private void Handler(long equipmentId, long jobId, decimal accepted, decimal undefined, decimal rejected)
		{
			logger.LogInformation(equipmentId.ToString());

			OnSignal(new ProductionQuantityByNewCycle {
				EquipmentId = equipmentId,
				JobId = jobId,
				QuantityModel = new QuantityModel(accepted, undefined, rejected)
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
