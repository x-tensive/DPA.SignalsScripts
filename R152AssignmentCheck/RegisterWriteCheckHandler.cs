using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Linq.Expressions;
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
	/// Will send an web notification about missing registers write operations to users from Technologists group using the {TEMPLATE_ID} template
	/// </summary>
	public class RegisterWriteCheckHandler : Signals2HandlerBase
	{
		/// <summary>
		// "REGISTER NOT REFERENCED" message template must be added in Messenger microservice, with following message body:
		// "{RegisterName} reference was not found in CP {ProgramName} body on {EquipmentName} equipment"
		/// </summary>
		private const string NotFoundTemplateName = "REGISTER NOT REFERENCED";

		/// <summary>
		// "REGISTER NOT UPDATED" message template must be added in Messenger microservice, with following message body:
		// "{RegisterName} reference exists in CP {ProgramName} body, but its value is never modified on {EquipmentName} equipment"
		/// </summary>
		private const string NotUpdatedTemplateName = "REGISTER NOT UPDATED";

		/// <summary>
		// Program download event will trigger write operation(s) to the register(s) with name(s), identified from equipment group(s), which the equipment
		// sourcing the event belongs to (if any).
		// The register name to check must be appended to the end of "AffectedEquipmentGroupNameTemplate" value in the equipment group name.
		// For example, Control Programs from all Equipment belonging to "CHECK WRITE OPERATION R152" will be checked for write operations to "R152" register.
		// Another example, if given Equipment belongs to groups with names "CHECK WRITE OPERATION R140" and "CHECK WRITE OPERATION R152", then Control Programs
		// from this Equipment will be checked for write operations to register R140 and to register R152, and notification will be sent if any of them is missing
		/// </summary>
		private const string AffectedEquipmentGroupNameTemplate = "CHECK WRITE OPERATION ";

		private readonly IJobService jobService;
		private readonly NotificationMessageTaskBuilder notificationMessageTaskBuilder;
		private readonly ILogger<RegisterWriteCheckHandler> logger;

		public RegisterWriteCheckHandler(IServiceProvider serviceProvider)
		{
			jobService = serviceProvider.GetRequiredService<IJobService>();
			notificationMessageTaskBuilder = serviceProvider.GetRequiredService<NotificationMessageTaskBuilder>();
			logger = serviceProvider.GetRequiredService<ILogger<RegisterWriteCheckHandler>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			try {
				if (!(args.Obj is DownloadProgramRecordModel model)) {
					logger.LogError("Signal Handler received argument without DownloadProgramRecordModel payload for CP source code check for register write operation");
					return Task.CompletedTask;
				}
				var equipmentFromGroupToCheck = Query.All<Equipment>()
					.Where(eq => eq.DriverIdentifier == model.DriverIdentifier)
					.SelectMany(equipment => equipment.EquipmentGroups
					.Where(wcGroup => wcGroup.Name.ToUpper().StartsWith(AffectedEquipmentGroupNameTemplate))
					.Select(matchedGroup => new { EquipmentName = equipment.Name, EquipmentGroupName = matchedGroup.Name }))
					.ToArray();
				foreach (var toCheck in equipmentFromGroupToCheck)
				{
					if (toCheck.EquipmentGroupName.Length <= AffectedEquipmentGroupNameTemplate.Length)
					{
						logger.LogError($"Signal Handler received event from equipment from group with malformed name: {toCheck.EquipmentGroupName}");
						return Task.CompletedTask;
					}
					var registerName = toCheck.EquipmentGroupName.Substring(AffectedEquipmentGroupNameTemplate.Length).Trim();
					if (string.IsNullOrEmpty(registerName))
					{
						logger.LogError($"Signal Handler received event from equipment from group with malformed name: {toCheck.EquipmentGroupName}");
						return Task.CompletedTask;
					}
					if (model.ProgramData == null) {
						logger.LogError($"Signal Handler received DownloadProgramRecordModel without CP source code check for {registerName} register write operation");
						return Task.CompletedTask;
					}
					var resultCheck = CheckForRegisterWriteOperation(registerName, model.ProgramData);
					switch (resultCheck) {
						case RegisterCheckResult.Assigned:
							logger.LogInformation($"Program {model.ProgramName} passed check for {registerName} register increment on {toCheck.EquipmentName} equipment");
							break;
						case RegisterCheckResult.Referenced:
							logger.LogWarning($"Program {model.ProgramName} references {registerName} register in source code, but doesn't increment it explicitly on {toCheck.EquipmentName} equipment");
							EmitNotification(NotUpdatedTemplateName, model.ProgramName, registerName, toCheck.EquipmentName);
							break;
						case RegisterCheckResult.NotFound:
							logger.LogError($"Program {model.ProgramName} doesn't reference {registerName} register in source code on {toCheck.EquipmentName} equipment");
							EmitNotification(NotFoundTemplateName, model.ProgramName, registerName, toCheck.EquipmentName);
							break;
						default:
							throw new Exception($"Unknown outcome of CP source code check for {registerName} register increment: {resultCheck}");
					}
				}
			}
			catch (Exception e) {
				logger.LogError("Failed to check CP source code for {registerName} register incrementation", e);
			}
			return Task.CompletedTask;
		}

		private static char[] nlChar = { '\n' };
		private static RegisterCheckResult CheckForRegisterWriteOperation(string registerName, DownloadProgramDataDto dto)
		{
			var cpSourceCode = DpaFileManager.GetHumanReadableText(dto.Data, dto.Format);
			var lines = cpSourceCode.Replace('\r', '\n').Split(nlChar, StringSplitOptions.RemoveEmptyEntries);
			var result = RegisterCheckResult.NotFound;
			foreach (var line in lines) {
				var semicolonIndex = line.IndexOf(';');
				var lineCode = (semicolonIndex >= 0 ? line.Substring(0, semicolonIndex) : line).Replace(" ", "").Replace("\t", "");
				var regRefInd = lineCode.IndexOf(registerName, StringComparison.OrdinalIgnoreCase);
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

		private void EmitNotification(string msgTemplateName, string programName, string registerName, string equipmentName)
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
				() => new Dictionary<string, string> { { "ProgramName", programName }, { "RegisterName", registerName }, { "EquipmentName", equipmentName } },
				"Register write operation check script, Signals 2.0"
			);
		}
	}
}