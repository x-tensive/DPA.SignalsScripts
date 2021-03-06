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

		//Email address where to send validation result
		public const string EMAIL = "validation@mail.com";

		//Name of state, which contains VALIDATIE field 
		public const string VALIDATION_TRIGGER_EVENT_NAME = "GUD5";
		public const int VALIDATION_CHANNEL = 1;
		public const string VALIDATION_TRIGGER = "VALIDATIE";
		public const string VALIDATION_TRIGGER_VALUE = "1";

		//in milliseconds
		public const int DELAY_BEFORE_VALIDATION = 10000;
	}
}
