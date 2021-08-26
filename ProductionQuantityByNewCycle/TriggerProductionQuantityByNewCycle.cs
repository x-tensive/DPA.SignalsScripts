using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Xtensive.DPA.Host.Contracts;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Xtensive.Project109.Host.Security;

namespace Xtensive.Project109.Host.DPA
{
	public class TriggerProductionQuantityByNewCycle : Signals2TriggerBase
	{
		private readonly IHostLog<TriggerProductionQuantityByNewCycle> logger;
		private readonly ISystemSessionExecutor sessionExecutor;
		private IJobService jobService;
		private IDisposable subUpdated;
		private IDisposable subRunned;
		private IDisposable subStopped;
		private IDisposable subRemoved;

		private ConcurrentDictionary<long, ProductionJob> ActiveJobs = new ConcurrentDictionary<long, ProductionJob>();

		public TriggerProductionQuantityByNewCycle(IServiceProvider serviceProvider)
		{
			jobService = serviceProvider.GetRequiredService<IJobService>();
			logger = serviceProvider.GetRequiredService<IHostLog<TriggerProductionQuantityByNewCycle>>();
			sessionExecutor = serviceProvider.GetRequiredService<ISystemSessionExecutor>();
		}
		public override Task StartAsync()
		{
			subUpdated = jobService.Subscribe<JobUpdatedActionDto>(HandleUpdate);

			subRemoved = jobService.Subscribe<JobsRemovedActionDto>((dto)=> {
				foreach (var jobId in dto.JobIds) {
					TryRemove(jobId);
				}
			});
			subStopped = jobService.Subscribe<JobStoppedActionDto>((dto) => {
				TryRemove(dto.Job.Id);
			});
			subRunned = jobService.Subscribe<JobRunnedActionDto>((dto) => {
				sessionExecutor.Execute(() => {
					var jobCurrent = Query.Single<ProductionJob>(dto.Job.Id);
					ActiveJobs.TryAdd(dto.Job.Id, jobCurrent);
				});
			});

			return Task.CompletedTask;
		}

		private void TryRemove(long jobId)
		{
			ProductionJob job;
			ActiveJobs.TryRemove(jobId, out job);
		}

		private void HandleUpdate(JobUpdatedActionDto dto)
		{
			sessionExecutor.Execute(() => {
				ProductionJob job;
				if (ActiveJobs.TryGetValue(dto.Job.Id, out job)) {
					var jobCurrent = Query.Single<ProductionJob>(dto.Job.Id);
					if (job.JobTechnology.CycleRuns != null && jobCurrent.JobTechnology.CycleRuns != null && jobCurrent.JobTechnology.CycleRuns.Count > job.JobTechnology.CycleRuns.Count) {
						if (dto.Job.EquipmentId != null)
							Handler(dto.Job.EquipmentId.Value, 0, 1, 0);
					}
					ActiveJobs[dto.Job.Id] = jobCurrent;
				}
				else {
					job = Query.SingleOrDefault<ProductionJob>(dto.Job.Id);
					if (job != null)
						ActiveJobs.TryAdd(dto.Job.Id, job);
				}
			});
		}

		private void Handler(long equipmentId, decimal accepted, decimal undefined, decimal rejected)
		{
			logger.Info(equipmentId);

			OnSignal(new ZFProductionQuantity {
				EquipmentId = equipmentId,
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
