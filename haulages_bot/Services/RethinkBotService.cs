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
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace haulages_bot.Services
{
    public class RethinkBotService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RethinkBotService> _logger;
        private readonly LogHistoryService _logHistoryService;
        private readonly IHttpClientFactory _httpClientFactory;

        private const int STATUS_LOADING = 3;
        private const int STATUS_HAULING = 5;
        private const int STATUS_UNLOADING = 7;
        private const int STATUS_RETURNING = 9;

        private readonly Dictionary<int, List<SimVehicle>> _fleet = new();
        private int _lastWorkshiftId = -1;

        public RethinkBotService(IServiceScopeFactory scopeFactory, ILogger<RethinkBotService> logger,
            LogHistoryService logHistoryService, IHttpClientFactory httpClientFactory)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _logHistoryService = logHistoryService;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            await Task.Delay(12000, ct);
            while (!ct.IsCancellationRequested)
            {
                try { await Tick(ct); }
                catch (Exception ex) { _logger.LogError(ex, "[RethinkBot] Error en tick"); }
                await Task.Delay(5000, ct);
            }
        }

        private async Task Tick(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<dbboot>();
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

            var configs = await db.RethinkBotConfigs.Where(c => c.IsEnabled).ToListAsync(ct);

            foreach (var config in configs)
            {
                try
                {
                    var server = await db.ServerConfigs.FindAsync(config.ServerConfigId);
                    if (server == null) continue;

                    // Detectar cambio de turno
                    var workshiftId = await GetCurrentWorkshift(server, tokenService, ct);
                    if (workshiftId > 0 && _lastWorkshiftId > 0 && workshiftId != _lastWorkshiftId)
                    {
                        // Cambio de turno: marcar todos como deleted y limpiar flota
                        await MarkAllDeleted(config, ct);
                        _fleet.Remove(config.ServerConfigId);
                        _logHistoryService.AddLog(config.ServerConfigId, $"[RethinkBot] Cambio de turno ({workshiftId}). Flota renovada.");
                    }
                    if (workshiftId > 0) _lastWorkshiftId = workshiftId;

                    // Inicializar flota si no existe
                    if (!_fleet.ContainsKey(config.ServerConfigId))
                    {
                        await InitFleet(db, config, ct);
                    }

                    // Avanzar status de cada vehículo
                    await AdvanceFleet(config, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[RethinkBot] Error server {config.ServerConfigId}");
                }
            }
        }

        private async Task InitFleet(dbboot db, RethinkBotConfig config, CancellationToken ct)
        {
            var dataConfig = await db.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == config.ServerConfigId)
                .OrderByDescending(dc => dc.Id).FirstOrDefaultAsync(ct);
            if (dataConfig == null) return;

            var selectedVehicleIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedVehicles) ?? new();
            var selectedRouteIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new();
            var selectedEmployeeIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedEmployees) ?? new();

            var vehicles = await db.Vehicles.Where(v => v.ServerConfigId == config.ServerConfigId && selectedVehicleIds.Contains(v.VehicleId)).ToListAsync(ct);
            var routes = await db.Routes.Where(r => r.ServerConfigId == config.ServerConfigId && selectedRouteIds.Contains(r.haulagePathId) && r.isEnabled).ToListAsync(ct);
            var employees = await db.Employees.Where(e => e.ServerConfigId == config.ServerConfigId && selectedEmployeeIds.Contains(e.EmployeeId)).ToListAsync(ct);
            var companies = await db.Companies.Where(c => c.ServerConfigId == config.ServerConfigId).ToListAsync(ct);
            var vehicleTypes = await db.Set<VehicleType>().Where(vt => vt.ServerConfigId == config.ServerConfigId).ToListAsync(ct);

            if (!vehicles.Any() || !routes.Any() || !employees.Any()) return;

            var random = new Random();
            var fleet = new List<SimVehicle>();

            // Volteos/Bajo perfil (de la config general)
            var dumpTrucks = vehicles.Where(v => v.VehicleTypeId != 1).ToList(); // No scooptrams
            var numTrucks = Math.Min(config.MaxSimultaneousVehicles, dumpTrucks.Count);
            var selectedTrucks = dumpTrucks.OrderBy(_ => random.Next()).Take(numTrucks).ToList();

            foreach (var v in selectedTrucks)
            {
                var route = routes[random.Next(routes.Count)];
                var emp = employees[random.Next(employees.Count)];
                var company = companies.FirstOrDefault(c => c.CompanyId == v.CompanyId);
                var vType = vehicleTypes.FirstOrDefault(vt => vt.VehicleTypeId == v.VehicleTypeId);
                var empCompany = companies.FirstOrDefault(c => c.CompanyId == emp.CompanyId);

                fleet.Add(new SimVehicle
                {
                    VehicleId = v.VehicleId, EconomicNumber = v.EconomicNumber,
                    CompanyId = v.CompanyId, CompanyName = company?.Name ?? "LASEC",
                    VehicleTypeId = v.VehicleTypeId, VehicleTypeName = vType?.Name ?? "DUMP TRUCKS (HAULING) (VOLQUETE)",
                    EmployeeId = emp.EmployeeId, EmployeeName = emp.FullName,
                    EmployeeCompanyId = emp.CompanyId, EmployeeCompanyName = empCompany?.Name ?? "LASEC",
                    Route = route, Status = STATUS_LOADING + (random.Next(4) * 2), // Random initial status
                    IsScooptram = false
                });
            }

            // Scooptrams: obtener de la API remota (no dependen de config general)
            var server = await db.ServerConfigs.FindAsync(config.ServerConfigId);
            if (server != null && config.ScooptramCount > 0)
            {
                try
                {
                    using var scoopScope = _scopeFactory.CreateScope();
                    var tokenService = scoopScope.ServiceProvider.GetRequiredService<TokenService>();
                    var httpClient = _httpClientFactory.CreateClient();
                    var token = await tokenService.GetTokenAsync(server.Id);
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                    var resp = await httpClient.GetAsync($"{host}/Catalog/GetAllVehicles", ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync(ct);
                        var allApiVehicles = JsonConvert.DeserializeObject<List<ApiVehicleDto>>(json) ?? new();
                        var apiScoops = allApiVehicles.Where(v => v.VehicleTypeId == 1).OrderBy(_ => random.Next()).Take(config.ScooptramCount).ToList();

                        foreach (var s in apiScoops)
                        {
                            var route = routes[random.Next(routes.Count)];
                            var emp = employees[random.Next(employees.Count)];
                            var empCompany = companies.FirstOrDefault(c => c.CompanyId == emp.CompanyId);

                            fleet.Add(new SimVehicle
                            {
                                VehicleId = s.VehicleId, EconomicNumber = s.EconomicNumber ?? "",
                                CompanyId = s.CompanyId, CompanyName = s.CompanyName ?? "LASEC",
                                VehicleTypeId = 1, VehicleTypeName = s.VehicleTypeName ?? "SCOOPTRAM FRONT LOADERS",
                                EmployeeId = emp.EmployeeId, EmployeeName = emp.FullName,
                                EmployeeCompanyId = emp.CompanyId, EmployeeCompanyName = empCompany?.Name ?? "LASEC",
                                Route = route, Status = STATUS_LOADING,
                                IsScooptram = true
                            });
                        }
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[RethinkBot] Error obteniendo scooptrams del API"); }
            }

            // Asegurar al menos 1 en descarga
            var anyUnloading = fleet.Any(f => !f.IsScooptram && f.Status == STATUS_UNLOADING);
            if (!anyUnloading && fleet.Any(f => !f.IsScooptram))
            {
                fleet.First(f => !f.IsScooptram).Status = STATUS_UNLOADING;
            }

            _fleet[config.ServerConfigId] = fleet;
            _logHistoryService.AddLog(config.ServerConfigId, $"[RethinkBot] Flota inicializada: {selectedTrucks.Count} camiones + {fleet.Count(f => f.IsScooptram)} scooptrams.");
        }

        private async Task AdvanceFleet(RethinkBotConfig config, CancellationToken ct)
        {
            if (!_fleet.ContainsKey(config.ServerConfigId)) return;
            var fleet = _fleet[config.ServerConfigId];
            var random = new Random();
            var baseUrl = $"https://{config.RethinkHost}:{config.RethinkPort}";

            foreach (var sim in fleet)
            {
                if (sim.IsScooptram)
                {
                    // Scooptrams: mayormente cargando, a veces en tránsito, cambian sitio ocasionalmente
                    if (random.Next(10) < 2) // 20% chance de cambiar a tránsito momentáneo
                        sim.Status = STATUS_HAULING;
                    else
                        sim.Status = STATUS_LOADING;

                    // 30% chance de cambiar sitio de carga
                    if (random.Next(10) < 3)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<dbboot>();
                        var dataConfig = await db.DataConfigurationLocal.Where(dc => dc.ServerConfigId == config.ServerConfigId).OrderByDescending(dc => dc.Id).FirstOrDefaultAsync(ct);
                        var selRouteIds = dataConfig != null ? JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new() : new List<int>();
                        var routes = await db.Routes.Where(r => r.ServerConfigId == config.ServerConfigId && selRouteIds.Contains(r.haulagePathId) && r.isEnabled).ToListAsync(ct);
                        if (routes.Any()) sim.Route = routes[random.Next(routes.Count)];
                    }
                }
                else
                {
                    // Volteos: avanzar status 3→5→7→9→3
                    sim.Status = sim.Status switch
                    {
                        STATUS_LOADING => STATUS_HAULING,
                        STATUS_HAULING => STATUS_UNLOADING,
                        STATUS_UNLOADING => STATUS_RETURNING,
                        STATUS_RETURNING => STATUS_LOADING,
                        _ => STATUS_LOADING
                    };

                    // Al volver a LOADING, cambiar ruta
                    if (sim.Status == STATUS_LOADING)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<dbboot>();
                        var dataConfig = await db.DataConfigurationLocal.Where(dc => dc.ServerConfigId == config.ServerConfigId).OrderByDescending(dc => dc.Id).FirstOrDefaultAsync(ct);
                        var selRouteIds = dataConfig != null ? JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new() : new List<int>();
                        var routes = await db.Routes.Where(r => r.ServerConfigId == config.ServerConfigId && selRouteIds.Contains(r.haulagePathId) && r.isEnabled).ToListAsync(ct);
                        if (routes.Any()) sim.Route = routes[random.Next(routes.Count)];
                    }
                }

                // Insertar en RethinkDB
                var doc = BuildDocument(sim);
                await ExecuteReqlInsert(baseUrl, doc, config.ServerConfigId, ct);
            }

            // Garantizar al menos 1 en descarga
            var trucks = fleet.Where(f => !f.IsScooptram).ToList();
            if (trucks.Any() && !trucks.Any(t => t.Status == STATUS_UNLOADING))
            {
                var target = trucks[random.Next(trucks.Count)];
                target.Status = STATUS_UNLOADING;
                var doc = BuildDocument(target);
                await ExecuteReqlInsert(baseUrl, doc, config.ServerConfigId, ct);
            }

            // Log del primer vehículo
            var first = fleet.FirstOrDefault();
            if (first != null)
            {
                var statusName = first.Status switch { STATUS_LOADING => "CARGANDO", STATUS_HAULING => "EN TRÁNSITO", STATUS_UNLOADING => "DESCARGANDO", STATUS_RETURNING => "REGRESANDO", _ => "IDLE" };
                _logHistoryService.AddLog(config.ServerConfigId, $"[RethinkBot] {first.EconomicNumber} → {statusName} | {first.Route.description}");
            }
        }

        private async Task MarkAllDeleted(RethinkBotConfig config, CancellationToken ct)
        {
            if (!_fleet.ContainsKey(config.ServerConfigId)) return;
            var baseUrl = $"https://{config.RethinkHost}:{config.RethinkPort}";

            foreach (var sim in _fleet[config.ServerConfigId])
            {
                var doc = JsonConvert.SerializeObject(new { VehicleId = sim.VehicleId, IsDeleted = true, Status = 0 });
                await ExecuteReqlInsert(baseUrl, doc, config.ServerConfigId, ct);
            }
        }

        private string BuildDocument(SimVehicle sim)
        {
            var loadDate = DateTime.UtcNow.AddMinutes(-new Random().Next(3, 20));
            var doc = new Dictionary<string, object?>
            {
                ["VehicleId"] = sim.VehicleId,
                ["VehicleEconomicNumber"] = sim.EconomicNumber,
                ["VehicleCompanyId"] = sim.CompanyId,
                ["VehicleCompanyName"] = sim.CompanyName,
                ["VehicleTypeId"] = sim.VehicleTypeId,
                ["VehicleTypeName"] = sim.VehicleTypeName,
                ["EmployeeId"] = sim.EmployeeId,
                ["EmployeeName"] = sim.EmployeeName,
                ["EmployeeCompanyId"] = sim.EmployeeCompanyId,
                ["EmployeeCompanyName"] = sim.EmployeeCompanyName,
                ["LoadPointId"] = sim.Route.loadPointId,
                ["LoadPointName"] = sim.Route.loadPointName,
                ["UnLoadPointId"] = sim.IsScooptram ? null : (object)sim.Route.unLoadPointId,
                ["UnLoadPointName"] = sim.IsScooptram ? null : sim.Route.unLoadPointName,
                ["PathId"] = sim.Route.haulagePathId,
                ["PathName"] = sim.Route.description,
                ["MaterialId"] = sim.Route.materialTypeId ?? 0,
                ["MaterialName"] = sim.Route.materialType ?? "MINERAL",
                ["LoadDate"] = new Dictionary<string, object> { ["$reql_type$"] = "TIME", ["epoch_time"] = ((DateTimeOffset)loadDate).ToUnixTimeSeconds(), ["timezone"] = "-06:00" },
                ["UnloadDate"] = null,
                ["LoadEmployeeCompanyId"] = sim.EmployeeCompanyId,
                ["LoadEmployeeCompanyName"] = sim.EmployeeCompanyName,
                ["LoadEmployeeId"] = sim.EmployeeId,
                ["LoadEmployeeName"] = sim.EmployeeName,
                ["LoadVehicleId"] = sim.VehicleId,
                ["LoadVehicleEconomicNumber"] = sim.EconomicNumber,
                ["LoadVehicleCompanyId"] = sim.CompanyId,
                ["LoadVehicleCompanyName"] = sim.CompanyName,
                ["LoadVehicleTypeId"] = sim.VehicleTypeId,
                ["LoadVehicleTypeName"] = sim.VehicleTypeName,
                ["Status"] = sim.Status,
                ["IsDeleted"] = false
            };
            return JsonConvert.SerializeObject(doc);
        }

        private async Task<bool> ExecuteReqlInsert(string baseUrl, string documentJson, int serverId, CancellationToken ct)
        {
            try
            {
                var dbAst = "[14,[\"SmartFlow\"]]";
                var tableAst = "[15,[" + dbAst + ",\"HaulageProcess\"]]";
                var globalOpts = "{\"binary_format\":\"raw\",\"time_format\":\"raw\",\"profile\":false}";
                var reql = "[1,[56,[" + tableAst + "," + documentJson + "]]," + globalOpts + "]";

                // Obtener conn_id
                var psi1 = new ProcessStartInfo
                {
                    FileName = "/usr/bin/curl",
                    Arguments = $"-sk -X POST {baseUrl}/ajax/reql/open-new-connection",
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                };
                string connId;
                using (var p = Process.Start(psi1))
                {
                    if (p == null) return false;
                    connId = (await p.StandardOutput.ReadToEndAsync(ct)).Trim().Trim('"');
                    await p.WaitForExitAsync(ct);
                }
                if (string.IsNullOrWhiteSpace(connId)) return false;

                // Escribir payload binario
                var tmpFile = $"/tmp/reql_{serverId}_{Thread.CurrentThread.ManagedThreadId}.bin";
                var queryBytes = Encoding.UTF8.GetBytes(reql);
                var payload = new byte[8 + queryBytes.Length];
                BitConverter.GetBytes(1L).CopyTo(payload, 0);
                queryBytes.CopyTo(payload, 8);
                File.WriteAllBytes(tmpFile, payload);

                // Enviar
                var psi2 = new ProcessStartInfo
                {
                    FileName = "/usr/bin/curl",
                    Arguments = $"-sk -X POST \"{baseUrl}/ajax/reql/?conn_id={connId}\" -H \"Content-Type: application/octet-stream\" --data-binary @{tmpFile}",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (var p = Process.Start(psi2))
                {
                    if (p == null) return false;
                    await p.StandardOutput.ReadToEndAsync(ct);
                    await p.WaitForExitAsync(ct);
                    try { File.Delete(tmpFile); } catch { }
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private async Task<int> GetCurrentWorkshift(ServerConfig server, TokenService tokenService, CancellationToken ct)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var resp = await client.GetAsync($"{host}/Catalog/GetAllWorkShifts", ct);
                if (!resp.IsSuccessStatusCode) return -1;
                var json = await resp.Content.ReadAsStringAsync(ct);
                var shifts = JsonConvert.DeserializeObject<List<ShiftDto>>(json);
                if (shifts == null || !shifts.Any()) return -1;

                var now = DateTime.UtcNow.AddHours(-6);
                var t = now.TimeOfDay;
                foreach (var s in shifts.Where(x => x.Enabled))
                {
                    var start = TimeSpan.Parse(s.StartTime);
                    var end = TimeSpan.Parse(s.EndTime);
                    if (start < end) { if (t >= start && t < end) return s.WorkShiftId; }
                    else { if (t >= start || t < end) return s.WorkShiftId; }
                }
                return shifts.First().WorkShiftId;
            }
            catch { return -1; }
        }

        private class SimVehicle
        {
            public int VehicleId { get; set; }
            public string EconomicNumber { get; set; } = "";
            public int CompanyId { get; set; }
            public string CompanyName { get; set; } = "";
            public int VehicleTypeId { get; set; }
            public string VehicleTypeName { get; set; } = "";
            public int EmployeeId { get; set; }
            public string EmployeeName { get; set; } = "";
            public int EmployeeCompanyId { get; set; }
            public string EmployeeCompanyName { get; set; } = "";
            public haulages_bot.Models.Route Route { get; set; } = null!;
            public int Status { get; set; }
            public bool IsScooptram { get; set; }
        }

        private class ShiftDto
        {
            public int WorkShiftId { get; set; }
            public string StartTime { get; set; } = "";
            public string EndTime { get; set; } = "";
            public bool Enabled { get; set; }
        }

        private class ApiVehicleDto
        {
            [JsonProperty("vehicleId")] public int VehicleId { get; set; }
            [JsonProperty("economicNumber")] public string? EconomicNumber { get; set; }
            [JsonProperty("companyId")] public int CompanyId { get; set; }
            [JsonProperty("vehicleTypeId")] public int VehicleTypeId { get; set; }
            public string? CompanyName => Company?.Name;
            public string? VehicleTypeName => VehicleType?.Name;
            [JsonProperty("company")] public ApiCompanyDto? Company { get; set; }
            [JsonProperty("vehicleType")] public ApiVehicleTypeDto? VehicleType { get; set; }
        }

        private class ApiCompanyDto
        {
            [JsonProperty("name")] public string? Name { get; set; }
        }

        private class ApiVehicleTypeDto
        {
            [JsonProperty("name")] public string? Name { get; set; }
        }
    }
}
