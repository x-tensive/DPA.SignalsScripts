using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerImportProductFromFile : Signals2HandlerBase
	{
		private readonly IFileSystem fileSystem;
		private readonly IHostLog<HandlerImportProductFromFile> logger;

		public HandlerImportProductFromFile(IServiceProvider serviceProvider)
		{
			fileSystem = serviceProvider.GetRequiredService<IFileSystem>();
			logger = serviceProvider.GetRequiredService<IHostLog<HandlerImportProductFromFile>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var fullFilePath = args.Obj.ToString();
			var fileContent = fileSystem.ReadAllText(fullFilePath);
			logger.Debug("Import product from file '" + fullFilePath + "'. Data: " + fileContent);

			var product = JsonConvert.DeserializeObject<ReferenceBookOfProductsModel>(fileContent);
			new ReferenceBookOfProducts().Update(product);

			fileSystem.DeleteFile(fullFilePath);
			return Task.CompletedTask;
		}
	}
}
