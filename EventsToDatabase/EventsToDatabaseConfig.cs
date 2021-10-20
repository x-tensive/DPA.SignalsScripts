using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Xtensive.DPA.EventManager;

namespace Xtensive.Project109.Host.DPA
{
	public static class EventsToDatabaseConfig
	{
		public const int MAX_GUD_VALUE_LENGTH = 200;

		public static readonly HashSet<Guid> DriversForMonitoring = new HashSet<Guid>(new[] {
			Guid.Parse("12457ce5-5034-47d7-a262-762379568b80"), //Monitoring -> Drivers -> Driver -> Driver identifier
			Guid.Parse("12457ce5-5034-47d7-a262-762379568b81")
		});

		public static readonly Dictionary<Guid, Func<SharedEventInfo, string, DataTable>> TableBuilders =
			new Dictionary<Guid, Func<SharedEventInfo, string, DataTable>> {
				{
					Guid.Parse("7b966f67-28c8-49f6-ac9f-bbefa34cfa95"), //Monitoring -> Drivers -> Driver -> Events -> Event -> Event identifier
					BuildRpaTable
				},
				{
					Guid.Parse("7b966f67-28c8-49f6-ac9f-bbefa34cfa91"), 
					BuildGudTable
				},
				{
					Guid.Parse("7b966f67-28c8-49f6-ac9f-bbefa34cfa92"), 
					BuildLinshiftTable
				}
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
					.WithColumn("Timestamp", x => x.Timestamp.DateTime)
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
					.WithColumn("Timestamp", x => x.Timestamp.DateTime)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}

		private static DataTable BuildGudTable(SharedEventInfo eventInfo, string workcenterName)
		{
			return eventInfo.EventInfo.Names
				.Select((fieldName, index) => new
				{
					Value = eventInfo.EventInfo.Values[index],
					Parameter = fieldName,
					Machine = workcenterName,
					Timestamp = eventInfo.TimeStamp,
				})
				.AsDataTable("GUD", schema => schema
					.WithColumn("Value", x => x.Value == null ? null : new string(x.Value.ToString().Take(MAX_GUD_VALUE_LENGTH).ToArray()))
					.WithColumn("Timestamp", x => x.Timestamp.DateTime)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}
	}
}
