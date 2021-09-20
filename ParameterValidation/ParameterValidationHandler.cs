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
			var validationResultAsString = JsonConvert.SerializeObject(validationResult, new JsonSerializerSettings { Formatting = Formatting.Indented, Converters = new[] { new StringEnumConverter() } });
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

		public void WriteToDriver(long equipmentId, EquipmentStateValidationResult validationResult)
		{
			var equipment = Query.Single<Equipment>(equipmentId);
			var driverId = equipment.DriverIdentifier;
			var serverName = equipment.Server.Name;
			var driverManager = managerResolver.GetChannelManager(serverName);

			var validationMsg = GetMessage(validationResult);
			var result = ((int)validationResult.Result) == 2 ? 2 : 3;
			driverManager.WriteVariableByUrl(driverId, ZF_Config.TARGET_RESULT_URL, new[] { result.ToString() });
			if (!string.IsNullOrEmpty(validationMsg)) {
				driverManager.WriteVariableByUrl(driverId, ZF_Config.TARGET_MESSAGE_URL, new[] { validationMsg.Substring(0, Math.Min(100, validationMsg.Length)) });
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