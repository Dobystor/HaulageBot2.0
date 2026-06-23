using Microsoft.AspNetCore.Mvc;
using haulages_bot.Services;
using haulages_bot.Data;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Models;
using haulages_bot.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace haulages_bot.Controllers
{
    [Route("api/sync")]
    [ApiController]
    public class DataSyncController : ControllerBase
    {
        private readonly DataSyncManualService _dataSyncManualService;
        private readonly DataSyncJobManual _dataSyncJobManual;
        private readonly dbboot _dbContext;
        private readonly DataSyncJobService _dataSyncJobService;
        private readonly TokenService _tokenService;
        private readonly HttpClient _httpClient;
        private readonly IHubContext<NotificationHub> _notificationHubContext;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly LogHistoryService _logHistoryService;

        public DataSyncController(
            DataSyncManualService dataSyncManualService, 
            DataSyncJobManual dataSyncJobManual, 
            dbboot dbContext, 
            DataSyncJobService dataSyncJobService,
            TokenService tokenService,
            IHttpClientFactory httpClientFactory,
            IHubContext<NotificationHub> notificationHubContext,
            IServiceScopeFactory serviceScopeFactory,
            LogHistoryService logHistoryService)
        {
            _dataSyncManualService = dataSyncManualService;
            _dataSyncJobManual = dataSyncJobManual;
            _dbContext = dbContext;
            _dataSyncJobService = dataSyncJobService;
            _tokenService = tokenService;
            _httpClient = httpClientFactory.CreateClient();
            _notificationHubContext = notificationHubContext;
            _serviceScopeFactory = serviceScopeFactory;
            _logHistoryService = logHistoryService;
        }

        [HttpPost("toggle")]
        public async Task<IActionResult> ToggleSync([FromBody] bool enable, [FromQuery] int serverId)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return NotFound("Servidor no encontrado");

            server.IsBotRunning = enable;
            _dbContext.ServerConfigs.Update(server);
            await _dbContext.SaveChangesAsync();

            if (enable)
            {
                _dataSyncJobService.StartBot(serverId);
            }
            else
            {
                _dataSyncJobService.StopBot(serverId);
            }

            return Ok(new { ServerId = serverId, BotRunning = server.IsBotRunning });
        }

        [HttpPost("togglelocal")]
        public async Task<IActionResult> ToggleSyncLocal([FromBody] bool enable, [FromQuery] int serverId)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return NotFound("Servidor no encontrado");

            server.IsSyncEnabledLocal = enable;
            _dbContext.ServerConfigs.Update(server);
            await _dbContext.SaveChangesAsync();

            return Ok(new { ServerId = serverId, SyncEnabledLocal = server.IsSyncEnabledLocal });
        }

        [HttpPost("manuallocal")]
        public async Task<IActionResult> SyncData([FromQuery] int serverId)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            var serverName = server?.Name ?? $"Servidor {serverId}";
            try
            {
                _logHistoryService.AddLog(serverId, $"Iniciando sincronización local de catálogos para '{serverName}'...");
                await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Message = "Iniciando sincronización local de catálogos..." });

                await _dataSyncManualService.SyncData(serverId);

                _logHistoryService.AddLog(serverId, "Sincronización local de catálogos completada exitosamente.");
                await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Message = "Sincronización local de catálogos completada exitosamente." });

                return Ok(new { success = true, message = "Sincronización completada exitosamente." });
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error en la sincronización de catálogos: {ex.Message}";
                _logHistoryService.AddLog(serverId, errorMsg, true);
                await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = errorMsg });
                return BadRequest(new { success = false, message = errorMsg });
            }
        }

        [HttpPost("botmanual")]
        public async Task<IActionResult> InsertManualAutonomo([FromQuery] int serverId)
        {
            try
            {
                await _dataSyncJobManual.SyncData(serverId);
                return Ok(new { success = true, message = "Registro de acarreo manual completado exitosamente." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Error en el registro manual: {ex.Message}" });
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetServerStatus([FromQuery] int serverId)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return NotFound("Servidor no encontrado");

            return Ok(new
            {
                serverId = server.Id,
                name = server.Name,
                isBotRunning = server.IsBotRunning,
                isSyncEnabledLocal = server.IsSyncEnabledLocal,
                tokenExpiry = server.TokenExpiry?.ToString("yyyy-MM-dd HH:mm:ss") ?? "No autenticado"
            });
        }

        [HttpGet("nextRunTime")]
        public IActionResult GetNextRunTime([FromQuery] int serverId)
        {
            var nextTime = _dataSyncJobService.GetNextRunTime(serverId);
            if (nextTime.HasValue)
            {
                return Ok(new { nextRunTime = nextTime.Value.ToString("yyyy-MM-ddTHH:mm:ss") });
            }
            return NotFound("No hay ejecución programada o el bot está apagado para este servidor.");
        }

        [HttpGet("logHistory")]
        public IActionResult GetLogHistory([FromQuery] int serverId)
        {
            var logs = _logHistoryService.GetLogs(serverId);
            return Ok(logs);
        }

        [HttpPost("clearLogHistory")]
        public IActionResult ClearLogHistory([FromQuery] int serverId)
        {
            _logHistoryService.ClearLogs(serverId);
            return Ok(new { success = true });
        }

        [HttpPost("bulk-generate")]
        public async Task<IActionResult> BulkGenerate([FromBody] BulkGenerateRequest request)
        {
            if (request == null) return BadRequest("Solicitud inválida.");
            if (request.StartDate >= request.EndDate) return BadRequest("La fecha de inicio debe ser anterior a la fecha de fin.");
            if (request.TotalTonnage <= 0) return BadRequest("El tonelaje total debe ser mayor que cero.");

            var server = await _dbContext.ServerConfigs.FindAsync(request.ServerId);
            if (server == null) return NotFound("Servidor no encontrado.");

            var dataConfig = await _dbContext.DataConfigurationLocal
                .FirstOrDefaultAsync(dc => dc.ServerConfigId == request.ServerId);
            if (dataConfig == null) return BadRequest("Configuración del bot no encontrada para este servidor.");

            var selectedRoutes = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new List<int>();
            var selectedEmployees = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedEmployees) ?? new List<int>();
            var selectedVehicles = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedVehicles) ?? new List<int>();

            if (!selectedRoutes.Any() || !selectedEmployees.Any() || !selectedVehicles.Any())
            {
                return BadRequest("Debes configurar al menos una ruta, un operador y un vehículo activos para este servidor en la configuración del bot.");
            }

            var tonnageRange = JsonConvert.DeserializeObject<List<int>>(dataConfig.TonnageVariation) ?? new List<int> { 10, 20 };
            decimal tonnageMin = tonnageRange.FirstOrDefault();
            decimal tonnageMax = tonnageRange.LastOrDefault();
            if (tonnageMin <= 0) tonnageMin = 80; // default 80%
            if (tonnageMax <= tonnageMin) tonnageMax = tonnageMin + 20; // default 100%

            var token = await _tokenService.GetTokenAsync(request.ServerId);
            if (string.IsNullOrEmpty(token)) return Unauthorized("No se pudo obtener el token de autorización.");

            // Pre-cargar rutas y materiales para resolver materialTypeId de forma dinámica y eficiente en memoria
            var routes = await _dbContext.Routes
                .Where(r => r.ServerConfigId == request.ServerId && selectedRoutes.Contains(r.haulagePathId))
                .ToDictionaryAsync(r => r.haulagePathId, r => r);
            if (!routes.Any()) return BadRequest("No se encontraron las rutas activas seleccionadas en la base de datos.");

            var materials = await _dbContext.Materials
                .Where(m => m.ServerConfigId == request.ServerId)
                .ToListAsync();

            var mineralMat = materials.FirstOrDefault(m =>
                m.name.ToUpperInvariant().Contains("MINERAL"));
            var desmonteMat = materials.FirstOrDefault(m =>
                m.name.ToUpperInvariant().Contains("DESMONTE") ||
                m.name.ToUpperInvariant().Contains("ESTERIL") ||
                m.name.ToUpperInvariant().Contains("ESTÉRIL"));

            int mineralId = mineralMat?.materialTypeId ?? (materials.Any() ? materials.First().materialTypeId : 1);
            int desmonteId = desmonteMat?.materialTypeId ?? mineralId;

            // Pre-calcular la lista de acarreos (pesos y vehículos) para no hacerlo en el thread asíncrono
            var vehicles = await _dbContext.Vehicles
                .Where(v => v.ServerConfigId == request.ServerId && selectedVehicles.Contains(v.VehicleId))
                .ToListAsync();
            if (!vehicles.Any()) return BadRequest("No se encontraron los vehículos activos seleccionados en la base de datos.");

            // Pre-cargar empleados para snapshot de nombres
            var employees = await _dbContext.Employees
                .Where(e => e.ServerConfigId == request.ServerId && selectedEmployees.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e);

            var random = new Random();
            var weightsAndVehicles = new List<(decimal Weight, Vehicle Vehicle)>();
            decimal remainingTonnage = request.TotalTonnage;

            while (remainingTonnage > 0)
            {
                var vehicle = vehicles[random.Next(vehicles.Count)];
                decimal capacity = vehicle.LoadingCapacity > 0 ? vehicle.LoadingCapacity : 30m;

                decimal wMin = capacity * (tonnageMin / 100m);
                decimal wMax = capacity * (tonnageMax / 100m);
                if (wMin <= 0) wMin = capacity * 0.8m;
                if (wMax <= wMin) wMax = wMin + (capacity * 0.2m);

                decimal w = (decimal)(random.NextDouble() * (double)(wMax - wMin) + (double)wMin);
                w = Math.Round(w, 2);

                if (remainingTonnage <= wMax)
                {
                    if (remainingTonnage > 0)
                    {
                        weightsAndVehicles.Add((Math.Round(remainingTonnage, 2), vehicle));
                        remainingTonnage = 0;
                    }
                }
                else
                {
                    weightsAndVehicles.Add((w, vehicle));
                    remainingTonnage -= w;
                }
            }

            int n = weightsAndVehicles.Count;
            var dates = new List<DateTime>();
            double totalSeconds = (request.EndDate - request.StartDate).TotalSeconds;

            for (int i = 0; i < n; i++)
            {
                double randDouble = random.NextDouble();
                dates.Add(request.StartDate.AddSeconds(randDouble * totalSeconds));
            }
            dates.Sort();

            _ = Task.Run(async () =>
            {
                int processed = 0;
                int failed = 0;
                int completedCount = 0;
                try
                {
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<dbboot>();
                        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
                        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                        var client = httpClientFactory.CreateClient();
                        var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                        var apiEndpoint = $"{host}/service/haulages/api/v2/manualhaulages/manual/add";

                        // Obtener token actual una sola vez (si ha expirado se refresca automáticamente)
                        var currentToken = await tokenService.GetTokenAsync(request.ServerId);
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", currentToken);

                        using var semaphore = new SemaphoreSlim(10);
                        var tasks = new List<Task>();
                        var successfulHaulages = new System.Collections.Concurrent.ConcurrentBag<Haulage>();
                        object lockObj = new object();

                        for (int i = 0; i < n; i++)
                        {
                            int index = i;
                            await semaphore.WaitAsync();
                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    int routeId;
                                    int employeeId;
                                    lock (lockObj)
                                    {
                                        routeId = selectedRoutes[random.Next(selectedRoutes.Count)];
                                        employeeId = selectedEmployees[random.Next(selectedEmployees.Count)];
                                    }
                                    
                                    var item = weightsAndVehicles[index];
                                    int vehicleId = item.Vehicle.VehicleId;
                                    decimal weight = item.Weight;
                                    DateTime dateOfCarry = dates[index];

                                    // Resolver materialTypeId en base al tipo de material de la ruta seleccionada
                                    int resolvedMaterialTypeId = mineralId;
                                    if (routes.TryGetValue(routeId, out var routeDetail))
                                    {
                                        int specificEsterilId = desmonteId;
                                        if (routeDetail.materialTypeId.HasValue && routeDetail.materialTypeId.Value != 0 && routeDetail.materialTypeId.Value != mineralId)
                                        {
                                            specificEsterilId = routeDetail.materialTypeId.Value;
                                        }

                                        switch (routeDetail.selectedMaterialType)
                                        {
                                            case 1:
                                                resolvedMaterialTypeId = specificEsterilId;
                                                break;
                                            case 2:
                                                bool pickMineral;
                                                lock (lockObj)
                                                {
                                                    pickMineral = random.Next(2) == 0;
                                                }
                                                resolvedMaterialTypeId = pickMineral ? mineralId : specificEsterilId;
                                                break;
                                            case 0:
                                            default:
                                                resolvedMaterialTypeId = mineralId;
                                                break;
                                        }
                                    }

                                    var acarreo = new
                                    {
                                        VehicleId = vehicleId,
                                        EmployeeId = employeeId,
                                        PathId = routeId,
                                        Weight = weight,
                                        Date = dateOfCarry,
                                        Comments = "Importación Masiva Rango Fechas",
                                        materialTypeId = resolvedMaterialTypeId
                                    };

                                    var jsonContent = JsonConvert.SerializeObject(acarreo);
                                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                                    var response = await client.PostAsync(apiEndpoint, content);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        lock (lockObj)
                                        {
                                            processed++;
                                        }
                                        
                                        successfulHaulages.Add(new Haulage
                                        {
                                            VehicleId = vehicleId,
                                            EmployeeId = employeeId,
                                            PathId = routeId,
                                            Weight = weight,
                                            Comments = "Importación Masiva Rango Fechas",
                                            materialTypeId = resolvedMaterialTypeId,
                                            ServerConfigId = request.ServerId,
                                            Dateofcarries = dateOfCarry.ToString("yyyy-MM-dd HH:mm:ss"),
                                            VehicleEconomicNumber = item.Vehicle.EconomicNumber,
                                            EmployeeFullName = employees.TryGetValue(employeeId, out var emp) ? emp.FullName : null,
                                            RouteDescription = routes.TryGetValue(routeId, out var rt) ? rt.description : null
                                        });
                                    }
                                    else
                                    {
                                        lock (lockObj)
                                        {
                                            failed++;
                                        }
                                        var errorResponse = await response.Content.ReadAsStringAsync();
                                        Console.WriteLine($"Error al registrar acarreo masivo {index}: Código {response.StatusCode} - {errorResponse}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    lock (lockObj)
                                    {
                                        failed++;
                                    }
                                    Console.WriteLine($"Error en importación masiva elemento {index}: {ex.Message}");
                                }
                                finally
                                {
                                    int currentCompleted;
                                    int currentProcessed;
                                    int currentFailed;
                                    lock (lockObj)
                                    {
                                        completedCount++;
                                        currentCompleted = completedCount;
                                        currentProcessed = processed;
                                        currentFailed = failed;
                                    }
                                    
                                    if (currentCompleted % 10 == 0 || currentCompleted == n)
                                    {
                                        var progMsg = $"Progreso importación masiva: {currentCompleted} de {n} procesados. (Exitosos: {currentProcessed}, Fallidos: {currentFailed})";
                                        _logHistoryService.AddLog(request.ServerId, progMsg);
                                        await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new
                                        {
                                            ServerId = request.ServerId,
                                            Message = progMsg
                                        });
                                    }
                                    semaphore.Release();
                                }
                            }));
                        }

                        await Task.WhenAll(tasks);

                        // Guardar todos los acarreos exitosos en una sola transacción
                        if (!successfulHaulages.IsEmpty)
                        {
                            foreach (var Mathaul in successfulHaulages)
                            {
                                db.Haulages.Add(Mathaul);
                            }
                            await db.SaveChangesAsync();
                        }

                        var finMsg = $"Importación masiva finalizada. Total: {n}, Exitosos: {processed}, Fallidos: {failed}";
                        _logHistoryService.AddLog(request.ServerId, finMsg);
                        await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new
                        {
                            ServerId = request.ServerId,
                            Message = finMsg
                        });
                    }
                }
                catch (Exception ex)
                {
                    var errMsg = $"Error en la tarea de generación masiva: {ex.Message}";
                    _logHistoryService.AddLog(request.ServerId, errMsg, true);
                    await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new
                    {
                        ServerId = request.ServerId,
                        Error = true,
                        Message = errMsg
                    });
                }
            });

            return Ok(new { success = true, message = $"Proceso de generación masiva iniciado. Se generarán {n} acarreos en segundo plano." });
        }
    }

    public class BulkGenerateRequest
    {
        public int ServerId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalTonnage { get; set; }
    }
}