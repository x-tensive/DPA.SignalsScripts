using System;
using Microsoft.Extensions.Logging;

namespace Xtensive.Project109.Host.DPA
{
	public static class ZF_Config
	{
		public const string LM_NUMBER_FIELD = "LMNUMMER";
		public const string LM_NUMBER_STATE = "GUD5";

		//Machine register for validation result
		public const string TARGET_RESULT_URL = "123";
		//Machine register for validation message
		public const string TARGET_MESSAGE_URL = "123";

		//Machine folder for storing validation results
		public const string TARGET_FOLDER = "some folder of machine";
		public const string VALIDATION_FILE_NAME = "some file name";

		//Windows folder for storing validation results
		public const string LOG_DESTINATION = @"D:\Logs\Momentum Data";

		public static readonly Guid VALIDATION_TRIGGER_EVENT_ID = Guid.Parse("fef321ed-6a5e-4538-afb8-711bfee7351e");
		public const int VALIDATION_CHANNEL = 1;
		public const string VALIDATION_TRIGGER = "VALIDATIE";
		public const string VALIDATION_TRIGGER_VALUE = "1";

		//in milliseconds (1 sec = 1000 ms)
		public const int MAX_DIFF_TRIGGER_TO_PARAM_TIMESTAMP = 1000;
	}
}
