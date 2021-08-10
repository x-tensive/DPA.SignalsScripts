using DPA.Adapter.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Xtensive.Project109.Host.DPA.Adapter;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFTrigger : Signals2TriggerBase
	{
		private IEventSource generalEventSource;
		private IDisposable sub;

		public ZFTrigger(IServiceProvider serviceProvider)
		{
			generalEventSource = serviceProvider.GetRequiredService<IEventSource>();
		}
		public override Task StartAsync()
		{
			var indicatorId = Query.All<Indicator>().First(x => x.StateField == "Axis load, %" && x.Device.Name == "X").Id;

			sub = generalEventSource
					.EventsIncludingPreviousOf<IndicatorValueInfo>()
					.Where(x => x.IndicatorId == indicatorId)
					.Subscribe(HandleIndicatorEvent);

			return Task.CompletedTask;
		}

		private void HandleIndicatorEvent(IndicatorValueInfo obj)
		{
			OnSignal(obj.Value.Value);
		}

		public override Task StopAsync()
		{
			sub.Dispose();
			return Task.CompletedTask;
		}
	}
}
