using System;
using System.Collections.Generic;
using System.Linq;
using Xtensive.Core;
using Xtensive.DPA.EventManager;
using Xtensive.Orm;

namespace Xtensive.Project109.Host.DPA
{
	public class IdleSpeedAdjustmentDowntime : AdjustmentDowntimeReason
	{
		// Reason name from reference book reasons of downtime. It's used for classification adjustment downtime period
		public const string ReasonName = "Холостой ход при работе без управляющей программы";

		private static readonly Dictionary<EventInfoType, Predicate<double>> eventPredicateDict = new Dictionary<EventInfoType, Predicate<double>>() {
			// Spindle speed > 0
			{ EventInfoType.SpindleSpeed,  (val) => Math.Abs(val) > double.Epsilon },
			// Spindle load < 2%
			{ EventInfoType.SpindleLoad, (val) => Math.Abs(val) < 2.0 }
		};

		protected override Dictionary<EventInfoType, Predicate<double>> EventPredicateDict => eventPredicateDict;

		public IdleSpeedAdjustmentDowntime(IIndicatorContext indicatorContext, long equipmentId, DateTimeOffset startDate, DateTimeOffset endDate)
			: base(indicatorContext, ReasonName, equipmentId, startDate, endDate)
		{

		}
	}


	public class NoNcProgramAdjustmentDowntime : AdjustmentDowntimeReason
	{
		// Reason name from reference book reasons of downtime. It's used for classification adjustment downtime period
		public const string ReasonName = "Работа без управляющей программы";

		private static readonly Dictionary<EventInfoType, Predicate<double>> eventPredicateDict = new Dictionary<EventInfoType, Predicate<double>>() {
			// Spindle speed > 0
			{ EventInfoType.SpindleSpeed,  (val) => Math.Abs(val) > double.Epsilon },
			// Spindle load >= 2%
			{ EventInfoType.SpindleLoad, (val) => Math.Abs(val) >= 2.0 },
			// Feedrate > 0
			{ EventInfoType.FeedRate, (val) => Math.Abs(val) > double.Epsilon }
		};

		protected override Dictionary<EventInfoType, Predicate<double>> EventPredicateDict => eventPredicateDict;

		public NoNcProgramAdjustmentDowntime(IIndicatorContext indicatorContext, long equipmentId, DateTimeOffset startDate, DateTimeOffset endDate)
			: base(indicatorContext, ReasonName, equipmentId, startDate, endDate)
		{
		}
	}

	public abstract class AdjustmentDowntimeReason
	{
		public ReferenceBookReasonsOfDowntime Reason { get; set; }
		public long EquipmentId { get; }
		protected abstract Dictionary<EventInfoType, Predicate<double>> EventPredicateDict { get; }
		public DateTimeOffset StartDate { get; set; }
		public DateTimeOffset EndDate { get; set; }

		public DateTimeSegments Segments { get; set; }

		protected AdjustmentDowntimeReason(IIndicatorContext indicatorContext, 
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



		private static DateTimeSegments BuildFilteredDateTimeSegments(List<IndicatorValue> values, Predicate<double> filter)
		{
			var result = new DateTimeSegments();

			DateTimeOffset? startSegment = null;
			var lastValue = values.Last();

			foreach (var item in values) {
				var currentIndicatorValue = IndicatorSimpleModel.GetDoubleValue(item.Value);
				var isSatisfiedValue = filter(currentIndicatorValue);

				if (isSatisfiedValue && !startSegment.HasValue && item != lastValue) {
					startSegment = item.TimeStamp;
				}
				else if (!isSatisfiedValue && startSegment.HasValue && startSegment.Value != item.TimeStamp) {
					result.Add(new DateTimeSegment(startSegment.Value, item.TimeStamp));
					startSegment = null;
				}
			}

			if (startSegment.HasValue && lastValue.TimeStamp != startSegment.Value) {
				result.Add(new DateTimeSegment(startSegment.Value, lastValue.TimeStamp));
			}

			return result;
		}

		private DateTimeSegments GetDateTimeSegmentsToClassify(IIndicatorContext indicatorContext)
		{
			DateTimeSegments result = null;

			foreach (var eventPredicate in EventPredicateDict) {
				var indicator = Query.All<Indicator>()
					.FirstOrDefault(indic => indic.Device.Owner.Id == EquipmentId && indic.StateName == eventPredicate.Key.GetStateName());

				if (indicator == null) {
					throw new IndicatorNotFoundException();
				}

				var values = indicatorContext.GetIndicatorData(indicator.Id, StartDate, EndDate);
				if (values.IsNullOrEmpty()) {
					return null;
				}

				var dtSegments = BuildFilteredDateTimeSegments(values.OrderBy(v => v.TimeStamp).ToList(), eventPredicate.Value);
				if (dtSegments.IsNullOrEmpty()) {
					return null;
				}

				result = result == null ? dtSegments : result.Intersection(dtSegments);
			}

			return result;
		}
	}

	public static class AdjustmentDowntimeCreator
	{
		private static readonly List<string> reasons = new List<string> { IdleSpeedAdjustmentDowntime.ReasonName, NoNcProgramAdjustmentDowntime.ReasonName };
		public static List<string> Reasons => reasons;

		static AdjustmentDowntimeCreator()
		{
		}

		public static AdjustmentDowntimeReason Create(IIndicatorContext indicatorContext, string reasonName, long equipmentId, DateTimeOffset startDate, DateTimeOffset endDate)
		{
			switch (reasonName) {
				case IdleSpeedAdjustmentDowntime.ReasonName:
					return new IdleSpeedAdjustmentDowntime(indicatorContext, equipmentId, startDate, endDate);
				case NoNcProgramAdjustmentDowntime.ReasonName:
					return new NoNcProgramAdjustmentDowntime(indicatorContext, equipmentId, startDate, endDate);
				default:
					throw new NotImplementedException();
			}
		}
	}
}
