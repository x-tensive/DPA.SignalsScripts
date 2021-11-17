using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace Xtensive.Project109.Host.DPA
{
	public class DatabaseAdapter
	{
		private readonly string connectionString;

		public DatabaseAdapter(string connectionString)
		{
			this.connectionString = connectionString;
		}

		public async Task WriteAsync(DataTable data)
		{
			using (var connection = new SqlConnection(connectionString)) {
				await connection.OpenAsync();
				var bulk = new SqlBulkCopy(connection) {
					DestinationTableName = data.TableName
				};
				foreach (DataColumn column in data.Columns) {
					bulk.ColumnMappings.Add(new SqlBulkCopyColumnMapping(column.ColumnName, column.ColumnName));
				}
				await bulk.WriteToServerAsync(data);
			}
		}

	}
	public static class DataTableExtensions
	{
		public static DataTable AsDataTable<T>(this IEnumerable<T> source, string tableName, Func<DataTableBuilder<T>, DataTableBuilder<T>> cfg)
		{
			var result = cfg(new DataTableBuilder<T>()).WithData(source.ToArray());
			result.TableName = tableName;
			return result;
		}
	}

	public class DataTableBuilder<TSource>
	{
		private readonly DataTable table;
		private Dictionary<string, Func<TSource, object>> Selectors = new Dictionary<string, Func<TSource, object>>();

		public DataTableBuilder()
		{
			table = new DataTable();
		}

		private Func<TSource, object> GetNullableValue<T>(Func<TSource, T> selector)
		{
			return x => {
				var value = (object)selector(x);
				if (value == null) {
					return DBNull.Value;
				}
				return value;
			};
		}

		public DataTableBuilder<TSource> WithColumn<TValue>(string name, Func<TSource, TValue> selector)
			where TValue : struct
		{
			Selectors[name] = GetNullableValue(selector);
			table.Columns.Add(name, typeof(TValue));
			return this;
		}

		public DataTableBuilder<TSource> WithColumn(string name, Func<TSource, string> selector)
		{
			Selectors[name] = GetNullableValue(selector);
			table.Columns.Add(name, typeof(string));
			return this;
		}

		public DataTableBuilder<TSource> WithColumn<TValue>(string name, Func<TSource, TValue?> selector)
			where TValue : struct
		{
			Selectors[name] = GetNullableValue(selector);
			table.Columns.Add(name, typeof(TValue));
			return this;
		}

		public DataTable WithData(TSource[] values)
		{
			foreach (var item in values) {
				var newRow = table.NewRow();
				foreach (var selector in Selectors) {
					newRow[selector.Key] = selector.Value(item);
				}
				table.Rows.Add(newRow);
			}
			return table;
		}
	}
}
