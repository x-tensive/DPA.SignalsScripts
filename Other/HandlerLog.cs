using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFHandler : Signals2HandlerBase
	{
		private readonly IServiceProvider serviceProvider;

		public ZFHandler(IServiceProvider serviceProvider)
		{
			this.serviceProvider = serviceProvider;
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var sp = serviceProvider.GetRequiredService<ISignals2FileScriptProvider>();

			return Task.Run(() => {
				var start = DateTime.UtcNow;
				var path = Path.Combine("C://", "ZF_handler.txt");
				if (!System.IO.File.Exists(path)) {
					var t = System.IO.File.Create(path);
					t.Dispose();
				}

				using (var w = System.IO.File.AppendText(path)) {
					var str = "handler start " + start + " - " + args.Obj + Environment.NewLine;
					w.WriteLine(str);
				}
			});
		}
	}
}
