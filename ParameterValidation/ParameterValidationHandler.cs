using DPA.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xtensive.DPA.Contracts;
using Xtensive.Orm;
using Xtensive.Project109.Host.Base;
using System.Data;
using System.Data.SqlClient;
using Xtensive.DPA.Host.Localization;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFHandler2 : Signals2HandlerBase
	{
		private readonly ILogger<ZFHandler2> logger;
		private readonly IInScopeExecutor<IControlProgramService> executor;
		private readonly IDpaChannelManagerResolver managerResolver;
		private readonly IIndicatorDataService indicatorService;

		private readonly DatabaseAdapter dbAdapter = new DatabaseAdapter(EventsToDatabaseSensitiveConfig.TARGET_DATABASE_CONNECTION);

		public ZFHandler2(IServiceProvider serviceProvider)
		{
			logger = serviceProvider.GetRequiredService<ILogger<ZFHandler2>>();
			executor = serviceProvider.GetRequiredService<IInScopeExecutor<IControlProgramService>>();
			managerResolver = serviceProvider.GetRequiredService<IDpaChannelManagerResolver>();
			indicatorService = serviceProvider.GetRequiredService<IIndicatorDataService>();
		}

		private void LogValidationResult(EquipmentStateValidationResult validationResult)
		{
			if (validationResult.Result == EquipmentValidationResult.Valid) {
				return;
			}

			var invalidResult = new EquipmentStateValidationResult {
				Equipment = validationResult.Equipment,
				Result = validationResult.Result,
				ResultDescription = validationResult.ResultDescription,
				TimeStamp = validationResult.TimeStamp,
				ControlProgramsValidations = validationResult
					.ControlProgramsValidations
					.Where(x => x.Result != EquipmentValidationResult.Valid)
					.Select(programValidation => new ControlProgramValidationResult {
						Result = programValidation.Result,
						ControlProgram = programValidation.ControlProgram,
						ResultDescription = programValidation.ResultDescription,
						Subprogram = programValidation.Subprogram,
						SetsValidation = programValidation
							.SetsValidation
							.Where(x => x.Result != EquipmentValidationResult.Valid)
							.Select(setValidation => new ParameterSetValidationResult {
								Result = setValidation.Result,
								ParameterSet = setValidation.ParameterSet,
								ParametersValidation = setValidation
									.ParametersValidation
									.Where(p => p.Result != EquipmentValidationResult.Valid)
									.ToArray(),
								ResultDescription = string.Empty
							})
							.ToArray()
					})
					.ToArray()
			};

			var validationResultAsString = JsonConvert.SerializeObject(invalidResult, new JsonSerializerSettings { Formatting = Formatting.Indented, Converters = new[] { new StringEnumConverter() } });
			Write(validationResultAsString);
		}

		private static void WriteToFolder(EquipmentStateValidationResult validationResult, Guid driverId, Xtensive.DPA.DpaClient.IDpaChannelManager driverManager)
		{
			var validationResultAsString = JsonConvert.SerializeObject(validationResult, new JsonSerializerSettings { Formatting = Formatting.Indented, Converters = new[] { new StringEnumConverter() } });
			var data = Encoding.ASCII.GetBytes(validationResultAsString);
			driverManager.UploadProgram(driverId, new UploadProgramRequestInfo {
				Folder = ZF_Config.TARGET_FOLDER,
				ProgramData = data,
				ClearNetworkFolder = false,
				IsNetworkFolder = false,
				ProgramInfo = new ProgramInfo {
					ProgramName = ZF_Config.VALIDATION_FILE_NAME + ".txt",
					Channel = 1
				}
			});
		}

		private static double? AsNullableDouble(object sourceValue)
		{
			if (sourceValue == null) {
				return null;
			}
			return IndicatorSimpleModel.GetDoubleValue(sourceValue);
		}

		private void WriteToDatabase(long equipmentId, EquipmentStateValidationResult validationResult)
		{
			var lmNumber = string.Empty;
			var indicator = indicatorService.GetFor(equipmentId, ZF_Config.LM_NUMBER_STATE, ZF_Config.LM_NUMBER_FIELD);
			if (indicator != null) {
				var value = indicatorService.GetLastValue(Query.Single<Indicator>(indicator.Id));
				if (value != null && value.Value != null) {
					lmNumber = value.Value.ToString();
				}
			}

			var flattenedResults = validationResult.ControlProgramsValidations
				.SelectMany(programValidation => programValidation
					.SetsValidation
					.SelectMany(paramtersSetValidation => paramtersSetValidation
						.ParametersValidation
						.Select(parameterValidation => new
						{
							Result = parameterValidation.Result,
							Timestamp = validationResult.TimeStamp,
							Machine = validationResult.Equipment == null ? string.Empty : validationResult.Equipment,
							Parameter = parameterValidation.Parameter == null || parameterValidation.Parameter.Name == null ? string.Empty : parameterValidation.Parameter.Name,
							Subprogram = programValidation.Subprogram == null ? string.Empty : programValidation.Subprogram,
							Program = programValidation.ControlProgram == null ? string.Empty : programValidation.ControlProgram,
							Value = parameterValidation.CalculatedValue == null ? 
								AsNullableDouble(parameterValidation.CurrentValue) : 
								AsNullableDouble(parameterValidation.CalculatedValue),
							LmNumber = lmNumber,
							MinValue = parameterValidation.Parameter.Min,
							MaxValue = parameterValidation.Parameter.Max,
							Message = parameterValidation.ResultDescription
						})
					)
				);
			var invalidResults = flattenedResults
				.Where(x => x.Result != EquipmentValidationResult.Valid)
				.AsDataTable("Kehren_OutOfBounds", cfg => cfg
					.WithColumn("Timestamp", x => x.Timestamp.DateTime)
					.WithColumn("Machine", x => x.Machine)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("SubProgram", x => x.Subprogram)
					.WithColumn("Program", x => x.Program)
					.WithColumn("Value", x => x.Value)
					.WithColumn("LM Number", x => x.LmNumber)
					.WithColumn("Min-value", x => x.MinValue)
					.WithColumn("Max-value", x => x.MaxValue)
					.WithColumn("Message", x => x.Message)
				);
			var allResults = flattenedResults
				.AsDataTable("ParamValKehren", cfg => cfg
					.WithColumn("Machine", x => x.Machine)
					.WithColumn("Timestamp", x => x.Timestamp.DateTime)
					.WithColumn("Programma", x => x.Program)
					.WithColumn("Subprogramma", x => x.Subprogram)
					.WithColumn("LM nummer", x => x.LmNumber)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Value", x => x.Value)
				);

			var aggregatedResults = validationResult
				.ControlProgramsValidations
				.Select(x => new { x.ControlProgram, x.Subprogram })
				.DefaultIfEmpty(new { ControlProgram = string.Empty, Subprogram = string.Empty })
				.Take(1)
				.AsDataTable("Kehren_response", cfg => cfg
					.WithColumn("Timestamp", x => validationResult.TimeStamp.DateTime)
					.WithColumn("Program-name", x => x.ControlProgram)
					.WithColumn("Subprogram-name", x => x.Subprogram)
					.WithColumn("Machine", x => validationResult.Equipment)
					.WithColumn("Message", x => GetMessage(validationResult))
					.WithColumn("Validation", x => GetResultAsInt(validationResult))
				);

			dbAdapter.WriteAsync(invalidResults).Wait();
			dbAdapter.WriteAsync(allResults).Wait();
			dbAdapter.WriteAsync(aggregatedResults).Wait();
		}

		private void ShortenValidationMessages(EquipmentStateValidationResult validationResult)
		{
			validationResult
				.ControlProgramsValidations
				.SelectMany(x => x.SetsValidation)
				.SelectMany(x => x.ParametersValidation)
				.ToList()
				.ForEach(x => x.ResultDescription = BuildMessage(x));
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			Task.Delay(ZF_Config.DELAY_BEFORE_VALIDATION).Wait();

			var triggeredBy = (Tuple<long, int, DateTimeOffset>)args.Obj;
			var equipmentId = triggeredBy.Item1;
			var channel = triggeredBy.Item2;
			var validationResult = Validate(equipmentId, channel);

			ShortenValidationMessages(validationResult);
			LogValidationResult(validationResult);
			WriteToDriver(equipmentId, validationResult);
			WriteToDatabase(equipmentId, validationResult);
			//WriteToFolder(validationResult, driverId, driverManager);
			
			return Task.CompletedTask;
		}

		private EquipmentStateValidationResult Validate(long equipmentId, int channel)
		{
			using (new DPALocalizationScope(CultureInfo.GetCultureInfo("nl-BE"))) {
				return executor.ExecuteRead(programService => programService.ValidateEquipmentState(equipmentId, channel));
			}
		}

		private string BuildMessage(ParameterValidationResult validationResult)
		{
			if (validationResult.Result == EquipmentValidationResult.Invalid) {
				return string.Format(
					"{0} = {1} [{2} - {3}]",
					string.IsNullOrEmpty(validationResult.Parameter.Description)
						? validationResult.Parameter.Name
						: validationResult.Parameter.Description,
					validationResult.CurrentValue,
					validationResult.Parameter.Min,
					validationResult.Parameter.Max
				);
			}
			return validationResult.ResultDescription;
		}

		private string GetMessage(EquipmentStateValidationResult validationResult)
		{
			if (validationResult.Result == EquipmentValidationResult.Valid) {
				return validationResult.ResultDescription;
			}
			var messages = validationResult
				.ControlProgramsValidations
				.SelectMany(controlProgram => controlProgram
					.SetsValidation
					.SelectMany(parametersSet => parametersSet.ParametersValidation.Select(x => new { x.ResultDescription, Order = 0, x.Result }))
					.Concat(new[] { new { controlProgram.ResultDescription, Order = 1, controlProgram.Result } })
				)
				.Concat(new[] { new { validationResult.ResultDescription, Order = 2, validationResult.Result } })
				.Where(x => x.Result != EquipmentValidationResult.Valid && !string.IsNullOrEmpty(x.ResultDescription))
				.OrderBy(x => x.Result == EquipmentValidationResult.Invalid ? 1 : 2)
				.ThenBy(x => x.Order)
				.ThenBy(x => x.ResultDescription.Length)
				.Select(x => x.ResultDescription)
				.ToArray();

			if (!messages.Any()) {
				return string.Empty;
			}

			var maxLength = 100;
			var currentResult = messages.First();
			var currentPrefix = string.Format("[1/{0}]", messages.Length);
			var currentCount = 1;

			foreach (var message in messages.Skip(1)) {
				var tempPrefix = string.Format("[{0}/{1}]", currentCount + 1, messages.Length);
				var tempResult = string.Format("{0}, {1}", currentResult, message);

				if (string.Format("{0} {1}", tempPrefix, tempResult).Length > maxLength) {
					break;
				}

				currentCount++;
				currentPrefix = tempPrefix;
				currentResult = tempResult;
			}

			var result = string.Format("{0} {1}", currentPrefix, currentResult);
			return result.Substring(0, Math.Min(result.Length, maxLength));
		}

		private int GetResultAsInt(EquipmentStateValidationResult validationResult)
		{
			switch (validationResult.Result) {
				case EquipmentValidationResult.Valid:
					return 2;
				case EquipmentValidationResult.Invalid:
					return 3;
				default:
					return 4;
			}
		}

		public void WriteToDriver(long equipmentId, EquipmentStateValidationResult validationResult)
		{
			var equipment = Query.Single<Equipment>(equipmentId);
			var driverId = equipment.DriverIdentifier;
			var serverName = equipment.Server.Name;
			var driverManager = managerResolver.GetChannelManager(serverName);

			var validationMsg = GetMessage(validationResult);
			var result = GetResultAsInt(validationResult);
			logger.LogInformation("Validation message: " + validationMsg);
			driverManager.WriteVariableByUrl(driverId, ZF_Config.TARGET_MESSAGE_URL, new[] { validationMsg == null ? string.Empty : validationMsg });
			driverManager.WriteVariableByUrl(driverId, ZF_Config.TARGET_RESULT_URL, new[] { result.ToString() });
		}

		private void Write(string data)
		{
			var newFileName = string.Format("{0}_{1}.txt", DateTime.Now.ToString("yyyyMMdd_hhmmss"), Guid.NewGuid().GetHashCode());
			var destination = Path.Combine(ZF_Config.LOG_DESTINATION, newFileName);
			if (!Directory.Exists(ZF_Config.LOG_DESTINATION)) {
				Directory.CreateDirectory(ZF_Config.LOG_DESTINATION);
			}
			System.IO.File.WriteAllText(destination, data);
		}
	}
}
