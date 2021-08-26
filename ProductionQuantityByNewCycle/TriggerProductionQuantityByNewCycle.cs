using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Linq;
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
		private readonly ISystemSessionExecutor systemSession;
		private IJobService jobService;
		private IDisposable subUpdated;
		private IDisposable subRunned;
		private IDisposable subStopped;
		private IDisposable subRemoved;

		private ConcurrentDictionary<long, JobTechnologyCycleRun[]> ActiveJobs = new ConcurrentDictionary<long, JobTechnologyCycleRun[]>();

		public TriggerProductionQuantityByNewCycle(IServiceProvider serviceProvider)
		{
			jobService = serviceProvider.GetRequiredService<IJobService>();
			logger = serviceProvider.GetRequiredService<IHostLog<TriggerProductionQuantityByNewCycle>>();
			systemSession = serviceProvider.GetRequiredService<ISystemSessionExecutor>();
		}
		public override Task StartAsync()
		{
			subUpdated = jobService.Subscribe<JobUpdatedActionDto>(HandleUpdate);

			subRemoved = jobService.Subscribe<JobsRemovedActionDto>((dto) => {
				foreach (var jobId in dto.JobIds) {
					TryRemove(jobId);
				}
			});
			subStopped = jobService.Subscribe<JobStoppedActionDto>((dto) => {
				TryRemove(dto.Job.Id);
			});
			subRunned = jobService.Subscribe<JobRunnedActionDto>((dto) => {
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
							Handler(dto.Job.EquipmentId.Value, 0, 1, 0);
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

		private void Handler(long equipmentId, decimal accepted, decimal undefined, decimal rejected)
		{
			logger.Info(equipmentId);

			OnSignal(new ProductionQuantityByNewCycle {
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
