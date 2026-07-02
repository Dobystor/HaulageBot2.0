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
            return Ok(config ?? new ProductionPlanBotConfig { ServerConfigId = serverId });
        }

        [HttpPost("{serverId}")]
        public async Task<IActionResult> SaveConfig(int serverId, [FromBody] ProductionPlanBotConfig input)
        {
            var existing = await _dbContext.ProductionPlanBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (existing == null)
            {
                input.ServerConfigId = serverId;
                _dbContext.ProductionPlanBotConfigs.Add(input);
            }
            else
            {
                existing.TonnageMin = input.TonnageMin;
                existing.TonnageMax = input.TonnageMax;
                existing.LawMinGrTon = input.LawMinGrTon;
                existing.LawMaxGrTon = input.LawMaxGrTon;
                existing.LawMinPercent = input.LawMinPercent;
                existing.LawMaxPercent = input.LawMaxPercent;
                existing.IsEnabled = input.IsEnabled;
            }
            await _dbContext.SaveChangesAsync();
            return Ok(existing ?? input);
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
            }
            else config.IsEnabled = enabled;
            await _dbContext.SaveChangesAsync();
            return Ok(new { isEnabled = config.IsEnabled });
        }

        [HttpGet("{serverId}/plans/{year}")]
        public async Task<IActionResult> GetPlans(int serverId, int year,
            [FromServices] IHttpClientFactory httpClientFactory, [FromServices] TokenService tokenService)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest("Servidor no encontrado");
            var (client, host) = await CreateClient(server, httpClientFactory, tokenService);
            var resp = await client.GetAsync($"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}");
            var body = await resp.Content.ReadAsStringAsync();
            return Content(body, "application/json");
        }

        /// <summary>
        /// Forzar generación de planes para un mes.
        /// 1) Crear workdays si no existen
        /// 2) Obtener plannedWorkId via allworkbyyear
        /// 3) Para cada ruta: UPDATE si tiene plan, CREATE si no tiene
        /// </summary>
        [HttpPost("{serverId}/force/{year}/{month}")]
        public async Task<IActionResult> ForceGenerate(int serverId, int year, int month,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService,
            [FromServices] LogHistoryService logHistory)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest(new { message = "Servidor no encontrado." });

            var config = await _dbContext.ProductionPlanBotConfigs.FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
            var tonnageMin = config?.TonnageMin ?? 3000;
            var tonnageMax = config?.TonnageMax ?? 15000;
            var lawMinGrTon = config?.LawMinGrTon ?? 50m;
            var lawMaxGrTon = config?.LawMaxGrTon ?? 150m;
            var lawMinPercent = config?.LawMinPercent ?? 0.5m;
            var lawMaxPercent = config?.LawMaxPercent ?? 5m;

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            logHistory.AddLog(serverId, $"[ProductionPlanBot] Forzando planes para {year}/{month:D2}...");

            try
            {
                // 1. Crear workdays (todos los días del mes)
                var daysInMonth = DateTime.DaysInMonth(year, month);
                var days = Enumerable.Range(1, daysInMonth)
                    .Select(d => new DateTime(year, month, d).ToString("yyyy-MM-dd") + "T06:00:00.000Z").ToList();

                var (wdClient, _) = await CreateClient(server, httpClientFactory, tokenService);
                var wdJson = JsonConvert.SerializeObject(new { year, month, days });
                await wdClient.PostAsync($"{host}/service/haulages/api/v2/ProductionPlans/add/workdays",
                    new StringContent(wdJson, Encoding.UTF8, "application/json"));

                // 2. Obtener plannedWorkId
                var (awClient, _2) = await CreateClient(server, httpClientFactory, tokenService);
                var awResp = await awClient.GetAsync($"{host}/service/haulages/api/v2/productionplans/allworkbyyear/{year}");
                var awBody = await awResp.Content.ReadAsStringAsync();
                var allWorks = JsonConvert.DeserializeObject<List<WorkByYearDto>>(awBody);
                var monthWork = allWorks?.FirstOrDefault(w => w.Month == month);

                if (monthWork == null || monthWork.PlannedWorkId <= 0)
                {
                    logHistory.AddLog(serverId, "[ProductionPlanBot] No se encontró plannedWorkId.", true);
                    return BadRequest(new { message = $"No se pudo obtener plannedWorkId para mes {month}." });
                }

                int plannedWorkId = monthWork.PlannedWorkId;

                // 3. GET planes existentes
                var (plClient, _3) = await CreateClient(server, httpClientFactory, tokenService);
                var plResp = await plClient.GetAsync($"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}");
                var plBody = await plResp.Content.ReadAsStringAsync();
                var plans = JsonConvert.DeserializeObject<List<PlanRouteDto>>(plBody);

                if (plans == null || !plans.Any())
                    return BadRequest(new { message = "No hay rutas asignadas para este año." });

                // 4. Crear o actualizar cada ruta
                var random = new Random();
                int created = 0, updated = 0;
                var errors = new List<string>();

                var ores = new (int id, string name, string unit)[] {
                    (1,"AG","gr/tons"),(2,"PB","%"),(3,"FE","%"),(4,"AS","%"),
                    (5,"CU","%"),(6,"ZN","%"),(1352,"AU","gr/tons"),(1353,"NI","%")
                };

                foreach (var route in plans)
                {
                    try
                    {
                        var tons = random.Next(tonnageMin, tonnageMax + 1);
                        var lawDetails = ores.Select(o => (object)new {
                            law = o.unit == "gr/tons"
                                ? Math.Round((decimal)(random.NextDouble() * (double)(lawMaxGrTon - lawMinGrTon) + (double)lawMinGrTon), 2)
                                : Math.Round((decimal)(random.NextDouble() * (double)(lawMaxPercent - lawMinPercent) + (double)lawMinPercent), 2),
                            oreName = o.name, oreId = o.id
                        }).ToList();

                        var existingMonth = route.Months?.Where(m => m != null).FirstOrDefault(m => m.Month == month);
                        var (c, _4) = await CreateClient(server, httpClientFactory, tokenService);

                        if (existingMonth != null && existingMonth.ProductionPlanId > 0)
                        {
                            var payload = new { productionPlanId = existingMonth.ProductionPlanId, distance = route.Distance, timeInSite = route.TimeInHour, Tons = tons, lawDetails };
                            var resp = await c.PutAsync($"{host}/service/haulages/api/v2/productionplans/update/productionplan",
                                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
                            if (resp.IsSuccessStatusCode) updated++; else errors.Add($"{route.HaulagePathName}: UPDATE {resp.StatusCode}");
                        }
                        else
                        {
                            var payload = new { pathProductionPlanId = route.PathProductionPlanId, haulagePathId = route.HaulagePathId, distance = route.Distance, timeInSite = route.TimeInHour, tons, plannedWorkId, lawDetails };
                            var resp = await c.PostAsync($"{host}/service/haulages/api/v2/productionplans/add/productionplan",
                                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
                            if (resp.IsSuccessStatusCode) created++;
                            else { var err = await resp.Content.ReadAsStringAsync(); errors.Add($"{route.HaulagePathName}: CREATE {resp.StatusCode} - {err}"); }
                        }
                    }
                    catch (Exception ex) { errors.Add($"{route.HaulagePathName}: {ex.Message}"); }
                }

                var msg = $"{created} creados, {updated} actualizados" + (errors.Any() ? $", {errors.Count} errores" : "");
                logHistory.AddLog(serverId, $"[ProductionPlanBot] {msg}");
                return Ok(new { message = msg, created, updated, plannedWorkId, totalRoutes = plans.Count, errors = errors.Any() ? errors : null });
            }
            catch (Exception ex)
            {
                logHistory.AddLog(serverId, $"[ProductionPlanBot] Error: {ex.Message}", true);
                return StatusCode(500, new { message = "Error interno", detail = ex.Message, stack = ex.StackTrace });
            }
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

        private class PlanRouteDto
        {
            [JsonProperty("pathProductionPlanId")] public int PathProductionPlanId { get; set; }
            [JsonProperty("haulagePathId")] public int HaulagePathId { get; set; }
            [JsonProperty("haulagePathName")] public string? HaulagePathName { get; set; }
            [JsonProperty("distance")] public decimal Distance { get; set; }
            [JsonProperty("timeInHour")] public decimal TimeInHour { get; set; }
            [JsonProperty("months")] public List<PlanMonthDto>? Months { get; set; }
        }

        private class PlanMonthDto
        {
            [JsonProperty("productionPlanId")] public int ProductionPlanId { get; set; }
            [JsonProperty("month")] public int Month { get; set; }
            [JsonProperty("tons")] public decimal Tons { get; set; }
        }

        private class WorkByYearDto
        {
            [JsonProperty("plannedWorkId")] public int PlannedWorkId { get; set; }
            [JsonProperty("month")] public int Month { get; set; }
            [JsonProperty("year")] public int Year { get; set; }
        }

        #endregion
    }
}
