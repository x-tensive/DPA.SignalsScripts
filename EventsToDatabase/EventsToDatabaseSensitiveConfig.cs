using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xtensive.Project109.Host.DPA
{
	public static class EventsToDatabaseSensitiveConfig
	{
		//Database connection string for storing validation results
		public const string TARGET_DATABASE_CONNECTION = @"Data Source=.\; Initial Catalog=ZF_Test;Integrated Security=True;";
		//@"Data Source=.\; Initial Catalog=ZF_Test;User ID=UserName;Password=Password";
	}
}
