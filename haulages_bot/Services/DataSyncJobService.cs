using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;
using haulages_bot.Hubs;
using haulages_bot.Models;
using haulages_bot.Services;

public class DataSyncJobService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataSyncJobService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly Random _random;
    private readonly IHubContext<NotificationHub> _notificationHubContext;
    private readonly LogHistoryService _logHistoryService;

    // Diccionario de temporizadores activos indexados por ID de servidor
    private readonly Dictionary<int, Timer> _activeTimers = new Dictionary<int, Timer>();
    private readonly Dictionary<int, DateTime> _nextRunTimes = new Dictionary<int, DateTime>();
    private readonly HashSet<int> _runningServers = new HashSet<int>();
    private readonly object _timerLock = new object();

    public DateTime? GetNextRunTime(int serverId)
    {
        lock (_timerLock)
        {
            if (_nextRunTimes.TryGetValue(serverId, out var dt))
            {
                return dt;
            }
            return null;
        }
    }

    public DataSyncJobService(IServiceProvider serviceProvider, ILogger<DataSyncJobService> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory, IHubContext<NotificationHub> notificationHubContext, LogHistoryService logHistoryService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _random = new Random();
        _notificationHubContext = notificationHubContext;
        _logHistoryService = logHistoryService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando DataSyncJobService...");

        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<dbboot>();
            try
            {
                // Iniciar el bot para todos los servidores que tengan IsBotRunning activo
                var activeServers = await dbContext.ServerConfigs
                    .Where(s => s.IsActive && s.IsBotRunning)
                    .ToListAsync();

                foreach (var server in activeServers)
                {
                    _logger.LogInformation($"Iniciando bot automático para el servidor '{server.Name}'...");
                    _logHistoryService.AddLog(server.Id, $"Iniciando bot automático para el servidor '{server.Name}'...");
                    StartBotInternal(server.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al arrancar bots automáticos: {ex.Message}");
            }
        }
    }

    public void StartBot(int serverId)
    {
        lock (_timerLock)
        {
            _logger.LogInformation($"Petición manual para encender el bot del servidor ID {serverId}");
            _logHistoryService.AddLog(serverId, "Bot automático iniciado manualmente.");
            StartBotInternal(serverId);
        }
    }

    public void StopBot(int serverId)
    {
        lock (_timerLock)
        {
            _logger.LogInformation($"Petición manual para apagar el bot del servidor ID {serverId}");
            _logHistoryService.AddLog(serverId, "Bot automático detenido manualmente.");
            if (_activeTimers.TryGetValue(serverId, out var timer))
            {
                timer.Dispose();
                _activeTimers.Remove(serverId);
            }
            _nextRunTimes.Remove(serverId);
        }
    }

    private void StartBotInternal(int serverId)
    {
        if (_activeTimers.TryGetValue(serverId, out var existingTimer))
        {
            existingTimer.Dispose();
            _activeTimers.Remove(serverId);
        }

        // Crear un temporizador con ejecución inmediata
        var timer = new Timer(SyncData, serverId, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        _activeTimers[serverId] = timer;
    }

    private void ConfigureTimer(int serverId)
    {
        lock (_timerLock)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<dbboot>();
                    var server = dbContext.ServerConfigs.Find(serverId);
                    if (server == null || !server.IsBotRunning || !server.IsActive)
                    {
                        if (_activeTimers.TryGetValue(serverId, out var timer))
                        {
                            timer.Dispose();
                            _activeTimers.Remove(serverId);
                        }
                        return;
                    }

                    var dataConfig = dbContext.DataConfigurationLocal
                        .OrderByDescending(dc => dc.Id)
                        .FirstOrDefault(dc => dc.ServerConfigId == serverId);

                    if (dataConfig == null)
                    {
                        _logger.LogWarning($"Configuración de bot no encontrada para el servidor {server.Name}. Usando tiempo por defecto de 5 min.");
                        RescheduleTimer(serverId, 5);
                        return;
                    }

                    var times = JsonConvert.DeserializeObject<List<int>>(dataConfig.Time) ?? new List<int> { 5, 15 };
                    int minTime = times.FirstOrDefault();
                    int maxTime = times.LastOrDefault();
                    int nextTime = _random.Next(minTime, maxTime + 1);

                    RescheduleTimer(serverId, nextTime);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en ConfigureTimer para servidor ID {serverId}: {ex.Message}. Rescheduling with 5 min default.");
                RescheduleTimer(serverId, 5);
            }
        }
    }

    private void RescheduleTimer(int serverId, int minutes)
    {
        var nextRunTime = DateTime.Now.AddMinutes(minutes);
        lock (_timerLock)
        {
            _nextRunTimes[serverId] = nextRunTime;
        }
        NotifyFrontend(serverId, nextRunTime, minutes);

        if (_activeTimers.TryGetValue(serverId, out var timer))
        {
            timer.Change(TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
        }
    }

    private void NotifyFrontend(int serverId, DateTime nextRunTime, int intervalMinutes)
    {
        try
        {
            _logger.LogInformation($"Bot ID {serverId} configurado para correr en {intervalMinutes} minutos.");
            _logHistoryService.AddLog(serverId, $"Bot configurado para correr en {intervalMinutes} minutos.");
            _notificationHubContext.Clients.All.SendAsync(
                "UpdateTimerInfo",
                new { ServerId = serverId, NextRunTime = nextRunTime.ToString("HH:mm:ss"), IntervalMinutes = intervalMinutes }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al notificar al frontend: {ex.Message}");
        }
    }

    private async void SyncData(object state)
    {
        int serverId = (int)state;

        lock (_timerLock)
        {
            if (_runningServers.Contains(serverId))
            {
                _logger.LogInformation($"La sincronización para el servidor ID {serverId} ya está en curso. Evitando duplicado.");
                return;
            }
            _runningServers.Add(serverId);
        }

        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<dbboot>();
                var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
                var server = await context.ServerConfigs.FindAsync(serverId);
                if (server == null || !server.IsBotRunning || !server.IsActive)
                {
                    return;
                }

                _logger.LogInformation($"Ejecutando bot automático para '{server.Name}'...");
                await ExecuteBotRegistrationForServer(context, server, tokenService);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al ejecutar bot automático para el servidor ID {serverId}: {ex.Message}");
            _logHistoryService.AddLog(serverId, $"Error en bot automático: {ex.Message}", true);
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = $"Error bot automático: {ex.Message}" });
        }
        finally
        {
            lock (_timerLock)
            {
                _runningServers.Remove(serverId);
            }
            ConfigureTimer(serverId);
        }
    }

    private async Task ExecuteBotRegistrationForServer(dbboot context, ServerConfig server, TokenService tokenService)
    {
        var configService = new BootConfigurationService(context);
        var dataConfig = await configService.GetDataConfiguration(server.Id);
        if (dataConfig == null)
        {
            var errMsg = $"No se pudo obtener la configuración de datos para el servidor '{server.Name}'. Configura el bot primero.";
            _logger.LogError(errMsg);
            _logHistoryService.AddLog(server.Id, errMsg, true);
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = server.Id, Error = true, Message = errMsg });
            return;
        }

        configService.SetTonnageVariation(dataConfig.TonnageVariation);
        configService.SetTime(dataConfig.Time);
        configService.SetSelectedRoutes(dataConfig.SelectedRoutes);
        configService.SetSelectedEmployees(dataConfig.SelectedEmployees);
        configService.SetSelectedVehicles(dataConfig.SelectedVehicles);

        var randomRoute = configService.GetRandomRoute();
        var randomEmployee = configService.GetRandomEmployee();
        var randomVehicle = configService.GetRandomVehicle();

        var employeeDetail = await context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == randomEmployee && e.ServerConfigId == server.Id);
        var routeDetail = await context.Routes.FirstOrDefaultAsync(r => r.haulagePathId == randomRoute && r.ServerConfigId == server.Id);
        var vehicleDetail = await context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == randomVehicle && v.ServerConfigId == server.Id);

        if (employeeDetail == null || routeDetail == null || vehicleDetail == null)
        {
            var warningMsg = $"Datos de catálogo incompletos para bot en '{server.Name}' (Empleado: {randomEmployee}, Ruta: {randomRoute}, Vehículo: {randomVehicle}). Sincroniza catálogos y activa elementos en la configuración.";
            _logger.LogWarning(warningMsg);
            _logHistoryService.AddLog(server.Id, warningMsg, true);
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = server.Id, Error = true, Message = warningMsg });
            return;
        }

        var vehicleCapacity = vehicleDetail.LoadingCapacity;
        var tonnageWeight = configService.GetRandomTonnageWeight(vehicleCapacity);

        // Resolver materialTypeId en función del tipo de material de la ruta
        int resolvedMaterialTypeId = await ResolveMaterialTypeId(context, server.Id, routeDetail.selectedMaterialType, routeDetail.materialTypeId);

        var acarreo = new
        {
            VehicleId = randomVehicle,
            EmployeeId = randomEmployee,
            PathId = randomRoute,
            Weight = tonnageWeight,
            Date = DateTime.UtcNow,
            Comments = "Auto-Registro SF Bot C#",
            materialTypeId = resolvedMaterialTypeId,
        };

        var jsonContent = JsonConvert.SerializeObject(acarreo);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var token = await tokenService.GetTokenAsync(server.Id);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
        var apiEndpoint = $"{host}/service/haulages/api/v2/manualhaulages/manual/add";

        var response = await _httpClient.PostAsync(apiEndpoint, content);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation($"Acarreo enviado correctamente a '{server.Name}': {tonnageWeight}t");

            context.Haulages.Add(new Haulage
            {
                VehicleId = acarreo.VehicleId,
                EmployeeId = acarreo.EmployeeId,
                PathId = acarreo.PathId,
                Weight = acarreo.Weight,
                Comments = acarreo.Comments,
                materialTypeId = acarreo.materialTypeId,
                ServerConfigId = server.Id,
                Dateofcarries = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                VehicleEconomicNumber = vehicleDetail.EconomicNumber,
                EmployeeFullName = employeeDetail.FullName,
                RouteDescription = routeDetail.description
            });

            await context.SaveChangesAsync();
            _logHistoryService.AddLog(server.Id, $"Acarreo registrado correctamente: {tonnageWeight}t");
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = server.Id, Message = $"Acarreo registrado correctamente en {server.Name}: {tonnageWeight}t" });
        }
        else
        {
            var errorMessage = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Error al enviar acarreo a '{server.Name}': {response.StatusCode} - {errorMessage}");
            _logHistoryService.AddLog(server.Id, $"Error al registrar acarreo: {errorMessage}", true);
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = server.Id, Error = true, Message = $"Error: {errorMessage}" });
        }
    }

    /// <summary>
    /// Obtiene la hora actual ajustada según el offset de zona horaria del servidor.
    /// </summary>
    private DateTime GetAdjustedNow(ServerConfig server)
    {
        int offset = server.TimezoneOffsetHours ?? _configuration.GetValue<int>("TimezoneOffsetHours", 0);
        return DateTime.UtcNow.AddHours(offset);
    }

    /// <summary>
    /// Resuelve el materialTypeId correcto para un acarreo en función del tipo de material de la ruta.
    /// 1=Mineral, 2=Estéril/Desmonte, 3=Ambos (aleatorio).
    /// </summary>
    private async Task<int> ResolveMaterialTypeId(dbboot context, int serverId, int selectedMaterialType, int? routeMaterialTypeId)
    {
        try
        {
            var materials = await context.Materials
                .Where(m => m.ServerConfigId == serverId)
                .ToListAsync();

            if (!materials.Any())
                return 1; // Fallback si no hay materiales sincronizados

            // Encontrar materiales por nombre en la DB (MINERAL, DESMONTE, TEPETATE, TAILS, etc.)
            var mineralMat = materials.FirstOrDefault(m =>
                m.name.ToUpperInvariant().Contains("MINERAL"));
            var desmonteMat = materials.FirstOrDefault(m =>
                m.name.ToUpperInvariant().Contains("DESMONTE") ||
                m.name.ToUpperInvariant().Contains("ESTERIL") ||
                m.name.ToUpperInvariant().Contains("EST\u00c9RIL"));

            // IDs reales de mineral y desmonte
            int mineralId = mineralMat?.materialTypeId ?? materials.First().materialTypeId;
            int desmonteId = desmonteMat?.materialTypeId ?? mineralId;

            int specificEsterilId = desmonteId;
            if (routeMaterialTypeId.HasValue && routeMaterialTypeId.Value != 0 && routeMaterialTypeId.Value != mineralId)
            {
                specificEsterilId = routeMaterialTypeId.Value;
            }

            switch (selectedMaterialType)
            {
                case 1:
                    return specificEsterilId;
                case 2:
                    return _random.Next(2) == 0 ? mineralId : specificEsterilId;
                case 0:
                default:
                    return mineralId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error al resolver materialTypeId para servidor {serverId}: {ex.Message}. Usando ID 1 por defecto.");
            return 1;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deteniendo DataSyncJobService...");
        lock (_timerLock)
        {
            foreach (var timer in _activeTimers.Values)
            {
                timer.Dispose();
            }
            _activeTimers.Clear();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_timerLock)
        {
            foreach (var timer in _activeTimers.Values)
            {
                timer.Dispose();
            }
            _activeTimers.Clear();
        }
        _httpClient?.Dispose();
    }
}
