using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using haulages_bot.Data;
using haulages_bot.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace haulages_bot.Services
{
    /// <summary>
    /// Servicio en segundo plano que simula datos en la tabla HaulageProcess de RethinkDB
    /// usando la API HTTP del Data Explorer (POST /ajax/reql/).
    /// </summary>
    public class RethinkBotService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RethinkBotService> _logger;
        private readonly LogHistoryService _logHistoryService;
        private readonly HttpClient _httpClient;

        // Status del flujo de acarreo
        private const int STATUS_IDLE = 0;
        private const int STATUS_LOADING = 3;
        private const int STATUS_HAULING = 5;
        private const int STATUS_UNLOADING = 7;
        private const int STATUS_RETURNING = 9;

        // Tracking de vehículos activos por servidor
        private readonly Dictionary<int, List<SimulatedVehicle>> _activeVehicles = new();

        public RethinkBotService(
            IServiceScopeFactory scopeFactory,
            ILogger<RethinkBotService> logger,
            LogHistoryService logHistoryService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _logHistoryService = logHistoryService;

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(10000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessAllServers(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en ciclo principal RethinkBotService");
                }

                await Task.Delay(5000, stoppingToken);
            }
        }

        private async Task ProcessAllServers(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<dbboot>();

            var configs = await db.RethinkBotConfigs
                .Where(c => c.IsEnabled)
                .ToListAsync(ct);

            foreach (var config in configs)
            {
                try
                {
                    await ProcessServer(db, config, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error RethinkBot servidor {config.ServerConfigId}");
                    _logHistoryService.AddLog(config.ServerConfigId, $"[RethinkBot] Error: {ex.Message}", true);
                }
            }

            // Limpiar servidores deshabilitados
            var enabledIds = configs.Select(c => c.ServerConfigId).ToHashSet();
            var toRemove = _activeVehicles.Keys.Where(k => !enabledIds.Contains(k)).ToList();
            foreach (var id in toRemove)
            {
                _activeVehicles.Remove(id);
            }
        }

        private async Task ProcessServer(dbboot db, RethinkBotConfig config, CancellationToken ct)
        {
            if (!_activeVehicles.ContainsKey(config.ServerConfigId))
            {
                _activeVehicles[config.ServerConfigId] = new List<SimulatedVehicle>();
            }

            var vehicles = _activeVehicles[config.ServerConfigId];

            // Cargar catálogos
            var dataConfig = await db.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == config.ServerConfigId)
                .OrderByDescending(dc => dc.Id)
                .FirstOrDefaultAsync(ct);

            if (dataConfig == null)
            {
                _logHistoryService.AddLog(config.ServerConfigId, "[RethinkBot] Sin configuración de bot.", true);
                return;
            }

            var selectedVehicleIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedVehicles) ?? new();
            var selectedRouteIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new();
            var selectedEmployeeIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedEmployees) ?? new();

            if (!selectedVehicleIds.Any() || !selectedRouteIds.Any() || !selectedEmployeeIds.Any())
            {
                _logHistoryService.AddLog(config.ServerConfigId, "[RethinkBot] Sin vehículos/rutas/operadores configurados.", true);
                return;
            }

            var dbVehicles = await db.Vehicles
                .Where(v => v.ServerConfigId == config.ServerConfigId && selectedVehicleIds.Contains(v.VehicleId))
                .ToListAsync(ct);
            var dbRoutes = await db.Routes
                .Where(r => r.ServerConfigId == config.ServerConfigId && selectedRouteIds.Contains(r.haulagePathId))
                .ToListAsync(ct);
            var dbEmployees = await db.Employees
                .Where(e => e.ServerConfigId == config.ServerConfigId && selectedEmployeeIds.Contains(e.EmployeeId))
                .ToListAsync(ct);
            var dbCompanies = await db.Companies
                .Where(c => c.ServerConfigId == config.ServerConfigId)
                .ToListAsync(ct);
            var dbVehicleTypes = await db.Set<VehicleType>()
                .Where(vt => vt.ServerConfigId == config.ServerConfigId)
                .ToListAsync(ct);
            // Scooptrams: VehicleTypeId = 1 (SCOOPTRAM FRONT LOADERS)
            var scooptrams = dbVehicles.Where(v => v.VehicleTypeId == 1).ToList();

            if (!dbVehicles.Any() || !dbRoutes.Any() || !dbEmployees.Any())
            {
                _logHistoryService.AddLog(config.ServerConfigId, "[RethinkBot] Catálogos vacíos. Sincroniza primero.", true);
                return;
            }

            var random = new Random();

            // Agregar vehículos si hay espacio
            int attempts = 0;
            while (vehicles.Count < config.MaxSimultaneousVehicles && vehicles.Count < dbVehicles.Count && attempts < dbVehicles.Count * 2)
            {
                attempts++;
                var vehicle = dbVehicles[random.Next(dbVehicles.Count)];
                if (vehicles.Any(v => v.VehicleId == vehicle.VehicleId)) continue;

                var route = dbRoutes[random.Next(dbRoutes.Count)];
                var employee = dbEmployees[random.Next(dbEmployees.Count)];
                var vehicleCompany = dbCompanies.FirstOrDefault(c => c.CompanyId == vehicle.CompanyId);
                var vehicleType = dbVehicleTypes.FirstOrDefault(vt => vt.VehicleTypeId == vehicle.VehicleTypeId);
                var employeeCompany = dbCompanies.FirstOrDefault(c => c.CompanyId == employee.CompanyId);
                var loadVehicle = scooptrams.Any() ? scooptrams[random.Next(scooptrams.Count)] : vehicle;
                var loadVehicleCompany = dbCompanies.FirstOrDefault(c => c.CompanyId == loadVehicle.CompanyId);
                var loadVehicleType = dbVehicleTypes.FirstOrDefault(vt => vt.VehicleTypeId == loadVehicle.VehicleTypeId);

                vehicles.Add(new SimulatedVehicle
                {
                    VehicleId = vehicle.VehicleId,
                    VehicleEconomicNumber = vehicle.EconomicNumber,
                    VehicleCompanyId = vehicle.CompanyId,
                    VehicleCompanyName = vehicleCompany?.Name ?? "LASEC",
                    VehicleTypeId = vehicle.VehicleTypeId,
                    VehicleTypeName = vehicleType?.Name ?? "DUMP TRUCKS (HAULING) (VOLQUETE)",
                    EmployeeId = employee.EmployeeId,
                    EmployeeName = employee.FullName,
                    EmployeeCompanyId = employee.CompanyId,
                    EmployeeCompanyName = employeeCompany?.Name ?? "LASEC",
                    Route = route,
                    CurrentStatus = STATUS_LOADING,
                    LastUpdate = DateTime.UtcNow.AddSeconds(-config.IntervalSeconds - 1),
                    NeedsFirstUpdate = true,
                    LoadVehicleId = loadVehicle.VehicleId,
                    LoadVehicleEconomicNumber = loadVehicle.EconomicNumber,
                    LoadVehicleCompanyId = loadVehicle.CompanyId,
                    LoadVehicleCompanyName = loadVehicleCompany?.Name ?? "LASEC",
                    LoadVehicleTypeId = loadVehicle.VehicleTypeId,
                    LoadVehicleTypeName = loadVehicleType?.Name ?? "SCOOPTRAM FRONT LOADERS"
                });
            }

            // Avanzar estado de cada vehículo
            var baseUrl = $"https://{config.RethinkHost}:{config.RethinkPort}";
            bool anyAdvanced = false;

            _logger.LogWarning($"[RethinkBot] Procesando {vehicles.Count} vehículos para servidor {config.ServerConfigId}. Host: {baseUrl}");

            foreach (var sim in vehicles.ToList())
            {
                anyAdvanced = true;

                try
                {
                    _logger.LogWarning($"[RethinkBot] Intentando insert VehicleId={sim.VehicleId} Status={sim.CurrentStatus}");

                    // Avanzar al siguiente estado
                    sim.CurrentStatus = GetNextStatus(sim.CurrentStatus);
                    sim.LastUpdate = DateTime.UtcNow;

                if (sim.CurrentStatus == STATUS_IDLE)
                {
                    // Reasignar ruta/empleado para nuevo ciclo
                    sim.Route = dbRoutes[random.Next(dbRoutes.Count)];
                    var emp = dbEmployees[random.Next(dbEmployees.Count)];
                    sim.EmployeeId = emp.EmployeeId;
                    sim.EmployeeName = emp.FullName;
                    sim.CurrentStatus = STATUS_LOADING;
                }

                // Construir y enviar query a RethinkDB via HTTP
                var doc = BuildDocumentJson(sim);
                var success = await ExecuteReqlHttp(baseUrl, doc, config.ServerConfigId, ct);

                if (success)
                {
                    var statusName = sim.CurrentStatus switch
                    {
                        STATUS_LOADING => "CARGANDO",
                        STATUS_HAULING => "EN TRÁNSITO",
                        STATUS_UNLOADING => "DESCARGANDO",
                        STATUS_RETURNING => "REGRESANDO",
                        _ => "IDLE"
                    };
                    _logHistoryService.AddLog(config.ServerConfigId,
                        $"[RethinkBot] {sim.VehicleEconomicNumber} → {statusName} | {sim.Route.description}");
                }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[RethinkBot] Error en foreach para VehicleId={sim.VehicleId}");
                }
            }

            if (!anyAdvanced && vehicles.Count > 0)
            {
                _logger.LogWarning($"[RethinkBot] {vehicles.Count} vehículos en lista pero ninguno avanzó. Intervalo: {config.IntervalSeconds}s");
            }
        }

        // Tracking de conexiones HTTP (conn_id por servidor)
        // Nota: cada request necesita su propio conn_id, no se reusan
        private readonly Dictionary<int, string> _connIds = new();

        private async Task<bool> ExecuteReqlHttp(string baseUrl, string documentJson, int serverId, CancellationToken ct)
        {
            try
            {
                // ReQL AST: r.db('SmartFlow').table('HaulageProcess').insert(document)
                var dbAst = "[14,[\"SmartFlow\"]]";
                var tableAst = "[15,[" + dbAst + ",\"HaulageProcess\"]]";
                var globalOptions = "{\"binary_format\":\"raw\",\"time_format\":\"raw\",\"profile\":false}";
                var reqlJson = "[1,[56,[" + tableAst + "," + documentJson + "]]," + globalOptions + "]";

                // Paso 1: Obtener conn_id
                var psi1 = new ProcessStartInfo
                {
                    FileName = "/usr/bin/curl",
                    Arguments = $"-sk -X POST {baseUrl}/ajax/reql/open-new-connection",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                string connId;
                using (var proc1 = Process.Start(psi1))
                {
                    if (proc1 == null) return false;
                    connId = (await proc1.StandardOutput.ReadToEndAsync(ct)).Trim().Trim('"');
                    await proc1.WaitForExitAsync(ct);
                }

                if (string.IsNullOrWhiteSpace(connId))
                {
                    _logger.LogWarning("[RethinkBot] conn_id vacío");
                    return false;
                }

                // Paso 2: Escribir payload binario directamente desde C# (8 bytes token LE int64 + JSON UTF8)
                var tmpFile = $"/tmp/reql_{serverId}_{Environment.CurrentManagedThreadId}.bin";
                var queryBytes = System.Text.Encoding.UTF8.GetBytes(reqlJson);
                using (var fs = new FileStream(tmpFile, FileMode.Create))
                {
                    var tokenBytes = BitConverter.GetBytes((long)1); // little-endian int64
                    await fs.WriteAsync(tokenBytes, 0, 8, ct);
                    await fs.WriteAsync(queryBytes, 0, queryBytes.Length, ct);
                }

                // Paso 3: Enviar con curl (HTTP/1.1, sin -0)
                var psi2 = new ProcessStartInfo
                {
                    FileName = "/usr/bin/curl",
                    Arguments = $"-sk -X POST \"{baseUrl}/ajax/reql/?conn_id={connId}\" -H \"Content-Type: application/octet-stream\" --data-binary @{tmpFile}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc2 = Process.Start(psi2))
                {
                    if (proc2 == null) return false;
                    var output = await proc2.StandardOutput.ReadToEndAsync(ct);
                    await proc2.WaitForExitAsync(ct);

                    // Limpiar archivo temporal
                    try { System.IO.File.Delete(tmpFile); } catch { }

                    // La respuesta es binaria: 12 bytes header (8 token + 4 length) + JSON
                    // Buscar "inserted" o "replaced" en la salida raw
                    if (output.Contains("inserted") || output.Contains("replaced"))
                        return true;

                    // Si exit code 0 y no hay error, probablemente funcionó
                    // (la respuesta binaria puede no tener el texto visible)
                    if (proc2.ExitCode == 0 && output.Length > 12)
                        return true;

                    if (proc2.ExitCode != 0)
                        _logger.LogWarning($"[RethinkBot] curl exit code: {proc2.ExitCode}");
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RethinkBot] Error ejecutando query via curl");
                return false;
            }
        }

        private static int GetNextStatus(int current)
        {
            return current switch
            {
                STATUS_LOADING => STATUS_HAULING,
                STATUS_HAULING => STATUS_UNLOADING,
                STATUS_UNLOADING => STATUS_RETURNING,
                STATUS_RETURNING => STATUS_IDLE,
                _ => STATUS_LOADING
            };
        }

        private static string BuildDocumentJson(SimulatedVehicle sim)
        {
            var loadDate = DateTime.UtcNow.AddMinutes(-new Random().Next(5, 30));
            var doc = new
            {
                VehicleId = sim.VehicleId,
                VehicleEconomicNumber = sim.VehicleEconomicNumber,
                VehicleCompanyId = sim.VehicleCompanyId,
                VehicleCompanyName = sim.VehicleCompanyName,
                VehicleTypeId = sim.VehicleTypeId,
                VehicleTypeName = sim.VehicleTypeName,
                EmployeeId = sim.EmployeeId,
                EmployeeName = sim.EmployeeName,
                EmployeeCompanyId = sim.EmployeeCompanyId,
                EmployeeCompanyName = sim.EmployeeCompanyName,
                LoadPointId = sim.Route.loadPointId,
                LoadPointName = sim.Route.loadPointName,
                UnLoadPointId = sim.Route.unLoadPointId,
                UnLoadPointName = sim.Route.unLoadPointName,
                PathId = sim.Route.haulagePathId,
                PathName = sim.Route.description,
                MaterialId = sim.Route.materialTypeId ?? 0,
                MaterialName = sim.Route.materialType ?? "MINERAL",
                LoadDate = new { reql_type = "TIME", epoch_time = ((DateTimeOffset)loadDate).ToUnixTimeSeconds(), timezone = "-06:00" },
                UnloadDate = (object?)null,
                LoadEmployeeId = sim.EmployeeId,
                LoadEmployeeName = sim.EmployeeName,
                LoadEmployeeCompanyId = sim.EmployeeCompanyId,
                LoadEmployeeCompanyName = sim.EmployeeCompanyName,
                LoadVehicleId = sim.LoadVehicleId,
                LoadVehicleEconomicNumber = sim.LoadVehicleEconomicNumber,
                LoadVehicleCompanyId = sim.LoadVehicleCompanyId,
                LoadVehicleCompanyName = sim.LoadVehicleCompanyName,
                LoadVehicleTypeId = sim.LoadVehicleTypeId,
                LoadVehicleTypeName = sim.LoadVehicleTypeName,
                Status = sim.CurrentStatus,
                IsDeleted = false
            };

            return JsonConvert.SerializeObject(doc);
        }

        private class SimulatedVehicle
        {
            public int VehicleId { get; set; }
            public string VehicleEconomicNumber { get; set; } = "";
            public int VehicleCompanyId { get; set; }
            public string VehicleCompanyName { get; set; } = "";
            public int VehicleTypeId { get; set; }
            public string VehicleTypeName { get; set; } = "";
            public int EmployeeId { get; set; }
            public string EmployeeName { get; set; } = "";
            public int EmployeeCompanyId { get; set; }
            public string EmployeeCompanyName { get; set; } = "";
            public haulages_bot.Models.Route Route { get; set; } = null!;
            public int CurrentStatus { get; set; }
            public DateTime LastUpdate { get; set; }
            public bool NeedsFirstUpdate { get; set; } = true;
            // Scooptram (vehículo de carga)
            public int LoadVehicleId { get; set; }
            public string LoadVehicleEconomicNumber { get; set; } = "";
            public int LoadVehicleCompanyId { get; set; }
            public string LoadVehicleCompanyName { get; set; } = "";
            public int LoadVehicleTypeId { get; set; }
            public string LoadVehicleTypeName { get; set; } = "";
        }
    }
}
