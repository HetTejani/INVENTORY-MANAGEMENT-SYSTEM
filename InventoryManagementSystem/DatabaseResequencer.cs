using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace InventoryManagementSystem
{
 public static class DatabaseResequencer
 {
 // Resequence a table's integer primary key so IDs are continuous starting at1.
 // childReferences are strings in the form "ChildTable.ChildFkColumn" (e.g. "tbOrder.cid").
 public static void ResequenceTable(string connectionString, string tableName, string idColumn, IEnumerable<string> childReferences)
 {
 if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
 if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
 if (string.IsNullOrWhiteSpace(idColumn)) throw new ArgumentNullException(nameof(idColumn));

 using (var con = new SqlConnection(connectionString))
 {
 con.Open();
 using (var tran = con.BeginTransaction())
 {
 try
 {
 //1) Build mapping NewId <-> OldId
 var cmdText = $@"
-- mapping table
IF OBJECT_ID('tempdb..#Map_{tableName}') IS NOT NULL DROP TABLE #Map_{tableName};
SELECT ROW_NUMBER() OVER (ORDER BY [{idColumn}]) AS NewId, [{idColumn}] AS OldId
INTO #Map_{tableName}
FROM [{tableName}];
";
 using (var cmd = new SqlCommand(cmdText, con, tran)) cmd.ExecuteNonQuery();

 //2) Update child foreign key columns (if any)
 if (childReferences != null)
 {
 foreach (var childRef in childReferences)
 {
 if (string.IsNullOrWhiteSpace(childRef) || !childRef.Contains('.')) continue;
 var parts = childRef.Split(new[] { '.' },2);
 var childTable = parts[0].Trim();
 var childFk = parts[1].Trim();

 // Disable constraints on child table to avoid FK errors during update
 var disable = $"ALTER TABLE [{childTable}] NOCHECK CONSTRAINT ALL;";
 using (var cmd = new SqlCommand(disable, con, tran)) cmd.ExecuteNonQuery();

 // Update child fk to new ids
 var upd = $@"
UPDATE c
SET c.[{childFk}] = m.NewId
FROM [{childTable}] c
INNER JOIN #Map_{tableName} m ON c.[{childFk}] = m.OldId;
";
 using (var cmd = new SqlCommand(upd, con, tran)) cmd.ExecuteNonQuery();

 // We'll re-enable constraints later after parent is repopulated
 }
 }

 //3) Get column list for the parent table (exclude identity/idColumn when reinserting)
 var columns = new List<string>();
 using (var cmd = new SqlCommand($@"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table ORDER BY ORDINAL_POSITION", con, tran))
 {
 cmd.Parameters.AddWithValue("@table", tableName);
 using (var rdr = cmd.ExecuteReader())
 {
 while (rdr.Read())
 {
 var col = rdr.GetString(0);
 columns.Add(col);
 }
 }
 }

 var otherCols = columns.Where(c => !string.Equals(c, idColumn, StringComparison.OrdinalIgnoreCase)).ToList();

 //4) Delete parent rows, then re-insert with new sequential IDs using identity insert if needed
 // Check if idColumn is identity
 bool isIdentity = false;
 using (var cmd = new SqlCommand($@"SELECT COLUMNPROPERTY(OBJECT_ID(@tableFull), @idCol, 'IsIdentity')", con, tran))
 {
 cmd.Parameters.AddWithValue("@tableFull", tableName);
 cmd.Parameters.AddWithValue("@idCol", idColumn);
 var val = cmd.ExecuteScalar();
 isIdentity = (val != null && val != DBNull.Value && Convert.ToInt32(val) ==1);
 }

 // Prepare delete and insert
 if (isIdentity)
 {
 // If identity, need to set IDENTITY_INSERT ON
 var sql = $@"
SET NOCOUNT ON;
DELETE FROM [{tableName}];
SET IDENTITY_INSERT [{tableName}] ON;
INSERT INTO [{tableName}] ([{idColumn}]{(otherCols.Count>0?",":"")}{string.Join(",", otherCols.Select(c=>"["+c+"]"))})
SELECT m.NewId{(otherCols.Count>0?","+string.Join(",", otherCols.Select(c=>"t.["+c+"]")):"")}
FROM [{tableName}] t
INNER JOIN #Map_{tableName} m ON t.[{idColumn}] = m.OldId
ORDER BY m.NewId;
SET IDENTITY_INSERT [{tableName}] OFF;
";
 using (var cmd = new SqlCommand(sql, con, tran)) cmd.ExecuteNonQuery();
 }
 else
 {
 var sql = $@"
SET NOCOUNT ON;
DELETE FROM [{tableName}];
INSERT INTO [{tableName}] ([{idColumn}]{(otherCols.Count>0?",":"")}{string.Join(",", otherCols.Select(c=>"["+c+"]"))})
SELECT m.NewId{(otherCols.Count>0?","+string.Join(",", otherCols.Select(c=>"t.["+c+"]")):"")}
FROM [{tableName}] t
INNER JOIN #Map_{tableName} m ON t.[{idColumn}] = m.OldId
ORDER BY m.NewId;
";
 using (var cmd = new SqlCommand(sql, con, tran)) cmd.ExecuteNonQuery();
 }

 //5) Re-enable constraints on child tables and check
 if (childReferences != null)
 {
 foreach (var childRef in childReferences)
 {
 if (string.IsNullOrWhiteSpace(childRef) || !childRef.Contains('.')) continue;
 var parts = childRef.Split(new[] { '.' },2);
 var childTable = parts[0].Trim();

 var enable = $@"ALTER TABLE [{childTable}] WITH CHECK CHECK CONSTRAINT ALL;";
 using (var cmd = new SqlCommand(enable, con, tran)) cmd.ExecuteNonQuery();
 }
 }

 tran.Commit();
 }
 catch (Exception)
 {
 try { tran.Rollback(); } catch { }
 throw;
 }
 }
 }
 }
 }
}
