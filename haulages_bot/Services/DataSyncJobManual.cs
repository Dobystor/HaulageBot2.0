using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System;
using haulages_bot.Controllers;
using haulages_bot.Data;
using haulages_bot.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using haulages_bot.Hubs;
using System.Linq;
using System.Threading.Tasks;

namespace haulages_bot.Services
{
    public class DataSyncJobManual : Controller
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataSyncJobManual> _logger;
        private readonly HttpClient _httpClient;
        private readonly Random _random;
        private readonly TokenService _tokenService;
        private readonly IHubContext<NotificationHub> _notificationHubContext;
        private readonly LogHistoryService _logHistoryService;
               
        public DataSyncJobManual(IServiceProvider serviceProvider, ILogger<DataSyncJobManual> logger, TokenService tokenService, IHttpClientFactory httpClientFactory, IHubContext<NotificationHub> notificationHubContext, LogHistoryService logHistoryService)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _tokenService = tokenService;
            _httpClient = httpClientFactory.CreateClient();
            _random = new Random();
            _notificationHubContext = notificationHubContext;
            _logHistoryService = logHistoryService;
        }

        public async Task SyncData(int serverId)
        {
            _logger.LogInformation($"Iniciando registro manual de acarreo para el servidor ID {serverId}...");

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<dbboot>();
                var server = await context.ServerConfigs.FindAsync(serverId);
                if (server == null)
                {
                    _logger.LogError($"Server config {serverId} not found for manual bot registration.");
                    throw new Exception("Servidor no encontrado.");
                }

                try
                {
                    var configService = new BootConfigurationService(context);
                    var dataConfig = await configService.GetDataConfiguration(serverId);
                    if (dataConfig == null)
                    {
                        _logger.LogError($"No se pudo obtener la configuración de datos para el servidor {server.Name}");
                        throw new Exception("Configuración de bot incompleta.");
                    }

                    configService.SetTonnageVariation(dataConfig.TonnageVariation);
                    configService.SetTime(dataConfig.Time);
                    configService.SetSelectedRoutes(dataConfig.SelectedRoutes);
                    configService.SetSelectedEmployees(dataConfig.SelectedEmployees);
                    configService.SetSelectedVehicles(dataConfig.SelectedVehicles);

                    var randomRoute = configService.GetRandomRoute();
                    var randomEmployee = configService.GetRandomEmployee();
                    var randomVehicle = configService.GetRandomVehicle();

                    var employeeDetail = await context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == randomEmployee && e.ServerConfigId == serverId);
                    var routeDetail = await context.Routes.FirstOrDefaultAsync(r => r.haulagePathId == randomRoute && r.ServerConfigId == serverId);
                    var vehicleDetail = await context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == randomVehicle && v.ServerConfigId == serverId);

                    if (employeeDetail == null || routeDetail == null || vehicleDetail == null)
                    {
                        _logger.LogWarning($"Catálogo incompleto para el bot del servidor {server.Name}.");
                        throw new Exception("Catálogo incompleto para este bot.");
                    }

                    var vehicleCapacity = vehicleDetail.LoadingCapacity;
                    var tonnageWeight = configService.GetRandomTonnageWeight(vehicleCapacity);

                    // Resolver materialTypeId en función del tipo de material de la ruta
                    int resolvedMaterialTypeId = await ResolveMaterialTypeId(context, serverId, routeDetail.selectedMaterialType);

                    var acarreo = new
                    {
                        VehicleId = randomVehicle,
                        EmployeeId = randomEmployee,
                        PathId = randomRoute,
                        Weight = tonnageWeight,
                        Date = DateTime.Now,
                        Comments = "Registro Manual SF Bot C#",
                        materialTypeId = resolvedMaterialTypeId,
                    };

                    var jsonContent = JsonConvert.SerializeObject(acarreo);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var token = await _tokenService.GetTokenAsync(serverId);
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                    var apiEndpoint = $"{host}/service/haulages/api/v2/manualhaulages/manual/add";

                    var response = await _httpClient.PostAsync(apiEndpoint, content);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"Datos enviados correctamente a la API del servidor {server.Name}");

                        context.Haulages.Add(new Haulage
                        {
                            VehicleId = acarreo.VehicleId,
                            EmployeeId = acarreo.EmployeeId,
                            PathId = acarreo.PathId,
                            Weight = acarreo.Weight,
                            Comments = acarreo.Comments,
                            materialTypeId = acarreo.materialTypeId,
                            ServerConfigId = serverId,
                            Dateofcarries = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        });

                        await context.SaveChangesAsync();
                        _logHistoryService.AddLog(serverId, $"Registro manual exitoso: {tonnageWeight}t");
                        await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Message = $"Registro manual exitoso en {server.Name}: {tonnageWeight}t" });
                    }
                    else
                    {
                        var errorMessage = await response.Content.ReadAsStringAsync();
                        _logger.LogError($"Error al enviar datos manuales a la API en {server.Name}: {response.StatusCode} - {errorMessage}");
                        _logHistoryService.AddLog(serverId, $"Error en registro manual: {errorMessage}", true);
                        await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = $"Error: {errorMessage}" });
                        throw new Exception($"Error de servidor Smartflow: {errorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Excepción al registrar acarreo manual en {server.Name}: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Resuelve el materialTypeId correcto para un acarreo en función del tipo de material de la ruta.
        /// 1=Mineral, 2=Estéril/Desmonte, 3=Ambos (aleatorio).
        /// </summary>
        private async Task<int> ResolveMaterialTypeId(dbboot context, int serverId, int selectedMaterialType)
        {
            try
            {
                var materials = await context.Materials
                    .Where(m => m.ServerConfigId == serverId)
                    .ToListAsync();

                if (!materials.Any())
                    return 1;

                var mineralMats = materials.Where(m =>
                    m.name.ToUpperInvariant().Contains("MINERAL")).ToList();
                var esterilMats = materials.Where(m =>
                    m.name.ToUpperInvariant().Contains("DESMONTE") ||
                    m.name.ToUpperInvariant().Contains("ESTERIL") ||
                    m.name.ToUpperInvariant().Contains("ESTÉRIL")).ToList();

                if (!mineralMats.Any() && !esterilMats.Any())
                    return materials.First().materialTypeId;

                int resolvedType = selectedMaterialType;

                if (resolvedType == 3)
                {
                    resolvedType = _random.Next(2) == 0 ? 1 : 2;
                }

                if (resolvedType == 2)
                {
                    if (esterilMats.Any())
                        return esterilMats[_random.Next(esterilMats.Count)].materialTypeId;
                    if (mineralMats.Any())
                        return mineralMats[_random.Next(mineralMats.Count)].materialTypeId;
                }

                if (mineralMats.Any())
                    return mineralMats[_random.Next(mineralMats.Count)].materialTypeId;

                return materials.First().materialTypeId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error al resolver materialTypeId para servidor {serverId}: {ex.Message}. Usando ID 1 por defecto.");
                return 1;
            }
        }
    }
}
