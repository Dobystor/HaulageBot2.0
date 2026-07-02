using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using haulages_bot.Data;
using haulages_bot.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace haulages_bot.Services
{
    /// <summary>
    /// Bot que actualiza inventarios de mineral en SmartFlow.
    /// Toma sitios de carga de rutas de mineral configuradas y actualiza tonelaje cada turno.
    /// </summary>
    public class InventoryBotService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InventoryBotService> _logger;
        private readonly LogHistoryService _logHistoryService;
        private readonly IHttpClientFactory _httpClientFactory;
        private int _lastWorkshiftId = -1;

        public InventoryBotService(
            IServiceScopeFactory scopeFactory,
            ILogger<InventoryBotService> logger,
            LogHistoryService logHistoryService,
            IHttpClientFactory httpClientFactory)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _logHistoryService = logHistoryService;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(15000, stoppingToken); // Esperar inicio

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessInventory(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[InventoryBot] Error en ciclo principal");
                }

                await Task.Delay(60000, stoppingToken); // Revisar cada 60 segundos
            }
        }

        private async Task ProcessInventory(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<dbboot>();
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

            var config = await db.InventoryBotConfigs
                .FirstOrDefaultAsync(c => c.IsEnabled, ct);

            if (config == null) return;

            var server = await db.ServerConfigs.FindAsync(config.ServerConfigId);
            if (server == null) return;

            // Obtener turno actual
            var currentWorkshiftId = await GetCurrentWorkshift(server, tokenService, ct);
            if (currentWorkshiftId <= 0) return;

            // Solo actualizar si cambió el turno
            if (currentWorkshiftId == _lastWorkshiftId) return;
            _lastWorkshiftId = currentWorkshiftId;

            _logHistoryService.AddLog(config.ServerConfigId, $"[InventoryBot] Cambio de turno detectado (Turno {currentWorkshiftId}). Actualizando inventarios...");

            // Obtener sitios de carga de rutas de mineral configuradas
            var dataConfig = await db.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == config.ServerConfigId)
                .OrderByDescending(dc => dc.Id)
                .FirstOrDefaultAsync(ct);

            if (dataConfig == null) return;

            var selectedRouteIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new List<int>();

            // Rutas de mineral (selectedMaterialType = 0) que están activas
            var mineralRoutes = await db.Routes
                .Where(r => r.ServerConfigId == config.ServerConfigId
                    && selectedRouteIds.Contains(r.haulagePathId)
                    && r.selectedMaterialType == 0
                    && r.isEnabled)
                .ToListAsync(ct);

            if (!mineralRoutes.Any())
            {
                _logHistoryService.AddLog(config.ServerConfigId, "[InventoryBot] No hay rutas de mineral configuradas.", true);
                return;
            }

            // Obtener nombres únicos de sitios de carga
            var loadPointNames = mineralRoutes
                .Select(r => r.loadPointName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            // Obtener IDs de inventario histórico del servidor
            var historicalSites = await GetHistoricalSites(server, tokenService, ct);
            if (historicalSites == null || !historicalSites.Any())
            {
                _logHistoryService.AddLog(config.ServerConfigId, "[InventoryBot] No se pudieron obtener sitios de inventario histórico.", true);
                return;
            }

            // Actualizar TODOS los sitios de inventario con nuevos valores aleatorios
            var random = new Random();
            var updates = new List<object>();

            foreach (var site in historicalSites)
            {
                var tons = random.Next(config.TonnageMin, config.TonnageMax + 1);
                updates.Add(new
                {
                    tons = tons,
                    isConfirmedOre = true,
                    oreInventoryHistoricalId = site.OreInventoryHistoricalId
                });
            }

            // Enviar actualización
            var success = await UpdateInventory(server, tokenService, updates, ct);
            if (success)
            {
                _logHistoryService.AddLog(config.ServerConfigId, $"[InventoryBot] {updates.Count} sitios actualizados exitosamente.");
            }
            else
            {
                _logHistoryService.AddLog(config.ServerConfigId, "[InventoryBot] Error al actualizar inventarios.", true);
            }
        }

        private async Task<int> GetCurrentWorkshift(ServerConfig server, TokenService tokenService, CancellationToken ct)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var response = await client.GetAsync($"{host}/Catalog/GetAllWorkShifts", ct);

                if (!response.IsSuccessStatusCode) return -1;

                var json = await response.Content.ReadAsStringAsync(ct);
                var shifts = JsonConvert.DeserializeObject<List<WorkshiftDto>>(json);

                if (shifts == null || !shifts.Any()) return -1;

                // Determinar turno actual por hora
                var now = DateTime.UtcNow.AddHours(-6); // Hora local México
                var currentTime = now.TimeOfDay;

                foreach (var shift in shifts.Where(s => s.Enabled))
                {
                    var start = TimeSpan.Parse(shift.StartTime);
                    var end = TimeSpan.Parse(shift.EndTime);

                    if (start < end)
                    {
                        if (currentTime >= start && currentTime < end)
                            return shift.WorkShiftId;
                    }
                    else // Turno nocturno (cruza medianoche)
                    {
                        if (currentTime >= start || currentTime < end)
                            return shift.WorkShiftId;
                    }
                }

                return shifts.First().WorkShiftId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InventoryBot] Error obteniendo turno actual");
                return -1;
            }
        }

        private async Task<List<HistoricalSiteDto>?> GetHistoricalSites(ServerConfig server, TokenService tokenService, CancellationToken ct)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var response = await client.GetAsync($"{host}/service/haulages/api/v2/inventory/historical/sites", ct);

                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonConvert.DeserializeObject<List<HistoricalSiteDto>>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InventoryBot] Error obteniendo sitios históricos");
                return null;
            }
        }

        private async Task<bool> UpdateInventory(ServerConfig server, TokenService tokenService, List<object> updates, CancellationToken ct)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var json = JsonConvert.SerializeObject(updates);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PutAsync($"{host}/service/haulages/api/v2/inventory/sites/update", content, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InventoryBot] Error actualizando inventario");
                return false;
            }
        }

        private class WorkshiftDto
        {
            public int WorkShiftId { get; set; }
            public string Description { get; set; } = "";
            public string StartTime { get; set; } = "";
            public string EndTime { get; set; } = "";
            public bool Enabled { get; set; }
        }

        private class HistoricalSiteDto
        {
            [JsonProperty("oreInventoryHistoricalId")]
            public int OreInventoryHistoricalId { get; set; }

            [JsonProperty("place")]
            public string? Place { get; set; }

            [JsonProperty("placeId")]
            public int PlaceId { get; set; }

            [JsonProperty("tons")]
            public decimal Tons { get; set; }
        }
    }
}
