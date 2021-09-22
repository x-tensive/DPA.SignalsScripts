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
using System.Collections.Generic;
using System.Data.SqlClient;

namespace Xtensive.Project109.Host.DPA
{
	public class ZFHandler2 : Signals2HandlerBase
	{
		private readonly IHostLog<ZFHandler2> logger;
		private readonly IInScopeExecutor<IControlProgramService> executor;
		private readonly IDpaChannelManagerResolver managerResolver;
		private readonly IIndicatorDataService indicatorService;

		public ZFHandler2(IServiceProvider serviceProvider)
		{
			logger = serviceProvider.GetRequiredService<IHostLog<ZFHandler2>>();
			executor = serviceProvider.GetRequiredService<IInScopeExecutor<IControlProgramService>>();
			managerResolver = serviceProvider.GetRequiredService<IDpaChannelManagerResolver>();
			indicatorService = serviceProvider.GetRequiredService<IIndicatorDataService>();
		}

		private void LogValidationResult(EquipmentStateValidationResult validationResult)
		{
			if (validationResult.Result == EquipmentValidationResult.Valid) {
				return;
			}

			var invalidResult = new EquipmentStateValidationResultTemp {
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
									.ToArray()
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

		private async Task WriteToDatabaseAsync(long equipmentId, EquipmentStateValidationResult validationResult)
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
							Machine = validationResult.Equipment,
							Parameter = parameterValidation.parameter.Name,
							Subprogram = programValidation.Subprogram,
							Program = programValidation.ControlProgram,
							Value = parameterValidation.CurrentValue,
							LmNumber = lmNumber,
							MinValue = parameterValidation.parameter.Min,
							MaxValue = parameterValidation.parameter.Max,
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

			await WriteAsync(invalidResults);
			await WriteAsync(allResults);
		}

		private async Task WriteAsync(DataTable data)
		{
			var connectionString = ZF_Config.TARGET_DATABASE_CONNECTION;

			using (var connection = new SqlConnection(connectionString)) {
				await connection.OpenAsync();
				var bulk = new SqlBulkCopy(connection) {
					DestinationTableName = data.TableName
				};
				foreach (DataColumn column in data.Columns) {
					bulk.ColumnMappings.Add(new SqlBulkCopyColumnMapping(column.ColumnName, column.ColumnName));
				}
				await bulk.WriteToServerAsync(data);
			}
		}

		public override async Task SignalHandleAsync(Signals2ScriptEventArgs args)
		{
			var triggeredBy = (Tuple<long, int>)args.Obj;
			var equipmentId = triggeredBy.Item1;
			var channel = triggeredBy.Item2;

			var validationResult = executor.ExecuteRead(programService => programService.ValidateEquipmentState(equipmentId, channel));

			LogValidationResult(validationResult);
			WriteToDriver(equipmentId, validationResult);
			await WriteToDatabaseAsync(equipmentId, validationResult);

			//WriteToFolder(validationResult, driverId, driverManager);
		}

		private string BuildMessage(ParameterValidationResult validationResult)
		{
			if (validationResult.Result == EquipmentValidationResult.Invalid) {
				return string.Format(
					"{0} = {1} [{2} - {3}]",
					string.IsNullOrEmpty(validationResult.parameter.Description)
						? validationResult.parameter.Name
						: validationResult.parameter.Description,
					validationResult.CurrentValue,
					validationResult.parameter.Min,
					validationResult.parameter.Max
				);
			}
			return validationResult.ResultDescription;
		}

		private string GetMessage(EquipmentStateValidationResult validationResult)
		{
			var messages = validationResult
				.ControlProgramsValidations
				.SelectMany(controlProgram => controlProgram
					.SetsValidation
					.SelectMany(parametersSet => parametersSet.ParametersValidation.Select(x => new { ResultDescription = BuildMessage(x), Order = x.parameter.Id }))
					.Concat(new[] { new { controlProgram.ResultDescription, Order = -1L } })
				)
				.Concat(new[] { new { validationResult.ResultDescription, Order = -2L } })
				.Where(x => !string.IsNullOrEmpty(x.ResultDescription))
				.OrderBy(x => x.Order)
				.Select(x => x.ResultDescription)
				.ToArray();

			if (!messages.Any()) {
				return string.Empty;
			}

			var maxLength = 100;
			var currentResult = messages.First();
			var currentPrefix = string.Format("[1 of {0}]", messages.Length);
			var currentCount = 1;

			foreach (var message in messages.Skip(1)) {
				var tempPrefix = string.Format("[{0} of {1}]", currentCount + 1, messages.Length);
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

		public void WriteToDriver(long equipmentId, EquipmentStateValidationResult validationResult)
		{
			var equipment = Query.Single<Equipment>(equipmentId);
			var driverId = equipment.DriverIdentifier;
			var serverName = equipment.Server.Name;
			var driverManager = managerResolver.GetChannelManager(serverName);

			var validationMsg = GetMessage(validationResult);
			var result = ((int)validationResult.Result) == 2 ? 2 : 3;
			logger.Info("Validation message: " + validationMsg);
			driverManager.WriteVariableByUrl(driverId, ZF_Config.TARGET_RESULT_URL, new[] { result.ToString() });
			if (!string.IsNullOrEmpty(validationMsg)) {
				driverManager.WriteVariableByUrl(driverId, ZF_Config.TARGET_MESSAGE_URL, new[] { validationMsg });
			}
			else {
				driverManager.WriteVariableByUrl(driverId, ZF_Config.TARGET_MESSAGE_URL, new[] { "Validation OK" });
			}
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

	public class EquipmentStateValidationResultTemp
	{
		//TODO: This class was required because TimeStamp of EquipmentStateValidationResult was internal.
		//It can be removed after Timestamp became public
		public DateTimeOffset TimeStamp { get; set; }
		public string Equipment { get; set; }
		public EquipmentValidationResult Result { get; set; }
		public string ResultDescription { get; set; }
		public ControlProgramValidationResult[] ControlProgramsValidations { get; set; }
	}

	public static class Ex
	{
		public static DataTable AsDataTable<T>(this IEnumerable<T> source, string tableName, Func<Builder<T>, Builder<T>> cfg)
		{
			var result = cfg(new Builder<T>()).WithData(source.ToArray());
			result.TableName = tableName;
			return result;
		}
	}

	public class Builder<TSource>
	{
		private readonly DataTable table;
		private Dictionary<string, Func<TSource, object>> Selectors = new Dictionary<string, Func<TSource, object>>();

		public Builder()
		{
			this.table = new DataTable();
		}

		public Builder<TSource> WithColumn<TValue>(string name, Func<TSource, TValue> selector)
		{
			Selectors[name] = x => selector(x);
			table.Columns.Add(name, typeof(TValue));
			return this;
		}

		public DataTable WithData(TSource[] values)
		{
			foreach (var item in values) {
				var newRow = table.NewRow();
				foreach (var selector in Selectors) {
					newRow[selector.Key] = selector.Value(item);
				}
				table.Rows.Add(newRow);
			}
			return table;
		}
	}
}
