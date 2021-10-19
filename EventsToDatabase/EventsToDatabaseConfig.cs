using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Xtensive.DPA.EventManager;

namespace Xtensive.Project109.Host.DPA.Tests.Signals2.Scripts.DPA.SignalsScripts.EventsToDatabase
{
	public static class EventsToDatabaseConfig
	{
		public const int MAX_GUD_VALUE_LENGTH = 200;

		public static readonly HashSet<Guid> DriversForMonitoring = new HashSet<Guid>(new[] {
			Guid.NewGuid()
		});

		public static readonly Dictionary<string, Func<SharedEventInfo, string, DataTable>> TableBuilders =
			new Dictionary<string, Func<SharedEventInfo, string, DataTable>> {
				["Rpa"] = BuildRpaTable,
				["GUD"] = BuildGudTable,
				["Linshift"] = BuildLinshiftTable
			};


		private static double? AsNullableDouble(object sourceValue)
		{
			if (sourceValue == null) {
				return null;
			}
			return IndicatorSimpleModel.GetDoubleValue(sourceValue);
		}

		private static DataTable BuildRpaTable(SharedEventInfo eventInfo, string workcenterName)
		{
			return eventInfo.EventInfo.Names
				.Select((fieldName, index) => new
				{
					Value = AsNullableDouble(eventInfo.EventInfo.Values[index]),
					Parameter = fieldName,
					Machine = workcenterName,
					Timestamp = eventInfo.TimeStamp,
				})
				.AsDataTable("Rpa", schema => schema
					.WithColumn("Value", x => x.Value)
					.WithColumn("Timestamp", x => x.Timestamp)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}

		private static DataTable BuildLinshiftTable(SharedEventInfo eventInfo, string workcenterName)
		{
			return eventInfo.EventInfo.Names
				.Select((fieldName, index) => new
				{
					Value = AsNullableDouble(eventInfo.EventInfo.Values[index]),
					Parameter = fieldName,
					Machine = workcenterName,
					Timestamp = eventInfo.TimeStamp,
				})
				.AsDataTable("Linshift", schema => schema
					.WithColumn("Value", x => x.Value)
					.WithColumn("Timestamp", x => x.Timestamp)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}

		private static DataTable BuildGudTable(SharedEventInfo eventInfo, string workcenterName)
		{
			return eventInfo.EventInfo.Names
				.Select((fieldName, index) => new
				{
					Value = eventInfo.EventInfo.Values[index]?.ToString(),
					Parameter = fieldName,
					Machine = workcenterName,
					Timestamp = eventInfo.TimeStamp,
				})
				.AsDataTable("GUD", schema => schema
					.WithColumn("Value", x => x.Value == null ? null : new string(x.Value.Take(MAX_GUD_VALUE_LENGTH).ToArray()))
					.WithColumn("Timestamp", x => x.Timestamp)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}
	}
}
