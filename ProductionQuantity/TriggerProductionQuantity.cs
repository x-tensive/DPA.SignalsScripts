using DPA.Adapter.Contracts;
using DPA.Adapter.Dto;
using DPA.Core.Repository.Commands;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.Host.Contracts;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFTriggerProductionQuantity : Signals2TriggerBase
	{
		private readonly ILogger<ZFTriggerProductionQuantity> logger;
		private IEventSource generalEventSource;
		private IDisposable sub;
		private IDisposable sub2;

		public ZFTriggerProductionQuantity(IServiceProvider serviceProvider)
		{
			generalEventSource = serviceProvider.GetRequiredService<IEventSource>();
			logger = serviceProvider.GetRequiredService<ILogger<ZFTriggerProductionQuantity>>();
		}
		public override Task StartAsync()
		{
			var equipmentId = Query.All<Equipment>().First(x => x.Name == "Рабочий центр1").Id;
			sub = generalEventSource
					.EventsOf<Add<ProductionConfirmRecordDto>>()
					.Where(x => x.Dto.Equipment.Id == equipmentId)
					.Subscribe(HandleIndicatorEvent);
			sub2 = generalEventSource
					.EventsOf<Update<ProductionConfirmRecordDto>>()
					.Where(x => x.Dto.Equipment.Id == equipmentId)
					.Subscribe(HandleIndicatorEvent2);

			return Task.CompletedTask;
		}

		private void HandleIndicatorEvent2(Update<ProductionConfirmRecordDto> obj)
		{
			Handler(obj.Dto.Equipment.Id, obj.Dto.Accepted, obj.Dto.Undefined, obj.Dto.Rejected);
		}

		private void HandleIndicatorEvent(Add<ProductionConfirmRecordDto> obj)
		{
			Handler(obj.Dto.Equipment.Id, obj.Dto.Accepted, obj.Dto.Undefined, obj.Dto.Rejected);
		}

		private void Handler(long equipmentId, decimal accepted, decimal undefined, decimal rejected)
		{
			logger.LogInformation(equipmentId.ToString());

			OnSignal(new ZFProductionQuantity {
				EquipmentId = equipmentId,
				QuantityModel = new QuantityModel(accepted, undefined, rejected)
			});
		}

		public override Task StopAsync()
		{
			sub.Dispose();
			sub2.Dispose();

			return Task.CompletedTask;
		}
	}
}
