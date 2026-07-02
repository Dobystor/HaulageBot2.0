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

        /// <summary>Obtener config del bot de inventarios para un servidor</summary>
        [HttpGet("{serverId}")]
        public async Task<IActionResult> GetConfig(int serverId)
        {
            var config = await _dbContext.InventoryBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null)
            {
                return Ok(new InventoryBotConfig
                {
                    ServerConfigId = serverId,
                    TonnageMin = 200,
                    TonnageMax = 800,
                    SitesMin = 2,
                    SitesMax = 5,
                    IsEnabled = false
                });
            }

            return Ok(config);
        }

        /// <summary>Guardar o actualizar config del bot de inventarios</summary>
        [HttpPost("{serverId}")]
        public async Task<IActionResult> SaveConfig(int serverId, [FromBody] InventoryBotConfig input)
        {
            var existing = await _dbContext.InventoryBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (existing == null)
            {
                var newConfig = new InventoryBotConfig
                {
                    ServerConfigId = serverId,
                    TonnageMin = input.TonnageMin,
                    TonnageMax = input.TonnageMax,
                    SitesMin = input.SitesMin,
                    SitesMax = input.SitesMax,
                    IsEnabled = input.IsEnabled
                };
                _dbContext.InventoryBotConfigs.Add(newConfig);
                await _dbContext.SaveChangesAsync();
                return Ok(newConfig);
            }

            existing.TonnageMin = input.TonnageMin;
            existing.TonnageMax = input.TonnageMax;
            existing.SitesMin = input.SitesMin;
            existing.SitesMax = input.SitesMax;
            existing.IsEnabled = input.IsEnabled;

            await _dbContext.SaveChangesAsync();
            return Ok(existing);
        }

        /// <summary>Toggle rápido del bot</summary>
        [HttpPost("{serverId}/toggle")]
        public async Task<IActionResult> Toggle(int serverId, [FromBody] bool enabled)
        {
            var config = await _dbContext.InventoryBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null)
            {
                config = new InventoryBotConfig
                {
                    ServerConfigId = serverId,
                    TonnageMin = 200,
                    TonnageMax = 800,
                    SitesMin = 2,
                    SitesMax = 5,
                    IsEnabled = enabled
                };
                _dbContext.InventoryBotConfigs.Add(config);
                await _dbContext.SaveChangesAsync();
                return Ok(new { isEnabled = config.IsEnabled });
            }

            config.IsEnabled = enabled;
            await _dbContext.SaveChangesAsync();
            return Ok(new { isEnabled = config.IsEnabled });
        }

        /// <summary>Estado actual del bot con último log</summary>
        [HttpGet("{serverId}/status")]
        public async Task<IActionResult> GetStatus(int serverId, [FromServices] LogHistoryService logHistory)
        {
            var config = await _dbContext.InventoryBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            var logs = logHistory.GetLogs(serverId);
            var lastInventoryLog = logs?
                .Where(l => (l.Message ?? "").Contains("[InventoryBot]"))
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefault();

            return Ok(new
            {
                isEnabled = config?.IsEnabled ?? false,
                tonnageMin = config?.TonnageMin ?? 200,
                tonnageMax = config?.TonnageMax ?? 800,
                sitesMin = config?.SitesMin ?? 2,
                sitesMax = config?.SitesMax ?? 5,
                lastLog = lastInventoryLog?.Message?.Replace("[InventoryBot] ", "") ?? null,
                lastLogTime = lastInventoryLog?.Timestamp
            });
        }

        /// <summary>
        /// Forzar actualización de inventarios manualmente.
        /// 1) Elimina todos los sitios existentes (remove)
        /// 2) Agrega nuevos sitios basados en los loadPoints de rutas de mineral activas (add)
        /// </summary>
        [HttpPost("{serverId}/force")]
        public async Task<IActionResult> ForceUpdate(
            int serverId,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService,
            [FromServices] LogHistoryService logHistory)
        {
            var config = await _dbContext.InventoryBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            var tonnageMin = config?.TonnageMin ?? 200;
            var tonnageMax = config?.TonnageMax ?? 800;

            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null)
                return BadRequest(new { message = "Servidor no encontrado." });

            logHistory.AddLog(serverId, "[InventoryBot] Ejecución manual forzada. Removiendo sitios anteriores y agregando nuevos...");

            // Obtener rutas de mineral activas y seleccionadas
            var dataConfig = await _dbContext.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == serverId)
                .OrderByDescending(dc => dc.Id)
                .FirstOrDefaultAsync();

            if (dataConfig == null)
                return BadRequest(new { message = "No hay configuración de datos para este servidor." });

            var selectedRouteIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new List<int>();

            var mineralRoutes = await _dbContext.Routes
                .Where(r => r.ServerConfigId == serverId
                    && selectedRouteIds.Contains(r.haulagePathId)
                    && r.selectedMaterialType == 0
                    && r.isEnabled)
                .ToListAsync();

            if (!mineralRoutes.Any())
            {
                logHistory.AddLog(serverId, "[InventoryBot] No hay rutas de mineral activas configuradas.", true);
                return BadRequest(new { message = "No hay rutas de mineral activas configuradas." });
            }

            // 1. Obtener sitios actuales y eliminarlos todos
            var existingSites = await GetHistoricalSites(server, tokenService, httpClientFactory);
            int removed = 0;
            if (existingSites != null && existingSites.Any())
            {
                foreach (var site in existingSites)
                {
                    var ok = await RemoveSite(server, tokenService, httpClientFactory, site.OreInventoryHistoricalId);
                    if (ok) removed++;
                }
                logHistory.AddLog(serverId, $"[InventoryBot] {removed} sitios anteriores eliminados.");
            }

            // 2. Obtener sitios de carga únicos de las rutas de mineral
            var loadPoints = mineralRoutes
                .Select(r => new { r.loadPointId, r.loadPointName })
                .Where(lp => !string.IsNullOrWhiteSpace(lp.loadPointName))
                .GroupBy(lp => lp.loadPointId)
                .Select(g => g.First())
                .ToList();

            // 3. Agregar nuevos sitios con tonelaje aleatorio en una sola llamada (array)
            var random = new Random();
            var newSites = loadPoints.Select(lp => (object)new
            {
                placeId = lp.loadPointId,
                place = lp.loadPointName,
                tons = random.Next(tonnageMin, tonnageMax + 1),
                isConfirmedOre = true
            }).ToList();

            var (addSuccess, addResponse) = await AddSites(server, tokenService, httpClientFactory, newSites);

            if (addSuccess)
            {
                logHistory.AddLog(serverId, $"[InventoryBot] (Manual) {newSites.Count} sitios nuevos agregados exitosamente.");
                return Ok(new { message = $"{removed} eliminados, {newSites.Count} nuevos agregados.", removed, added = newSites.Count });
            }
            else
            {
                logHistory.AddLog(serverId, "[InventoryBot] Error al agregar sitios nuevos.", true);
                return StatusCode(500, new { message = "Error al agregar sitios de inventario.", apiResponse = addResponse });
            }
        }

        #region Helpers HTTP

        private async Task<List<HistoricalSiteDto>?> GetHistoricalSites(ServerConfig server, TokenService tokenService, IHttpClientFactory httpClientFactory)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var response = await client.GetAsync($"{host}/service/haulages/api/v2/inventory/historical/sites");

                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<HistoricalSiteDto>>(json);
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> RemoveSite(ServerConfig server, TokenService tokenService, IHttpClientFactory httpClientFactory, int siteId)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var response = await client.DeleteAsync($"{host}/service/haulages/api/v2/inventory/sites/remove/{siteId}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<(bool success, string? responseBody)> AddSites(ServerConfig server, TokenService tokenService, IHttpClientFactory httpClientFactory, List<object> sites)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var json = JsonConvert.SerializeObject(sites);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{host}/service/haulages/api/v2/Inventory/sites/add", content);
                var body = await response.Content.ReadAsStringAsync();
                return (response.IsSuccessStatusCode, body);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        #endregion

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
