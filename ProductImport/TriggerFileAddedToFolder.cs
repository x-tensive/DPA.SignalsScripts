using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Xtensive.Project109.Host.DPA
{
	public class TriggerFileAddedToFolder : Signals2TriggerBase
	{
		private const string FILE_DIRECTORY = @"C:\Temp\test";
		private readonly IFileSystemWatcher watcher;

		public TriggerFileAddedToFolder(IServiceProvider serviceProvider)
		{
			watcher = serviceProvider.GetRequiredService<IFileSystemWatcher>();
		}

		public override Task StartAsync()
		{
			watcher.StartWatch();
			watcher.Path = FILE_DIRECTORY;
			watcher.EnableRaisingEvents = true;
			watcher.Created += Watcher_Created;
			return Task.CompletedTask;
		}

		private async void Watcher_Created(object sender, FileSystemEventArgs e)
		{
			await Task.Delay(1000);
			OnSignal(e.FullPath);
		}

		public override Task StopAsync()
		{
			watcher.StopWatch();
			return Task.CompletedTask;
		}
	}
}
