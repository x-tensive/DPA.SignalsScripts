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

		public const string TriggerEventName = "";
		public const string TriggerValueName = "";
		public static readonly object TriggerExpectedValue = 1;
		public const string SuccessResultUrl = "";
		public const string SuccessResultValue = "";

		public static readonly Dictionary<Guid, Dictionary<Guid, Func<EventInfo, string, DataTable>>> TableBuilders =
			new Dictionary<Guid, Dictionary<Guid, Func<EventInfo, string, DataTable>>> {
				{
					Guid.Parse("12457ce5-5034-47d7-a262-762379568b80"), new Dictionary<Guid, Func<EventInfo, string, DataTable>> {
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
					}
				}
			};

		private static double? AsNullableDouble(object sourceValue)
		{
			if (sourceValue == null) {
				return null;
			}
			return IndicatorSimpleModel.GetDoubleValue(sourceValue);
		}

		private static DataTable BuildRpaTable(EventInfo eventInfo, string workcenterName)
		{
			return eventInfo.GetFields()
				.Select((fieldName) => new
				{
					Value = AsNullableDouble(eventInfo.GetFieldValue(fieldName)),
					Parameter = fieldName,
					Machine = workcenterName,
					Timestamp = eventInfo.TimeStamp,
				})
				.AsDataTable("Rpa", schema => schema
					.WithColumn("Value", x => x.Value)
					.WithColumn("Timestamp", x => DateTime.Now)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}

		private static DataTable BuildLinshiftTable(EventInfo eventInfo, string workcenterName)
		{
			return eventInfo.GetFields()
				.Select((fieldName) => new
				{
					Value = AsNullableDouble(eventInfo.GetFieldValue(fieldName)),
					Parameter = fieldName,
					Machine = workcenterName,
					Timestamp = eventInfo.TimeStamp,
				})
				.AsDataTable("Linshift", schema => schema
					.WithColumn("Value", x => x.Value)
					.WithColumn("Timestamp", x => DateTime.Now)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}

		private static DataTable BuildGudTable(EventInfo eventInfo, string workcenterName)
		{
			return eventInfo.GetFields()
				.Select((fieldName, index) => new
				{
					Value = eventInfo.GetFieldValue(fieldName),
					Parameter = fieldName,
					Machine = workcenterName,
					Timestamp = eventInfo.TimeStamp,
				})
				.AsDataTable("GUD", schema => schema
					.WithColumn("Value", x => x.Value == null ? null : new string(x.Value.ToString().Take(MAX_GUD_VALUE_LENGTH).ToArray()))
					.WithColumn("Timestamp", x => DateTime.Now)
					.WithColumn("Parameter", x => x.Parameter)
					.WithColumn("Machine", x => x.Machine)
				);
		}
	}
}
