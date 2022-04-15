using DPA.Core;
using DPA.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xtensive.DPA.Host.Localization;

namespace Xtensive.Project109.Host.DPA
{
	public class ParameterValidationJsonHandler : Signals2HandlerBase
	{		
		private readonly IFileSystem fileSystem;
		private readonly ILogger<ParameterValidationJsonHandler> logger;
		private readonly IInScopeExecutor<IControlProgramService> executor;

		private readonly DatabaseAdapter dbAdapter = new DatabaseAdapter(EventsToDatabaseSensitiveConfig.TARGET_DATABASE_CONNECTION);
		
		public ParameterValidationJsonHandler(IServiceProvider serviceProvider)
		{
			fileSystem = serviceProvider.GetRequiredService<IFileSystem>();
			logger = serviceProvider.GetRequiredService<ILogger<ParameterValidationJsonHandler>>();
			executor = serviceProvider.GetRequiredService<IInScopeExecutor<IControlProgramService>>();
		}

		public override Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var fullFilePath = args.Obj.ToString();
			var fileContent = fileSystem.ReadAllText(fullFilePath);
			var preamble = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetPreamble());
			if (fileContent.StartsWith(preamble))
			{
				fileContent = fileContent.Remove(0, preamble.Length);
			}
			logger.LogDebug("Validate parameters from file '" + fullFilePath + "'. Data: " + fileContent);

			var jsonModel = JsonConvert.DeserializeObject<JsonWorkCenterModel>(fileContent);
			jsonModel.Name = new DirectoryInfo(Path.GetDirectoryName(fullFilePath)).Name;
			jsonModel.Channel = ParameterValidationJsonConfig.VALIDATION_CHANNEL;
			var validationModel = JsonModelToValidationModelMapper.Map(jsonModel);
			var validationResult = Validate(validationModel);
			
			ShortenValidationMessages(validationResult);
			LogValidationResult(validationResult);
			WriteToDatabase(validationResult);

			fileSystem.DeleteFile(fullFilePath);
			return Task.CompletedTask;
		}
			
		private EquipmentStateValidationResult Validate(ValidationWorkCenterModel validationEquipmentModel)
		{
			using (new DPALocalizationScope(CultureInfo.GetCultureInfo("en-US")))
			{
				return executor.ExecuteRead(programService => programService.ValidateEquipmentState(validationEquipmentModel));
			}
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
								ResultDescription = setValidation.ResultDescription
							})
							.ToArray()
					})
					.ToArray()
			};

			var validationResultAsString = JsonConvert.SerializeObject(invalidResult, new JsonSerializerSettings { Formatting = Formatting.Indented, Converters = new[] { new StringEnumConverter() } });
			WriteLog(validationResultAsString);
		}
		
		private void WriteToDatabase(EquipmentStateValidationResult validationResult)
		{
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
							MinValue = parameterValidation.Parameter.Min,
							MaxValue = parameterValidation.Parameter.Max,
							Message = parameterValidation.ResultDescription
						})
					)
				);
			var invalidResults = flattenedResults
				.Where(x => x.Result != EquipmentValidationResult.Valid)
				.AsDataTable("OutOfBounds", cfg => cfg
					.WithColumn("Timestamp", x => x.Timestamp.DateTime)
					.WithColumn("Machine", x => x.Machine)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("SubProgram", x => x.Subprogram)
					.WithColumn("Program", x => x.Program)
					.WithColumn("Value", x => x.Value)
					.WithColumn("Min-value", x => x.MinValue)
					.WithColumn("Max-value", x => x.MaxValue)
					.WithColumn("Message", x => x.Message)
				);
			var allResults = flattenedResults
				.AsDataTable("ParamValue", cfg => cfg
					.WithColumn("Machine", x => x.Machine)
					.WithColumn("Timestamp", x => x.Timestamp.DateTime)
					.WithColumn("Programma", x => x.Program)
					.WithColumn("Subprogramma", x => x.Subprogram)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Value", x => x.Value)
				);
			var aggregatedResults = validationResult
				.ControlProgramsValidations
				.Select(x => new { x.ControlProgram, x.Subprogram })
				.DefaultIfEmpty(new { ControlProgram = string.Empty, Subprogram = string.Empty })
				.Take(1)
				.AsDataTable("Response", cfg => cfg
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
		
		private static double? AsNullableDouble(object sourceValue)
		{
			if (sourceValue == null) {
				return null;
			}
			return GetDoubleValue(sourceValue);
		}
		
		private static double GetDoubleValue(object value)
		{
			double.TryParse(value != null ? value.ToString() : "", out var result);
			return result;
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
		
		private void WriteLog(string data)
		{
			var newFileName = string.Format("{0}_{1}.txt", DateTime.Now.ToString("yyyyMMdd_hhmmss"), Guid.NewGuid().GetHashCode());
			var destination = Path.Combine(ParameterValidationJsonConfig.OUTPUT_DIRECTORY, newFileName);
			if (!Directory.Exists(ParameterValidationJsonConfig.OUTPUT_DIRECTORY)) {
				Directory.CreateDirectory(ParameterValidationJsonConfig.OUTPUT_DIRECTORY);
			}
			System.IO.File.WriteAllText(destination, data);
		}
	}
	
	#region Models
	public class JsonWorkCenterModel
	{
		public string Name { get; set; }
		public int Channel { get; set; }
		public JsonDialogModel[] Dialogs { get; set; }
	}
	
	public class JsonDialogModel
	{
		public string Name { get; set; }
		public JsonControlModel[] Controls { get; set; }
	}
	
	public class JsonControlModel
	{
		public string Name { get; set; }
		public string Value { get; set; }
	}
	
	public class JsonModelToValidationModelMapper
	{
		public static ValidationWorkCenterModel Map(JsonWorkCenterModel jsonEquipmentModel)
		{
			var validationEquipmentModel = new ValidationWorkCenterModel()
			{
				Name = jsonEquipmentModel.Name,
				Channel = jsonEquipmentModel.Channel
			};
			
			var countOfParameterSets = jsonEquipmentModel.Dialogs != null ? jsonEquipmentModel.Dialogs.Length : 0;
			var validationParameterSetModels = new List<ValidationParameterSetModel>(countOfParameterSets);
			foreach (var jsonDialogModel in  jsonEquipmentModel.Dialogs)
			{
				var validationParameterSetModel = new ValidationParameterSetModel() { Name = jsonDialogModel.Name };

				var countOfParameters = jsonDialogModel.Controls != null ? jsonDialogModel.Controls.Length : 0;
				var  validationParameterModels = new List<ValidationParameterModel>(countOfParameters);
				foreach (var jsonControlModel in  jsonDialogModel.Controls)
				{
					var validationParameterModel = new ValidationParameterModel() { Name = jsonControlModel.Name, Value = jsonControlModel.Value };
					validationParameterModels.Add(validationParameterModel);
				}
				validationParameterSetModel.Parameters = validationParameterModels.ToArray();
				
				validationParameterSetModels.Add(validationParameterSetModel);
			}
			validationEquipmentModel.ParameterSets = validationParameterSetModels.ToArray();

			return validationEquipmentModel;
		}
	}
	#endregion
}
