using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;
using haulages_bot.Models;
using haulages_bot.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        /// <summary>Obtener config del bot de planes de producción para un servidor</summary>
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

        /// <summary>Guardar o actualizar config del bot de planes de producción</summary>
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

        /// <summary>Toggle rápido del bot</summary>
        [HttpPost("{serverId}/toggle")]
        public async Task<IActionResult> Toggle(int serverId, [FromBody] bool enabled)
        {
            var config = await _dbContext.ProductionPlanBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null)
            {
                config = new ProductionPlanBotConfig
                {
                    ServerConfigId = serverId,
                    IsEnabled = enabled
                };
                _dbContext.ProductionPlanBotConfigs.Add(config);
                await _dbContext.SaveChangesAsync();
                return Ok(new { isEnabled = config.IsEnabled });
            }

            config.IsEnabled = enabled;
            await _dbContext.SaveChangesAsync();
            return Ok(new { isEnabled = config.IsEnabled });
        }

        /// <summary>Proxy: obtener planes de producción para el frontend</summary>
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
            var url = $"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}";

            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, body);

            return Content(body, "application/json");
        }

        /// <summary>
        /// Forzar generación de planes de producción para un mes específico.
        /// 1) GET planes existentes del año
        /// 2) PUT workdays con todos los días del mes
        /// 3) Para cada ruta: CREATE o UPDATE plan con tonelaje y leyes aleatorias
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
            logHistory.AddLog(serverId, $"[ProductionPlanBot] Ejecución manual forzada para {year}/{month:D2}...");

            try
            {
                // 1. GET planes existentes del año
                var client = httpClientFactory.CreateClient();
                var token = await tokenService.GetTokenAsync(server.Id);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var plansUrl = $"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}";
                var plansResp = await client.GetAsync(plansUrl);
                var plansBody = await plansResp.Content.ReadAsStringAsync();

                if (!plansResp.IsSuccessStatusCode)
                {
                    logHistory.AddLog(serverId, $"[ProductionPlanBot] Error al obtener planes: {plansResp.StatusCode}", true);
                    return StatusCode((int)plansResp.StatusCode, new { message = "Error al obtener planes del año", detail = plansBody });
                }

                var plans = JsonConvert.DeserializeObject<List<PlanRouteDto>>(plansBody);
                if (plans == null || !plans.Any())
                {
                    logHistory.AddLog(serverId, "[ProductionPlanBot] No se encontraron rutas de planes.", true);
                    return BadRequest(new { message = "No se encontraron rutas de planes para este año." });
                }

                // 2. PUT workdays — todos los días del mes
                var daysInMonth = DateTime.DaysInMonth(year, month);
                var days = Enumerable.Range(1, daysInMonth)
                    .Select(d => new DateTime(year, month, d).ToString("yyyy-MM-dd") + "T06:00:00.000Z")
                    .ToList();

                var workdaysPayload = new { PlannedWorkId = 0, days };
                var workdaysJson = JsonConvert.SerializeObject(workdaysPayload);
                var workdaysContent = new StringContent(workdaysJson, Encoding.UTF8, "application/json");

                var client2 = httpClientFactory.CreateClient();
                var token2 = await tokenService.GetTokenAsync(server.Id);
                client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

                var workdaysUrl = $"{host}/service/haulages/api/v2/productionplans/update/workdays";
                var workdaysResp = await client2.PutAsync(workdaysUrl, workdaysContent);
                var workdaysBody = await workdaysResp.Content.ReadAsStringAsync();

                if (!workdaysResp.IsSuccessStatusCode)
                {
                    logHistory.AddLog(serverId, $"[ProductionPlanBot] Error al crear workdays: {workdaysResp.StatusCode}", true);
                    return StatusCode((int)workdaysResp.StatusCode, new { message = "Error al crear workdays", detail = workdaysBody });
                }

                // Parsear el PlannedWorkId de la respuesta
                int plannedWorkId = 0;
                try
                {
                    var workdaysResult = JToken.Parse(workdaysBody);
                    // Intentar distintas formas de extraer el ID
                    if (workdaysResult is JObject obj)
                    {
                        plannedWorkId = obj.Value<int?>("plannedWorkId")
                            ?? obj.Value<int?>("PlannedWorkId")
                            ?? obj.Value<int?>("id")
                            ?? obj.Value<int?>("Id")
                            ?? 0;
                    }
                    else if (workdaysResult is JValue val)
                    {
                        plannedWorkId = val.Value<int>();
                    }
                }
                catch
                {
                    // Si no se puede parsear, intentar como entero simple
                    int.TryParse(workdaysBody.Trim().Trim('"'), out plannedWorkId);
                }

                logHistory.AddLog(serverId, $"[ProductionPlanBot] Workdays creados: plannedWorkId={plannedWorkId}, días={daysInMonth}");

                // 3. Para cada ruta, crear o actualizar plan
                var random = new Random();
                int created = 0;
                int updated = 0;
                var errors = new List<string>();

                // Minerales estándar con sus unidades
                var standardOres = new List<(int oreId, string name, string unit)>
                {
                    (1, "AG", "gr/tons"),
                    (2, "PB", "%"),
                    (3, "FE", "%"),
                    (4, "AS", "%"),
                    (5, "CU", "%"),
                    (6, "ZN", "%"),
                    (1352, "AU", "gr/tons"),
                    (1353, "NI", "%")
                };

                foreach (var route in plans)
                {
                    try
                    {
                        var tons = random.Next(tonnageMin, tonnageMax + 1);
                        var existingMonth = route.Months?.FirstOrDefault(m => m.Month == month);

                        object payload;

                        if (existingMonth != null && existingMonth.ProductionPlanId > 0)
                        {
                            // UPDATE — ya tiene plan para este mes
                            var lawDetails = GenerateLawDetails(standardOres, random, lawMinGrTon, lawMaxGrTon, lawMinPercent, lawMaxPercent);
                            payload = new
                            {
                                productionPlanId = existingMonth.ProductionPlanId,
                                distance = route.Distance,
                                timeInSite = route.TimeInHour,
                                Tons = tons,
                                lawDetails
                            };
                        }
                        else
                        {
                            // CREATE — no tiene plan para este mes
                            var lawDetails = GenerateLawDetails(standardOres, random, lawMinGrTon, lawMaxGrTon, lawMinPercent, lawMaxPercent);
                            payload = new
                            {
                                pathProductionPlanId = route.PathProductionPlanId,
                                haulagePathId = route.HaulagePathId,
                                distance = route.Distance,
                                timeInSite = route.TimeInHour,
                                tons,
                                plannedWorkId,
                                lawDetails
                            };
                        }

                        var client3 = httpClientFactory.CreateClient();
                        var token3 = await tokenService.GetTokenAsync(server.Id);
                        client3.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token3);

                        var planJson = JsonConvert.SerializeObject(payload);
                        var planContent = new StringContent(planJson, Encoding.UTF8, "application/json");

                        var planUrl = $"{host}/service/haulages/api/v2/productionplans/update/productionplan";
                        var planResp = await client3.PutAsync(planUrl, planContent);

                        if (planResp.IsSuccessStatusCode)
                        {
                            if (existingMonth != null && existingMonth.ProductionPlanId > 0)
                                updated++;
                            else
                                created++;
                        }
                        else
                        {
                            var errBody = await planResp.Content.ReadAsStringAsync();
                            errors.Add($"Ruta {route.HaulagePathName}: {planResp.StatusCode} - {errBody}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Ruta {route.HaulagePathName}: {ex.Message}");
                    }
                }

                var msg = $"{created} planes creados, {updated} actualizados" + (errors.Any() ? $", {errors.Count} errores" : "");
                logHistory.AddLog(serverId, $"[ProductionPlanBot] (Manual) {msg}");

                return Ok(new
                {
                    message = msg,
                    created,
                    updated,
                    plannedWorkId,
                    totalRoutes = plans.Count,
                    errors = errors.Any() ? errors : null
                });
            }
            catch (Exception ex)
            {
                logHistory.AddLog(serverId, $"[ProductionPlanBot] Error crítico: {ex.Message}", true);
                return StatusCode(500, new { message = "Error interno", detail = ex.Message });
            }
        }

        #region Helpers

        private static List<object> GenerateLawDetails(
            List<(int oreId, string name, string unit)> ores,
            Random random,
            decimal lawMinGrTon, decimal lawMaxGrTon,
            decimal lawMinPercent, decimal lawMaxPercent)
        {
            return ores.Select(ore =>
            {
                decimal law;
                if (ore.unit == "gr/tons")
                {
                    law = Math.Round((decimal)(random.NextDouble() * (double)(lawMaxGrTon - lawMinGrTon) + (double)lawMinGrTon), 2);
                }
                else
                {
                    law = Math.Round((decimal)(random.NextDouble() * (double)(lawMaxPercent - lawMinPercent) + (double)lawMinPercent), 2);
                }

                return (object)new
                {
                    law,
                    oreName = ore.name,
                    oreId = ore.oreId
                };
            }).ToList();
        }

        #endregion

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
            public int Tons { get; set; }

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
