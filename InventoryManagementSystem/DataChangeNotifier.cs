using System;

namespace InventoryManagementSystem
{
 // Simple static notifier so forms can refresh when resequencing or other global data changes occur.
 public static class DataChangeNotifier
 {
 // TableName passed (e.g. "tbProduct")
 public static event Action<string> DataChanged;

 public static void Notify(string tableName)
 {
 try
 {
 DataChanged?.Invoke(tableName);
 }
 catch
 {
 // swallow exceptions from subscribers
 }
 }
 }
}
