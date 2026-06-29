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

                vehicles.Add(new SimulatedVehicle
                {
                    VehicleId = vehicle.VehicleId,
                    VehicleEconomicNumber = vehicle.EconomicNumber,
                    VehicleCompanyId = vehicle.CompanyId,
                    VehicleTypeId = vehicle.VehicleTypeId,
                    EmployeeId = employee.EmployeeId,
                    EmployeeName = employee.FullName,
                    Route = route,
                    CurrentStatus = STATUS_LOADING,
                    LastUpdate = DateTime.UtcNow.AddSeconds(-config.IntervalSeconds - 1), // Forzar que avance en el primer ciclo
                    NeedsFirstUpdate = true
                });
            }

            // Avanzar estado de cada vehículo
            var baseUrl = $"https://{config.RethinkHost}:{config.RethinkPort}";
            bool anyAdvanced = false;

            foreach (var sim in vehicles.ToList())
            {
                anyAdvanced = true;

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
                // Usar curl directamente ya que HttpClient tiene problemas con el protocolo binario de RethinkDB
                // Paso 1: Obtener conn_id
                var connResult = await RunCurl($"-sk -0 -X POST {baseUrl}/ajax/reql/open-new-connection");
                if (string.IsNullOrWhiteSpace(connResult))
                {
                    _logger.LogWarning("[RethinkBot] No se pudo obtener conn_id via curl");
                    return false;
                }
                var connId = connResult.Trim().Trim('"');

                // Paso 2: Construir el AST y enviarlo con curl via archivo temporal
                var dbAst = "[14,[\"SmartFlow\"]]";
                var tableAst = $"[15,[{dbAst},\"HaulageProcess\"]]";
                var insertOptions = "{\"conflict\":\"replace\"}";
                var globalOptions = "{\"binary_format\":\"raw\",\"time_format\":\"raw\",\"profile\":false}";
                var reqlJson = $"[1,[56,[{tableAst},{documentJson},{insertOptions}]],{globalOptions}]";

                // Escribir payload binario (8 bytes token + JSON) a archivo temporal
                var tempFile = Path.GetTempFileName();
                try
                {
                    var jsonBytes = Encoding.UTF8.GetBytes(reqlJson);
                    var token = BitConverter.GetBytes((long)1);
                    using (var fs = File.Create(tempFile))
                    {
                        await fs.WriteAsync(token, 0, 8, ct);
                        await fs.WriteAsync(jsonBytes, 0, jsonBytes.Length, ct);
                    }

                    var encodedConnId = Uri.EscapeDataString(connId);
                    var result = await RunCurl($"-sk -0 -X POST \"{baseUrl}/ajax/reql/?conn_id={encodedConnId}\" -H \"Content-Type: application/octet-stream\" --data-binary @{tempFile}");

                    return true; // Si curl no lanza excepción, consideramos exitoso
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RethinkBot] Error ejecutando query via curl");
                throw;
            }
        }

        private static async Task<string> RunCurl(string arguments)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "curl",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return "";

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
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
            var loadDate = DateTime.UtcNow.AddMinutes(-3).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var unloadDate = sim.CurrentStatus == STATUS_UNLOADING
                ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                : "null";

            var doc = new
            {
                VehicleId = sim.VehicleId,
                VehicleEconomicNumber = sim.VehicleEconomicNumber,
                VehicleCompanyId = sim.VehicleCompanyId,
                VehicleCompanyName = "LASEC",
                VehicleTypeId = sim.VehicleTypeId,
                VehicleTypeName = "DUMP TRUCKS (HAULING) (VOLQUETE)",
                EmployeeId = sim.EmployeeId,
                EmployeeName = sim.EmployeeName,
                EmployeeCompanyId = sim.VehicleCompanyId,
                EmployeeCompanyName = "LASEC",
                Status = sim.CurrentStatus,
                IsDeleted = false,
                LoadPointId = sim.Route.loadPointId,
                LoadPointName = sim.Route.loadPointName,
                UnLoadPointId = sim.Route.unLoadPointId,
                UnLoadPointName = sim.Route.unLoadPointName,
                PathId = sim.Route.haulagePathId,
                PathName = sim.Route.description,
                MaterialId = sim.Route.materialTypeId ?? 0,
                MaterialName = sim.Route.materialType ?? "MINERAL",
                LoadDate = loadDate,
                UnloadDate = sim.CurrentStatus == STATUS_UNLOADING ? unloadDate : (string?)null,
                LoadEmployeeId = sim.EmployeeId,
                LoadEmployeeName = sim.EmployeeName,
                LoadEmployeeCompanyId = sim.VehicleCompanyId,
                LoadEmployeeCompanyName = "LASEC",
                LoadVehicleId = sim.VehicleId,
                LoadVehicleEconomicNumber = sim.VehicleEconomicNumber,
                LoadVehicleCompanyId = sim.VehicleCompanyId,
                LoadVehicleCompanyName = "LASEC",
                LoadVehicleTypeId = sim.VehicleTypeId,
                LoadVehicleTypeName = "DUMP TRUCKS (HAULING) (VOLQUETE)"
            };

            return JsonConvert.SerializeObject(doc);
        }

        private class SimulatedVehicle
        {
            public int VehicleId { get; set; }
            public string VehicleEconomicNumber { get; set; } = "";
            public int VehicleCompanyId { get; set; }
            public int VehicleTypeId { get; set; }
            public int EmployeeId { get; set; }
            public string EmployeeName { get; set; } = "";
            public haulages_bot.Models.Route Route { get; set; } = null!;
            public int CurrentStatus { get; set; }
            public DateTime LastUpdate { get; set; }
            public bool NeedsFirstUpdate { get; set; } = true;
        }
    }
}
