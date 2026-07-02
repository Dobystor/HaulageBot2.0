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
    public class ProductionPlanBotController : ControllerBase
    {
        private readonly dbboot _dbContext;

        public ProductionPlanBotController(dbboot dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("{serverId}")]
        public async Task<IActionResult> GetConfig(int serverId)
        {
            var config = await _dbContext.ProductionPlanBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null)
            {
                return Ok(new ProductionPlanBotConfig
                {
                    ServerConfigId = serverId,
                    TonnageMin = 3000,
                    TonnageMax = 15000,
                    LawMinGrTon = 50,
                    LawMaxGrTon = 150,
                    LawMinPercent = 0.5m,
                    LawMaxPercent = 5,
                    IsEnabled = false
                });
            }

            return Ok(config);
        }

        [HttpPost("{serverId}")]
        public async Task<IActionResult> SaveConfig(int serverId, [FromBody] ProductionPlanBotConfig input)
        {
            var existing = await _dbContext.ProductionPlanBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (existing == null)
            {
                var newConfig = new ProductionPlanBotConfig
                {
                    ServerConfigId = serverId,
                    TonnageMin = input.TonnageMin,
                    TonnageMax = input.TonnageMax,
                    LawMinGrTon = input.LawMinGrTon,
                    LawMaxGrTon = input.LawMaxGrTon,
                    LawMinPercent = input.LawMinPercent,
                    LawMaxPercent = input.LawMaxPercent,
                    IsEnabled = input.IsEnabled
                };
                _dbContext.ProductionPlanBotConfigs.Add(newConfig);
                await _dbContext.SaveChangesAsync();
                return Ok(newConfig);
            }

            existing.TonnageMin = input.TonnageMin;
            existing.TonnageMax = input.TonnageMax;
            existing.LawMinGrTon = input.LawMinGrTon;
            existing.LawMaxGrTon = input.LawMaxGrTon;
            existing.LawMinPercent = input.LawMinPercent;
            existing.LawMaxPercent = input.LawMaxPercent;
            existing.IsEnabled = input.IsEnabled;

            await _dbContext.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpPost("{serverId}/toggle")]
        public async Task<IActionResult> Toggle(int serverId, [FromBody] bool enabled)
        {
            var config = await _dbContext.ProductionPlanBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null)
            {
                config = new ProductionPlanBotConfig { ServerConfigId = serverId, IsEnabled = enabled };
                _dbContext.ProductionPlanBotConfigs.Add(config);
                await _dbContext.SaveChangesAsync();
                return Ok(new { isEnabled = config.IsEnabled });
            }

            config.IsEnabled = enabled;
            await _dbContext.SaveChangesAsync();
            return Ok(new { isEnabled = config.IsEnabled });
        }

        [HttpGet("{serverId}/plans/{year}")]
        public async Task<IActionResult> GetPlans(
            int serverId, int year,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest("Servidor no encontrado");

            var client = httpClientFactory.CreateClient();
            var token = await tokenService.GetTokenAsync(server.Id);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var response = await client.GetAsync($"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, body);

            return Content(body, "application/json");
        }

        /// <summary>
        /// Forzar actualización de planes para un mes.
        /// Solo actualiza rutas que ya tengan plan para ese mes (usa productionPlanId existente).
        /// </summary>
        [HttpPost("{serverId}/force/{year}/{month}")]
        public async Task<IActionResult> ForceGenerate(
            int serverId, int year, int month,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService,
            [FromServices] LogHistoryService logHistory)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null)
                return BadRequest(new { message = "Servidor no encontrado." });

            var config = await _dbContext.ProductionPlanBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            var tonnageMin = config?.TonnageMin ?? 3000;
            var tonnageMax = config?.TonnageMax ?? 15000;
            var lawMinGrTon = config?.LawMinGrTon ?? 50;
            var lawMaxGrTon = config?.LawMaxGrTon ?? 150;
            var lawMinPercent = config?.LawMinPercent ?? 0.5m;
            var lawMaxPercent = config?.LawMaxPercent ?? 5;

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            logHistory.AddLog(serverId, $"[ProductionPlanBot] Forzando planes para {year}/{month:D2}...");

            try
            {
                // 1. GET planes existentes
                var client = httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var plansResp = await client.GetAsync($"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}");
                var plansBody = await plansResp.Content.ReadAsStringAsync();

                if (!plansResp.IsSuccessStatusCode)
                {
                    logHistory.AddLog(serverId, "[ProductionPlanBot] Error al obtener planes.", true);
                    return StatusCode((int)plansResp.StatusCode, new { message = "Error al obtener planes" });
                }

                var plans = JsonConvert.DeserializeObject<List<PlanRouteDto>>(plansBody);
                if (plans == null || !plans.Any())
                    return BadRequest(new { message = "No se encontraron rutas de planes." });

                // 2. Actualizar cada ruta que tenga plan para este mes
                var random = new Random();
                int updated = 0;
                int skipped = 0;
                var errors = new List<string>();

                var standardOres = new List<(int oreId, string name, string unit)>
                {
                    (1, "AG", "gr/tons"), (2, "PB", "%"), (3, "FE", "%"),
                    (4, "AS", "%"), (5, "CU", "%"), (6, "ZN", "%"),
                    (1352, "AU", "gr/tons"), (1353, "NI", "%")
                };

                foreach (var route in plans)
                {
                    var existingMonth = route.Months?.FirstOrDefault(m => m.Month == month);

                    if (existingMonth == null || existingMonth.ProductionPlanId <= 0)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var tons = random.Next(tonnageMin, tonnageMax + 1);
                        var lawDetails = standardOres.Select(ore =>
                        {
                            decimal law = ore.unit == "gr/tons"
                                ? Math.Round((decimal)(random.NextDouble() * (double)(lawMaxGrTon - lawMinGrTon) + (double)lawMinGrTon), 2)
                                : Math.Round((decimal)(random.NextDouble() * (double)(lawMaxPercent - lawMinPercent) + (double)lawMinPercent), 2);
                            return (object)new { law, oreName = ore.name, oreId = ore.oreId };
                        }).ToList();

                        var payload = new
                        {
                            productionPlanId = existingMonth.ProductionPlanId,
                            distance = route.Distance,
                            timeInSite = route.TimeInHour,
                            Tons = tons,
                            lawDetails
                        };

                        var c = httpClientFactory.CreateClient();
                        var t = await tokenService.GetTokenAsync(server.Id);
                        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);

                        var json = JsonConvert.SerializeObject(payload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var resp = await c.PutAsync($"{host}/service/haulages/api/v2/productionplans/update/productionplan", content);

                        if (resp.IsSuccessStatusCode)
                            updated++;
                        else
                            errors.Add($"{route.HaulagePathName}: {resp.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{route.HaulagePathName}: {ex.Message}");
                    }
                }

                var msg = $"{updated} actualizados, {skipped} sin plan para mes {month}";
                logHistory.AddLog(serverId, $"[ProductionPlanBot] {msg}");
                return Ok(new { message = msg, updated, skipped, totalRoutes = plans.Count, errors = errors.Any() ? errors : null });
            }
            catch (Exception ex)
            {
                logHistory.AddLog(serverId, $"[ProductionPlanBot] Error: {ex.Message}", true);
                return StatusCode(500, new { message = "Error interno", detail = ex.Message });
            }
        }

        #region DTOs

        private class PlanRouteDto
        {
            [JsonProperty("pathProductionPlanId")]
            public int PathProductionPlanId { get; set; }
            [JsonProperty("haulagePathId")]
            public int HaulagePathId { get; set; }
            [JsonProperty("haulagePathName")]
            public string? HaulagePathName { get; set; }
            [JsonProperty("distance")]
            public decimal Distance { get; set; }
            [JsonProperty("timeInHour")]
            public decimal TimeInHour { get; set; }
            [JsonProperty("months")]
            public List<PlanMonthDto>? Months { get; set; }
        }

        private class PlanMonthDto
        {
            [JsonProperty("productionPlanId")]
            public int ProductionPlanId { get; set; }
            [JsonProperty("month")]
            public int Month { get; set; }
            [JsonProperty("tons")]
            public decimal Tons { get; set; }
            [JsonProperty("minerals")]
            public List<PlanMineralDto>? Minerals { get; set; }
        }

        private class PlanMineralDto
        {
            [JsonProperty("oreId")]
            public int OreId { get; set; }
            [JsonProperty("name")]
            public string? Name { get; set; }
            [JsonProperty("law")]
            public decimal Law { get; set; }
            [JsonProperty("unit")]
            public string? Unit { get; set; }
        }

        #endregion
    }
}
