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
                // Devolver config por defecto
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
                // Crear config por defecto y activar
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

        /// <summary>Forzar actualización de inventarios manualmente (sin esperar cambio de turno)</summary>
        [HttpPost("{serverId}/force")]
        public async Task<IActionResult> ForceUpdate(
            int serverId,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService,
            [FromServices] LogHistoryService logHistory)
        {
            var config = await _dbContext.InventoryBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            // Usar config existente o valores por defecto
            var tonnageMin = config?.TonnageMin ?? 200;
            var tonnageMax = config?.TonnageMax ?? 800;

            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null)
                return BadRequest(new { message = "Servidor no encontrado." });

            logHistory.AddLog(serverId, "[InventoryBot] Ejecución manual forzada. Actualizando inventarios...");

            // Obtener rutas de mineral configuradas
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
                logHistory.AddLog(serverId, "[InventoryBot] No hay rutas de mineral configuradas.", true);
                return BadRequest(new { message = "No hay rutas de mineral configuradas." });
            }

            var loadPointNames = mineralRoutes
                .Select(r => r.loadPointName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            // Obtener sitios de inventario histórico
            var historicalSites = await GetHistoricalSites(server, tokenService, httpClientFactory);
            if (historicalSites == null || !historicalSites.Any())
            {
                logHistory.AddLog(serverId, "[InventoryBot] No se pudieron obtener sitios de inventario histórico.", true);
                return BadRequest(new { message = "No se pudieron obtener sitios de inventario histórico del servidor." });
            }

            // Hacer match y generar nuevos valores aleatorios
            var random = new Random();
            var updates = new List<object>();

            foreach (var site in historicalSites)
            {
                var place = site.Place ?? "";
                if (loadPointNames.Any(lp => lp.Contains(place) || place.Contains(lp)))
                {
                    var tons = random.Next(tonnageMin, tonnageMax + 1);
                    updates.Add(new
                    {
                        tons = tons,
                        isConfirmedOre = true,
                        oreInventoryHistoricalId = site.OreInventoryHistoricalId
                    });
                }
            }

            if (!updates.Any())
            {
                logHistory.AddLog(serverId, "[InventoryBot] No hubo match entre rutas de mineral y sitios de inventario.");
                return Ok(new { message = "No hubo match entre rutas de mineral y sitios de inventario.", updated = 0 });
            }

            // Enviar actualización (reemplaza los existentes con nuevos valores)
            var success = await UpdateInventory(server, tokenService, httpClientFactory, updates);
            if (success)
            {
                logHistory.AddLog(serverId, $"[InventoryBot] (Manual) {updates.Count} sitios actualizados exitosamente.");
                return Ok(new { message = $"{updates.Count} sitios actualizados exitosamente.", updated = updates.Count });
            }
            else
            {
                logHistory.AddLog(serverId, "[InventoryBot] Error al actualizar inventarios (ejecución manual).", true);
                return StatusCode(500, new { message = "Error al actualizar inventarios en el servidor remoto." });
            }
        }

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
