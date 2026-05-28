using Microsoft.Data.Sqlite;
using System;

var dbPath = @"..\haulages_bot\haulage_bot.db";
using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// 1. Verificar si la columna ya existe
var checkCmd = conn.CreateCommand();
checkCmd.CommandText = "PRAGMA table_info(ServerConfigs)";
bool exists = false;
using (var r = checkCmd.ExecuteReader())
    while (r.Read())
        if (r.GetString(1) == "TimezoneOffsetHours") { exists = true; break; }

if (!exists)
{
    var addCol = conn.CreateCommand();
    addCol.CommandText = "ALTER TABLE \"ServerConfigs\" ADD \"TimezoneOffsetHours\" INTEGER NULL";
    addCol.ExecuteNonQuery();
    Console.WriteLine("OK: Columna TimezoneOffsetHours agregada.");
}
else
{
    Console.WriteLine("INFO: La columna ya existe.");
}

// 2. Limpiar el registro falso del historial (para que EF lo re-inserte limpio)
var migId = "20260528200547_AddTimezoneOffsetToServerConfig";
var del = conn.CreateCommand();
del.CommandText = $"DELETE FROM __EFMigrationsHistory WHERE MigrationId = '{migId}'";
del.ExecuteNonQuery();

// 3. Re-insertar el registro correcto
var ins = conn.CreateCommand();
ins.CommandText = $"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('{migId}', '8.0.10')";
ins.ExecuteNonQuery();

Console.WriteLine("OK: Historial de migraciones actualizado correctamente.");
conn.Close();
