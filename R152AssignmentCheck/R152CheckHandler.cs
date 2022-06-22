using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Xtensive.Project109.Host.Security;
using Xtensive.Project109.Host.DPA;
using DPA.Messenger.Client.Models;
using DPA.Adapter.Tests.CommandHandlers.NcSubProgramEvent;
using Xtensive.DPA.FileManager;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public enum RegisterCheckResult
	{
		Assigned = 0,
		Referenced = 1,
		NotFound = 2
	}

	/// <summary>
	/// Will send an web notification about missing R152 incrementation to users from Technologists group using the {TEMPLATE_ID} template
	/// </summary>
	public class R152CheckHandler : Signals2HandlerBase
	{
		/// <summary>
		/// Message teamplate to be used for user notification
		/// </summary>
		private const string NotFoundTemplateName = "R152 NOT FOUND";//Обращение к регистру R152 не обнаружено в теле УП {ProgramName}, учет выпуска не будет произведен
		private const string NotIncreasedTemplateName = "R152 NOT INCREASED";//Регистр R152 присутствует в теле УП {ProgramName}, но его значение не изменяется, учет выпуска не будет произведен

		private readonly IJobService jobService;
		private readonly NotificationMessageTaskBuilder notificationMessageTaskBuilder;
		private readonly ILogger<R152CheckHandler> logger;

		public R152CheckHandler(IServiceProvider serviceProvider)
		{
			jobService = serviceProvider.GetRequiredService<IJobService>();
			notificationMessageTaskBuilder = serviceProvider.GetRequiredService<NotificationMessageTaskBuilder>();
			logger = serviceProvider.GetRequiredService<ILogger<R152CheckHandler>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			try {
				if (!(args.Obj is DownloadProgramRecordModel model)) {
					logger.LogError("Signal Handler received argument without DownloadProgramRecordModel payload for CP source code check for R152 register increment");
					return Task.CompletedTask;
				}
				if (model.ProgramData == null) {
					logger.LogError("Signal Handler received DownloadProgramRecordModel without CP source code check for R152 register increment");
					return Task.CompletedTask;
				}
				var resultCheck = CheckRegisterR152(model.ProgramData);
				switch (resultCheck) {
					case RegisterCheckResult.Assigned:
						logger.LogInformation($"Program {model.ProgramName} passed check for R152 register increment");
						break;
					case RegisterCheckResult.Referenced:
						logger.LogWarning($"Program {model.ProgramName} references R152 register in source code, but doesn't increment it explicitly");
						EmitNotification(NotIncreasedTemplateName, model.ProgramName);
						break;
					case RegisterCheckResult.NotFound:
						logger.LogError($"Program {model.ProgramName} doesn't reference R152 register in source code");
						EmitNotification(NotFoundTemplateName, model.ProgramName);
						break;
					default:
						throw new Exception($"Unknown outcome of CP source code check for R152 register increment: {resultCheck}");
				}
			}
			catch (Exception e) {
				logger.LogError("Failed to check CP source code for R152 register incrementation", e);
			}
			return Task.CompletedTask;
		}

		private static char[] nlChar = { '\n' };
		private static RegisterCheckResult CheckRegisterR152(DownloadProgramDataDto dto)
		{
			var cpSourceCode = DpaFileManager.GetHumanReadableText(dto.Data, dto.Format);
			var lines = cpSourceCode.Replace('\r', '\n').Split(nlChar, StringSplitOptions.RemoveEmptyEntries);
			var result = RegisterCheckResult.NotFound;
			foreach (var line in lines) {
				var semicolonIndex = line.IndexOf(';');
				var lineCode = (semicolonIndex >= 0 ? line.Substring(0, semicolonIndex) : line).Replace(" ", "").Replace("\t", "");
				var regRefInd = lineCode.IndexOf("R152", StringComparison.OrdinalIgnoreCase);
				if (regRefInd < 0)
					continue;
				if (result == RegisterCheckResult.NotFound)
					result = RegisterCheckResult.Referenced;
				var eqInd = regRefInd + 4;
				if (eqInd < lineCode.Length && lineCode[eqInd] == '=') {
					result = RegisterCheckResult.Assigned;
					break;
				}
			}
			return result;
		}

		private void EmitNotification(string msgTemplateName, string programName)
		{
			var msgTemplate = Query.All<MessageTemplate>().SingleOrDefault(tpl => tpl.Name.ToUpper() == msgTemplateName);
			if (msgTemplate == null) {
				logger.LogError($"Failed to find message template by Name={msgTemplateName}");
				return;
			}
			var techGrp = Query.All<Group>().SingleOrDefault(grp => grp.SID == Group.TechnologistGroup);
			if (techGrp == null) {
				logger.LogError($"Failed to find Technologists group by default SID={Group.TechnologistGroup}");
				return;
			}
			var techUsers = techGrp.Childs.OfType<DpaUser>().ToArray();
			notificationMessageTaskBuilder.BuildAndScheduleMessages(
				MessageTransportType.Web,
				msgTemplate,
				techUsers,
				() => new Dictionary<string, string> { { "ProgramName", programName } },
				"R152 increase check script, Signals 2.0"
			);
		}
	}
}