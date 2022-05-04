using DPA.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

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
			logger.LogDebug("Import product from file '" + fullFilePath + "'. Data: " + fileContent);

			var product = JsonConvert.DeserializeObject<ProductsModel>(fileContent);
			new ReferenceBookOfProducts().Update(product);

			fileSystem.DeleteFile(fullFilePath);
			return Task.CompletedTask;
		}
	}
}
