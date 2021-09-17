namespace Xtensive.Project109.Host.DPA
{
	public static class ZF_Config
	{
		//Database connection string for storing validation results
		public const string TARGET_DATABASE_CONNECTION = @"Data Source=.\; Initial Catalog=ZF_Test;Integrated Security=True;";
		//@"Data Source=.\; Initial Catalog=ZF_Test;User ID=UserName;Password=Password";

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

		public const string VALIDATION_TRIGGER = "VALIDATIE";
		public const string VALIDATION_TRIGGER_VALUE = "1";
	}
}
