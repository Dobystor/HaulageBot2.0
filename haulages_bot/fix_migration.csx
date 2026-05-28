using Microsoft.Data.Sqlite;

var dbPath = args.Length > 0 ? args[0] : "haulage_bot.db";
var migrationId = "20260528200547_AddTimezoneOffsetToServerConfig";

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// Verificar si la columna ya existe
using var checkCmd = conn.CreateCommand();
checkCmd.CommandText = "PRAGMA table_info(ServerConfigs)";
bool columnExists = false;
using (var reader = checkCmd.ExecuteReader())
{
    while (reader.Read())
    {
        if (reader.GetString(1) == "TimezoneOffsetHours")
        {
            columnExists = true;
            break;
        }
    }
}

if (!columnExists)
{
    using var alterCmd = conn.CreateCommand();
    alterCmd.CommandText = "ALTER TABLE ServerConfigs ADD COLUMN TimezoneOffsetHours INTEGER";
    alterCmd.ExecuteNonQuery();
    Console.WriteLine("✅ Columna TimezoneOffsetHours agregada a ServerConfigs");
}
else
{
    Console.WriteLine("ℹ️  Columna TimezoneOffsetHours ya existe");
}

// Eliminar el registro falso del historial de migraciones
using var deleteCmd = conn.CreateCommand();
deleteCmd.CommandText = $"DELETE FROM __EFMigrationsHistory WHERE MigrationId = '{migrationId}'";
int deleted = deleteCmd.ExecuteNonQuery();
if (deleted > 0)
    Console.WriteLine($"🗑️  Registro falso eliminado del historial: {migrationId}");
else
    Console.WriteLine("ℹ️  No había registro previo en el historial (ya limpio)");

conn.Close();
Console.WriteLine("✅ Listo.");
