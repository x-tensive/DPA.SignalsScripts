using DPA.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xtensive.DPA.Contracts;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class HandlerWriteOperationInRregister : Signals2HandlerBase
	{
		private const string TARGET_RESULT_URL = "VALIDATIE";
		private const string TARGET_MESSAGE_URL = "VALIDATIE";
		private const string TARGET_FOLDER = "some folder of machine";
		private const string VALIDATION_FILE_NAME = "some file name";

		private readonly ILogger<HandlerWriteOperationInRregister> logger;
		private readonly IInScopeExecutor<IControlProgramService> executor;
		private readonly IDpaChannelManagerResolver managerResolver;

		public HandlerWriteOperationInRregister(IServiceProvider serviceProvider)
		{
			logger = serviceProvider.GetRequiredService<ILogger<HandlerWriteOperationInRregister>>();
			executor = serviceProvider.GetRequiredService<IInScopeExecutor<IControlProgramService>>();
			managerResolver = serviceProvider.GetRequiredService<IDpaChannelManagerResolver>();
		}

		private void LogValidationResult(EquipmentStateValidationResult validationResult)
		{
			var validationResultAsString = JsonConvert.SerializeObject(validationResult, new JsonSerializerSettings { Formatting = Formatting.Indented, Converters = new[] { new StringEnumConverter() } });
			logger.Info(validationResultAsString);
		}

		private static void WriteToFolder(EquipmentStateValidationResult validationResult, Guid driverId, Xtensive.DPA.DpaClient.IDpaChannelManager driverManager)
		{
			var validationResultAsString = JsonConvert.SerializeObject(validationResult, new JsonSerializerSettings { Formatting = Formatting.Indented, Converters = new[] { new StringEnumConverter() } });
			var data = Encoding.ASCII.GetBytes(validationResultAsString);
			driverManager.UploadProgram(driverId, new UploadProgramRequestInfo {
				Folder = TARGET_FOLDER,
				ProgramData = data,
				ClearNetworkFolder = false,
				IsNetworkFolder = false,
				ProgramInfo = new ProgramInfo {
					ProgramName = VALIDATION_FILE_NAME + ".txt",
					Channel = 1
				}
			});
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var triggeredBy = (Tuple<long, int>)args.Obj;
			var equipmentId = triggeredBy.Item1;
			var channel = triggeredBy.Item2;

			var validationResult = executor.ExecuteRead(programService => programService.ValidateEquipmentState(equipmentId, channel));
			LogValidationResult(validationResult);
			//HandleValidationResult(equipmentId, validationResult);
			return Task.CompletedTask;
		}

		private string GetMessage(EquipmentStateValidationResult validationResult)
		{
			return validationResult
				.ControlProgramsValidations
				.SelectMany(controlProgram => controlProgram
					.SetsValidation
					.SelectMany(parametersSet => parametersSet.ParametersValidation.Select(parameter => parameter.ResultDescription))
					.Concat(new[] { controlProgram.ResultDescription })
				)
				.Concat(new[] { validationResult.ResultDescription })
				.Where(x => !string.IsNullOrEmpty(x))
				.FirstOrDefault();
		}

		private void HandleValidationResult(long equipmentId, EquipmentStateValidationResult validationResult)
		{
			var equipment = Query.Single<Equipment>(equipmentId);
			var driverId = equipment.DriverIdentifier;
			var serverName = equipment.Server.Name;
			var driverManager = managerResolver.GetChannelManager(serverName);

			var validationMsg = GetMessage(validationResult);
			driverManager.WriteVariableByUrl(driverId, TARGET_RESULT_URL, new[] { ((int)validationResult.Result).ToString() });
			if (!string.IsNullOrEmpty(validationMsg)) {
				driverManager.WriteVariableByUrl(driverId, TARGET_MESSAGE_URL, new[] { validationMsg.Substring(0, Math.Min(100, validationMsg.Length)) });
			}
			//WriteToFolder(validationResult, driverId, driverManager);
		}
	}
}
