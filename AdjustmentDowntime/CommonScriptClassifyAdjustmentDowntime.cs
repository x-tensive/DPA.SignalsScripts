using System;
using System.Collections.Generic;
using System.Linq;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;

namespace Xtensive.Project109.Host.DPA
{
	public class IdleSpeedAdjustmentDowntime : AdjustmentDowntimeReason
	{
		// Название причины из справочника для классификации периода наладки
		public const string reasonName = "Холостой ход при работе без управляющей программы";

		public override Dictionary<EventInfoType, Predicate<double>> EventPredicateDict => new Dictionary<EventInfoType, Predicate<double>>() {
			// скорость шпинделя != 0
			{ EventInfoType.SpindleSpeed,  (val) => val != 0 },
			// нагрузка на шпиндель < 2%
			{ EventInfoType.SpindleLoad, (val) => val < 2 }
		};

		public IdleSpeedAdjustmentDowntime(IIndicatorContext indicatorContext, long equipmentId, DateTimeOffset startDate, DateTimeOffset endDate)
			: base(indicatorContext, reasonName, equipmentId, startDate, endDate)
		{

		}
	}


	public class NoNcProgramAdjustmentDowntime : AdjustmentDowntimeReason
	{
		// Название причины из справочника для классификации периода наладки
		public const string reasonName = "Работа без управляющей программы";

		public override Dictionary<EventInfoType, Predicate<double>> EventPredicateDict => new Dictionary<EventInfoType, Predicate<double>>() {
			// скорость шпинделя != 0
			{ EventInfoType.SpindleSpeed,  (val) => val != 0 },
			// нагрузка на шпиндель >= 2%
			{ EventInfoType.SpindleLoad, (val) => val >= 2 },
			// подача != 0
			{ EventInfoType.FeedRate, (val) => val != 0 }
		};

		public NoNcProgramAdjustmentDowntime(IIndicatorContext indicatorContext, long equipmentId, DateTimeOffset startDate, DateTimeOffset endDate)
			: base(indicatorContext, reasonName, equipmentId, startDate, endDate)
		{
		}
	}

	public abstract class AdjustmentDowntimeReason
	{
		public ReferenceBookReasonsOfDowntime Reason { get; set; }
		public long EquipmentId { get; }
		public abstract Dictionary<EventInfoType, Predicate<double>> EventPredicateDict { get; }
		public DateTimeOffset StartDate { get; set; }
		public DateTimeOffset EndDate { get; set; }

		public DateTimeSegments Segments { get; set; }

		public AdjustmentDowntimeReason(IIndicatorContext indicatorContext, 
			string reasonName, long equipmentId, DateTimeOffset startDate, DateTimeOffset endDate)
		{
			Reason = Query.All<ReferenceBookReasonsOfDowntime>()
					.FirstOrDefault(r => r.Name.Equals(reasonName));
			if (Reason == null) {
				throw new Exception(string.Format($"No reference book reason of downtime with name {0} exists",
					reasonName));
			}

			StartDate = startDate;
			EndDate = endDate;
			EquipmentId = equipmentId;

			Segments = GetDateTimeSegmentsToClassify(indicatorContext);
		}



		private DateTimeSegments BuildFilteredDateTimeSegments(List<IndicatorValue> values, Predicate<double> filter)
		{
			var result = new DateTimeSegments();

			DateTimeOffset? startSegment = null;
			DateTimeOffset? endSegment = null;
			foreach (var item in values) {
				var currentIndicatorValue = IndicatorSimpleModel.GetDoubleValue(item.Value);
				var isSatisfiedValue = filter(currentIndicatorValue);

				if (!isSatisfiedValue && startSegment.HasValue) {
					endSegment = item.TimeStamp.ToLocalTime();
					result.Add(new DateTimeSegment(startSegment.Value, endSegment.Value));
					startSegment = null;
					endSegment = null;
				}
				else if (isSatisfiedValue && !startSegment.HasValue) {
					startSegment = item.TimeStamp.ToLocalTime();
				}
			}

			if (startSegment.HasValue && !endSegment.HasValue) {
				endSegment = values.Last().TimeStamp.ToLocalTime();
				result.Add(new DateTimeSegment(startSegment.Value, endSegment.Value));
			}

			return result;
		}

		private DateTimeSegments GetDateTimeSegmentsToClassify(IIndicatorContext indicatorContext)
		{
			DateTimeSegments result = null;

			foreach (var eventPredicate in EventPredicateDict) {
				var indicator = Query.All<Indicator>().First(indic => indic.Device.Owner.Id == EquipmentId && indic.StateName == eventPredicate.Key.GetStateName());
				var values = indicatorContext.GetIndicatorData(indicator.Id, StartDate, EndDate)?.ToList();
				if (values == null || values.Count() == 0) {
					return null;
				}

				var dtSegments = BuildFilteredDateTimeSegments(values, eventPredicate.Value);
				if (dtSegments == null) {
					return null;
				}

				result = result == null ? dtSegments : result.Intersection(dtSegments);
			}

			return result;
		}
	}

	public static class AdjustmentDowntimeCreator
	{
		public static List<string> Reasons =>
			new List<string> { IdleSpeedAdjustmentDowntime.reasonName, NoNcProgramAdjustmentDowntime.reasonName };

		static AdjustmentDowntimeCreator()
		{
		}

		public static AdjustmentDowntimeReason Create(IIndicatorContext indicatorContext, string reasonName, long equipmentId, DateTimeOffset startDate, DateTimeOffset endDate)
		{
			switch (reasonName) {
				case IdleSpeedAdjustmentDowntime.reasonName:
					return new IdleSpeedAdjustmentDowntime(indicatorContext, equipmentId, startDate, endDate);
				case NoNcProgramAdjustmentDowntime.reasonName:
					return new NoNcProgramAdjustmentDowntime(indicatorContext, equipmentId, startDate, endDate);
				default:
					throw new NotImplementedException();
			}
		}
	}
}