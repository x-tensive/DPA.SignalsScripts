using System;
using System.Collections.Generic;

namespace Xtensive.Project109.Host.DPA
{
	public class SettingsDowntimeUnclassified {
		public TimeSpan WorkerDelay = TimeSpan.FromMinutes(1);
		public TimeSpan UnclassifiedIgnoreDuration = TimeSpan.FromMinutes(7);
		public Dictionary<long, List<EquipmentSettingsDowntimeUnclassified>> EquipmentsSettings
		= new Dictionary<long, List<EquipmentSettingsDowntimeUnclassified>> {
			//номер рабочего центра
			{1, 
				//настройки для этого центра
				new List<EquipmentSettingsDowntimeUnclassified>{
					new EquipmentSettingsDowntimeUnclassified {
						Duration = TimeSpan.FromMinutes(60),
						PersonnelNumbers = new string[] { "2015984033", "234" },
						TemplateId = 3639519
					},
				}
			}
		};
	}
	public class EquipmentSettingsDowntimeUnclassified
	{
		/// <summary>
		/// Через сколько послать
		/// </summary>
		public TimeSpan Duration { get; set; }
		/// <summary>
		/// Номер шаблона сообщения
		/// </summary>
		public long TemplateId { get; set; }
		
		/// <summary>
		/// Группа получателей
		/// </summary>
		public long? GroupId { get; set; }
		/// <summary>
		/// Табельные номера
		/// </summary>
		public string[] PersonnelNumbers { get; set; }
	}

	public class CommonDowntimeUnclassified
	{
		public long EquipmentId { get; set; }
		public DateTimeOffset StartDate { get; set; }
		public long ReasonId { get; set; }
		public int LevelId { get; set; }

		public override string ToString()
		{
			return string.Format("{0} {1} {2} {3}", EquipmentId, LevelId, ReasonId, StartDate);
		}
	}
}
