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

        /// <summary>Diagnóstico: probar remove de un sitio específico y mostrar respuesta</summary>
        [HttpGet("{serverId}/debug-remove/{siteId}")]
        public async Task<IActionResult> DebugRemove(
            int serverId, int siteId,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest("Servidor no encontrado");

            var client = httpClientFactory.CreateClient();
            var token = await tokenService.GetTokenAsync(server.Id);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var url = $"{host}/service/haulages/api/v2/inventory/sites/remove/{siteId}";

            // Probar con DELETE
            var deleteResp = await client.DeleteAsync(url);
            var deleteBody = await deleteResp.Content.ReadAsStringAsync();

            // Probar con POST (por si usa POST en vez de DELETE)
            var client2 = httpClientFactory.CreateClient();
            var token2 = await tokenService.GetTokenAsync(server.Id);
            client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);
            var postResp = await client2.PostAsync(url, null);
            var postBody = await postResp.Content.ReadAsStringAsync();

            return Ok(new
            {
                url,
                deleteStatus = (int)deleteResp.StatusCode,
                deleteBody,
                postStatus = (int)postResp.StatusCode,
                postBody
            });
        }

        /// <summary>Diagnóstico: ver qué devuelve el API de sitios históricos y qué loadPoints se tienen</summary>
        [HttpGet("{serverId}/debug")]
        public async Task<IActionResult> Debug(
            int serverId,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest("Servidor no encontrado");

            // Obtener lo que devuelve historical/sites
            var historicalSites = await GetHistoricalSites(server, tokenService, httpClientFactory);

            // Obtener rutas de mineral
            var dataConfig = await _dbContext.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == serverId)
                .OrderByDescending(dc => dc.Id)
                .FirstOrDefaultAsync();

            var selectedRouteIds = dataConfig != null
                ? JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new List<int>()
                : new List<int>();

            var mineralRoutes = await _dbContext.Routes
                .Where(r => r.ServerConfigId == serverId
                    && selectedRouteIds.Contains(r.haulagePathId)
                    && r.selectedMaterialType == 0
                    && r.isEnabled)
                .Select(r => new { r.loadPointId, r.loadPointName })
                .ToListAsync();

            return Ok(new
            {
                historicalSitesCount = historicalSites?.Count ?? 0,
                historicalSites = historicalSites,
                mineralLoadPoints = mineralRoutes.GroupBy(r => r.loadPointId).Select(g => g.First()).ToList()
            });
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
        /// 1) Obtiene los sitios históricos existentes
        /// 2) Actualiza sus tonelajes con valores aleatorios (PUT /sites/update)
        /// 3) Si hay menos sitios que loadPoints disponibles, agrega los faltantes (POST /sites/add)
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

            logHistory.AddLog(serverId, "[InventoryBot] Ejecución manual forzada...");

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

            var random = new Random();
            int updated = 0;
            int added = 0;

            // 1. Obtener sitios existentes y actualizarlos con nuevos tonelajes
            var existingSites = await GetHistoricalSites(server, tokenService, httpClientFactory);
            if (existingSites != null && existingSites.Any())
            {
                var updates = existingSites.Select(site => (object)new
                {
                    tons = random.Next(tonnageMin, tonnageMax + 1),
                    isConfirmedOre = true,
                    oreInventoryHistoricalId = site.OreInventoryHistoricalId
                }).ToList();

                var updateSuccess = await UpdateInventory(server, tokenService, httpClientFactory, updates);
                if (updateSuccess)
                {
                    updated = updates.Count;
                    logHistory.AddLog(serverId, $"[InventoryBot] {updated} sitios existentes actualizados con nuevos tonelajes.");
                }
            }

            // 2. Obtener loadPoints de las rutas que NO tienen sitio todavía y agregarlos
            var existingPlaceIds = existingSites?.Select(s => s.PlaceId).ToHashSet() ?? new HashSet<int>();
            var loadPointsToAdd = mineralRoutes
                .Select(r => new { r.loadPointId, r.loadPointName })
                .Where(lp => !string.IsNullOrWhiteSpace(lp.loadPointName) && !existingPlaceIds.Contains(lp.loadPointId))
                .GroupBy(lp => lp.loadPointId)
                .Select(g => g.First())
                .ToList();

            if (loadPointsToAdd.Any())
            {
                var newSites = loadPointsToAdd.Select(lp => (object)new
                {
                    placeId = lp.loadPointId,
                    place = lp.loadPointName,
                    tons = random.Next(tonnageMin, tonnageMax + 1),
                    isConfirmedOre = true
                }).ToList();

                var (addSuccess, _) = await AddSites(server, tokenService, httpClientFactory, newSites);
                if (addSuccess)
                {
                    added = newSites.Count;
                    logHistory.AddLog(serverId, $"[InventoryBot] {added} sitios nuevos agregados.");
                }
            }

            var msg = $"{updated} actualizados, {added} nuevos agregados.";
            logHistory.AddLog(serverId, $"[InventoryBot] (Manual) {msg}");
            return Ok(new { message = msg, updated, added });
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

        private async Task<bool> UpdateInventory(ServerConfig server, TokenService tokenService, IHttpClientFactory httpClientFactory, List<object> updates)
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
                var json = JsonConvert.SerializeObject(updates);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PutAsync($"{host}/service/haulages/api/v2/inventory/sites/update", content);
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
