using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;
using haulages_bot.Models;
using haulages_bot.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace haulages_bot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryBotController : ControllerBase
    {
        private readonly dbboot _dbContext;

        public InventoryBotController(dbboot dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("{serverId}")]
        public async Task<IActionResult> GetConfig(int serverId)
        {
            var config = await _dbContext.InventoryBotConfigs.FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
            return Ok(config ?? new InventoryBotConfig { ServerConfigId = serverId });
        }

        [HttpPost("{serverId}")]
        public async Task<IActionResult> SaveConfig(int serverId, [FromBody] InventoryBotConfig input)
        {
            var existing = await _dbContext.InventoryBotConfigs.FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
            if (existing == null)
            {
                input.ServerConfigId = serverId;
                _dbContext.InventoryBotConfigs.Add(input);
            }
            else
            {
                existing.TonnageMin = input.TonnageMin;
                existing.TonnageMax = input.TonnageMax;
                existing.SitesMin = input.SitesMin;
                existing.SitesMax = input.SitesMax;
                existing.IsEnabled = input.IsEnabled;
            }
            await _dbContext.SaveChangesAsync();
            return Ok(existing ?? input);
        }

        [HttpPost("{serverId}/toggle")]
        public async Task<IActionResult> Toggle(int serverId, [FromBody] bool enabled)
        {
            var config = await _dbContext.InventoryBotConfigs.FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
            if (config == null)
            {
                config = new InventoryBotConfig { ServerConfigId = serverId, IsEnabled = enabled };
                _dbContext.InventoryBotConfigs.Add(config);
            }
            else config.IsEnabled = enabled;
            await _dbContext.SaveChangesAsync();
            return Ok(new { isEnabled = config.IsEnabled });
        }

        [HttpGet("{serverId}/status")]
        public async Task<IActionResult> GetStatus(int serverId, [FromServices] LogHistoryService logHistory)
        {
            var config = await _dbContext.InventoryBotConfigs.FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
            var logs = logHistory.GetLogs(serverId);
            var lastLog = logs?.Where(l => (l.Message ?? "").Contains("[InventoryBot]")).OrderByDescending(l => l.Timestamp).FirstOrDefault();
            return Ok(new
            {
                isEnabled = config?.IsEnabled ?? false,
                tonnageMin = config?.TonnageMin ?? 200,
                tonnageMax = config?.TonnageMax ?? 800,
                sitesMin = config?.SitesMin ?? 2,
                sitesMax = config?.SitesMax ?? 5,
                lastLog = lastLog?.Message?.Replace("[InventoryBot] ", "") ?? null,
                lastLogTime = lastLog?.Timestamp
            });
        }

        /// <summary>
        /// Forzar inventarios:
        /// 1) Obtener loadPoints de rutas de mineral activas
        /// 2) GET /HaulageSites/all → match por placeId para obtener haulageSiteId
        /// 3) Seleccionar entre sitesMin y sitesMax sitios al azar
        /// 4) POST /Inventory/sites/add con [{haulageSiteId, placeName, placeId}]
        /// 5) GET historical/sites → obtener oreInventoryHistoricalId
        /// 6) PUT /sites/update con tonelajes aleatorios
        /// </summary>
        [HttpPost("{serverId}/force")]
        public async Task<IActionResult> ForceUpdate(int serverId,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService,
            [FromServices] LogHistoryService logHistory)
        {
            var config = await _dbContext.InventoryBotConfigs.FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
            var tonnageMin = config?.TonnageMin ?? 200;
            var tonnageMax = config?.TonnageMax ?? 800;
            var sitesMin = config?.SitesMin ?? 2;
            var sitesMax = config?.SitesMax ?? 5;

            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest(new { message = "Servidor no encontrado." });

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            logHistory.AddLog(serverId, "[InventoryBot] Ejecución manual forzada...");

            // 1. Obtener loadPoints de rutas de mineral activas
            var dataConfig = await _dbContext.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == serverId)
                .OrderByDescending(dc => dc.Id).FirstOrDefaultAsync();

            if (dataConfig == null) return BadRequest(new { message = "No hay configuración de datos." });

            var selectedRouteIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new List<int>();
            var mineralRoutes = await _dbContext.Routes
                .Where(r => r.ServerConfigId == serverId && selectedRouteIds.Contains(r.haulagePathId) && r.selectedMaterialType == 0 && r.isEnabled)
                .ToListAsync();

            if (!mineralRoutes.Any()) return BadRequest(new { message = "No hay rutas de mineral activas." });

            var loadPoints = mineralRoutes.Select(r => new { r.loadPointId, r.loadPointName }).GroupBy(r => r.loadPointId).Select(g => g.First()).ToList();

            // 2. GET /HaulageSites/all → match por placeId
            var (client1, _) = await CreateClient(server, httpClientFactory, tokenService);
            var sitesResp = await client1.GetAsync($"{host}/service/haulages/api/v2/HaulageSites/all");
            var sitesBody = await sitesResp.Content.ReadAsStringAsync();
            var allHaulageSites = JsonConvert.DeserializeObject<List<HaulageSiteDto>>(sitesBody) ?? new List<HaulageSiteDto>();

            // Match: loadPointId == haulageSite.placeId
            var matchedSites = new List<HaulageSiteDto>();
            foreach (var lp in loadPoints)
            {
                var site = allHaulageSites.FirstOrDefault(s => s.PlaceId == lp.loadPointId);
                if (site != null) matchedSites.Add(site);
            }

            if (!matchedSites.Any())
            {
                logHistory.AddLog(serverId, "[InventoryBot] No se encontraron haulageSites que coincidan con los loadPoints.", true);
                return BadRequest(new { message = "No hay match entre loadPoints y haulageSites." });
            }

            // 3. Seleccionar entre sitesMin y sitesMax sitios al azar
            var random = new Random();
            var numSites = random.Next(sitesMin, sitesMax + 1);
            numSites = Math.Min(numSites, matchedSites.Count);
            var selectedSites = matchedSites.OrderBy(_ => random.Next()).Take(numSites).ToList();

            // 4. POST /Inventory/sites/add
            var addPayload = selectedSites.Select(s => (object)new
            {
                haulageSiteId = s.HaulageSiteId,
                placeName = s.PlaceName,
                placeId = s.PlaceId
            }).ToList();

            var (client2, _2) = await CreateClient(server, httpClientFactory, tokenService);
            var addJson = JsonConvert.SerializeObject(addPayload);
            var addResp = await client2.PostAsync($"{host}/service/haulages/api/v2/Inventory/sites/add",
                new StringContent(addJson, Encoding.UTF8, "application/json"));
            var addBody = await addResp.Content.ReadAsStringAsync();

            if (!addResp.IsSuccessStatusCode)
            {
                logHistory.AddLog(serverId, $"[InventoryBot] Error al agregar sitios: {addResp.StatusCode}", true);
                return StatusCode((int)addResp.StatusCode, new { message = "Error al agregar sitios", detail = addBody });
            }

            logHistory.AddLog(serverId, $"[InventoryBot] {selectedSites.Count} sitios agregados.");

            // 5. GET historical/sites → obtener oreInventoryHistoricalId
            await Task.Delay(1000); // Esperar un momento para que SmartFlow procese
            var (client3, _3) = await CreateClient(server, httpClientFactory, tokenService);
            var histResp = await client3.GetAsync($"{host}/service/haulages/api/v2/inventory/historical/sites");
            var histBody = await histResp.Content.ReadAsStringAsync();
            var historicalSites = JsonConvert.DeserializeObject<List<HistoricalSiteDto>>(histBody) ?? new List<HistoricalSiteDto>();

            if (!historicalSites.Any())
            {
                logHistory.AddLog(serverId, "[InventoryBot] Sitios agregados pero no aparecen en historical/sites aún.");
                return Ok(new { message = $"{selectedSites.Count} sitios agregados, pendiente asignar tonelaje.", added = selectedSites.Count });
            }

            // 6. PUT /sites/update con tonelajes aleatorios
            var updates = historicalSites.Select(site => (object)new
            {
                tons = random.Next(tonnageMin, tonnageMax + 1),
                isConfirmedOre = true,
                oreInventoryHistoricalId = site.OreInventoryHistoricalId
            }).ToList();

            var (client4, _4) = await CreateClient(server, httpClientFactory, tokenService);
            var updateJson = JsonConvert.SerializeObject(updates);
            var updateResp = await client4.PutAsync($"{host}/service/haulages/api/v2/inventory/sites/update",
                new StringContent(updateJson, Encoding.UTF8, "application/json"));

            int updated = updateResp.IsSuccessStatusCode ? updates.Count : 0;
            var msg = $"{selectedSites.Count} sitios agregados, {updated} tonelajes actualizados.";
            logHistory.AddLog(serverId, $"[InventoryBot] {msg}");
            return Ok(new { message = msg, added = selectedSites.Count, updated });
        }

        #region Helpers

        private async Task<(HttpClient client, string host)> CreateClient(ServerConfig server, IHttpClientFactory factory, TokenService tokenService)
        {
            var client = factory.CreateClient();
            var token = await tokenService.GetTokenAsync(server.Id);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            return (client, host);
        }

        #endregion

        #region DTOs

        private class HaulageSiteDto
        {
            [JsonProperty("haulageSiteId")] public int HaulageSiteId { get; set; }
            [JsonProperty("placeId")] public int PlaceId { get; set; }
            [JsonProperty("placeName")] public string? PlaceName { get; set; }
            [JsonProperty("siteType")] public int SiteType { get; set; }
        }

        private class HistoricalSiteDto
        {
            [JsonProperty("oreInventoryHistoricalId")] public int OreInventoryHistoricalId { get; set; }
            [JsonProperty("place")] public string? Place { get; set; }
            [JsonProperty("placeId")] public int PlaceId { get; set; }
            [JsonProperty("tons")] public decimal Tons { get; set; }
        }

        #endregion
    }
}
