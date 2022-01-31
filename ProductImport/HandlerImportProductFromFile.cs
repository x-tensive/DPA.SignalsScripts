using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerImportProductFromFile : Signals2HandlerBase
	{
		private readonly IFileSystem fileSystem;
		private readonly ILogger<HandlerImportProductFromFile> logger;

		public HandlerImportProductFromFile(IServiceProvider serviceProvider)
		{
			fileSystem = serviceProvider.GetRequiredService<IFileSystem>();
			logger = serviceProvider.GetRequiredService<ILogger<HandlerImportProductFromFile>>();
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
