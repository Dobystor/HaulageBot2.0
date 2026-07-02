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
    public class InventoryBotService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InventoryBotService> _logger;
        private readonly LogHistoryService _logHistoryService;
        private readonly IHttpClientFactory _httpClientFactory;
        private int _lastWorkshiftId = -1;

        public InventoryBotService(IServiceScopeFactory scopeFactory, ILogger<InventoryBotService> logger,
            LogHistoryService logHistoryService, IHttpClientFactory httpClientFactory)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _logHistoryService = logHistoryService;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(15000, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ProcessInventory(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "[InventoryBot] Error en ciclo principal"); }
                await Task.Delay(60000, stoppingToken);
            }
        }

        private async Task ProcessInventory(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<dbboot>();
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

            var config = await db.InventoryBotConfigs.FirstOrDefaultAsync(c => c.IsEnabled, ct);
            if (config == null) return;

            var server = await db.ServerConfigs.FindAsync(config.ServerConfigId);
            if (server == null) return;

            // Detectar cambio de turno
            var currentWorkshiftId = await GetCurrentWorkshift(server, tokenService, ct);
            if (currentWorkshiftId <= 0) return;
            if (currentWorkshiftId == _lastWorkshiftId) return;
            _lastWorkshiftId = currentWorkshiftId;

            _logHistoryService.AddLog(config.ServerConfigId, $"[InventoryBot] Cambio de turno ({currentWorkshiftId}). Actualizando inventarios...");

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";

            // 1. Obtener loadPoints de rutas de mineral activas
            var dataConfig = await db.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == config.ServerConfigId)
                .OrderByDescending(dc => dc.Id).FirstOrDefaultAsync(ct);
            if (dataConfig == null) return;

            var selectedRouteIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new List<int>();
            var mineralRoutes = await db.Routes
                .Where(r => r.ServerConfigId == config.ServerConfigId && selectedRouteIds.Contains(r.haulagePathId) && r.selectedMaterialType == 0 && r.isEnabled)
                .ToListAsync(ct);
            if (!mineralRoutes.Any()) return;

            var loadPoints = mineralRoutes.Select(r => new { r.loadPointId, r.loadPointName }).GroupBy(r => r.loadPointId).Select(g => g.First()).ToList();

            // 2. GET /HaulageSites/all → match por placeId
            var client1 = CreateClient(server, tokenService);
            var sitesResp = await client1.GetAsync($"{host}/service/haulages/api/v2/HaulageSites/all", ct);
            var sitesBody = await sitesResp.Content.ReadAsStringAsync(ct);
            var allSites = JsonConvert.DeserializeObject<List<HaulageSiteDto>>(sitesBody) ?? new List<HaulageSiteDto>();

            var matchedSites = new List<HaulageSiteDto>();
            foreach (var lp in loadPoints)
            {
                var site = allSites.FirstOrDefault(s => s.PlaceId == lp.loadPointId);
                if (site != null) matchedSites.Add(site);
            }
            if (!matchedSites.Any()) return;

            // 3. Seleccionar entre sitesMin y sitesMax al azar
            var random = new Random();
            var numSites = random.Next(config.SitesMin, config.SitesMax + 1);
            numSites = Math.Min(numSites, matchedSites.Count);
            var selectedSites = matchedSites.OrderBy(_ => random.Next()).Take(numSites).ToList();

            // 4. POST /Inventory/sites/add
            var addPayload = selectedSites.Select(s => (object)new { haulageSiteId = s.HaulageSiteId, placeName = s.PlaceName, placeId = s.PlaceId }).ToList();
            var client2 = CreateClient(server, tokenService);
            var addResp = await client2.PostAsync($"{host}/service/haulages/api/v2/Inventory/sites/add",
                new StringContent(JsonConvert.SerializeObject(addPayload), Encoding.UTF8, "application/json"), ct);

            if (!addResp.IsSuccessStatusCode)
            {
                _logHistoryService.AddLog(config.ServerConfigId, "[InventoryBot] Error al agregar sitios.", true);
                return;
            }

            _logHistoryService.AddLog(config.ServerConfigId, $"[InventoryBot] {selectedSites.Count} sitios agregados.");

            // 5. Esperar y obtener historical/sites
            await Task.Delay(2000, ct);
            var client3 = CreateClient(server, tokenService);
            var histResp = await client3.GetAsync($"{host}/service/haulages/api/v2/inventory/historical/sites", ct);
            var histBody = await histResp.Content.ReadAsStringAsync(ct);
            var historicalSites = JsonConvert.DeserializeObject<List<HistoricalSiteDto>>(histBody) ?? new List<HistoricalSiteDto>();

            if (!historicalSites.Any()) return;

            // 6. PUT /sites/update con tonelajes
            var updates = historicalSites.Select(s => (object)new
            {
                tons = random.Next(config.TonnageMin, config.TonnageMax + 1),
                isConfirmedOre = true,
                oreInventoryHistoricalId = s.OreInventoryHistoricalId
            }).ToList();

            var client4 = CreateClient(server, tokenService);
            await client4.PutAsync($"{host}/service/haulages/api/v2/inventory/sites/update",
                new StringContent(JsonConvert.SerializeObject(updates), Encoding.UTF8, "application/json"), ct);

            _logHistoryService.AddLog(config.ServerConfigId, $"[InventoryBot] {updates.Count} sitios actualizados con tonelaje.");
        }

        private async Task<int> GetCurrentWorkshift(ServerConfig server, TokenService tokenService, CancellationToken ct)
        {
            try
            {
                var client = CreateClient(server, tokenService);
                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var response = await client.GetAsync($"{host}/Catalog/GetAllWorkShifts", ct);
                if (!response.IsSuccessStatusCode) return -1;

                var json = await response.Content.ReadAsStringAsync(ct);
                var shifts = JsonConvert.DeserializeObject<List<WorkshiftDto>>(json);
                if (shifts == null || !shifts.Any()) return -1;

                var now = DateTime.UtcNow.AddHours(-6);
                var currentTime = now.TimeOfDay;

                foreach (var shift in shifts.Where(s => s.Enabled))
                {
                    var start = TimeSpan.Parse(shift.StartTime);
                    var end = TimeSpan.Parse(shift.EndTime);
                    if (start < end) { if (currentTime >= start && currentTime < end) return shift.WorkShiftId; }
                    else { if (currentTime >= start || currentTime < end) return shift.WorkShiftId; }
                }
                return shifts.First().WorkShiftId;
            }
            catch { return -1; }
        }

        private HttpClient CreateClient(ServerConfig server, TokenService tokenService)
        {
            var client = _httpClientFactory.CreateClient();
            var token = tokenService.GetTokenAsync(server.Id).Result;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private class WorkshiftDto
        {
            public int WorkShiftId { get; set; }
            public string StartTime { get; set; } = "";
            public string EndTime { get; set; } = "";
            public bool Enabled { get; set; }
        }

        private class HaulageSiteDto
        {
            [JsonProperty("haulageSiteId")] public int HaulageSiteId { get; set; }
            [JsonProperty("placeId")] public int PlaceId { get; set; }
            [JsonProperty("placeName")] public string? PlaceName { get; set; }
        }

        private class HistoricalSiteDto
        {
            [JsonProperty("oreInventoryHistoricalId")] public int OreInventoryHistoricalId { get; set; }
            [JsonProperty("placeId")] public int PlaceId { get; set; }
            [JsonProperty("tons")] public decimal Tons { get; set; }
        }
    }
}
