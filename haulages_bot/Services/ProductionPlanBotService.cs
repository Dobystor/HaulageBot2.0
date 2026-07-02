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
    /// Bot que gestiona planes de producción automáticamente.
    /// - Al inicio de cada año: asigna rutas de mineral (mínimo 8)
    /// - Al inicio de cada mes: crea workdays + planes con tonelaje/leyes aleatorias
    /// </summary>
    public class ProductionPlanBotService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ProductionPlanBotService> _logger;
        private readonly LogHistoryService _logHistoryService;
        private readonly IHttpClientFactory _httpClientFactory;
        private int _lastMonth = -1;
        private int _lastYear = -1;

        public ProductionPlanBotService(
            IServiceScopeFactory scopeFactory,
            ILogger<ProductionPlanBotService> logger,
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
            await Task.Delay(20000, stoppingToken); // Esperar inicio

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndExecute(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ProductionPlanBot] Error en ciclo principal");
                }

                await Task.Delay(3600000, stoppingToken); // Revisar cada hora
            }
        }

        private async Task CheckAndExecute(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<dbboot>();
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();

            var config = await db.ProductionPlanBotConfigs.FirstOrDefaultAsync(c => c.IsEnabled, ct);
            if (config == null) return;

            var server = await db.ServerConfigs.FindAsync(config.ServerConfigId);
            if (server == null) return;

            var now = DateTime.UtcNow.AddHours(-6); // Hora local México
            var currentMonth = now.Month;
            var currentYear = now.Year;

            // Inicializar en primera ejecución
            if (_lastMonth < 0) { _lastMonth = currentMonth; _lastYear = currentYear; return; }

            // Detectar cambio de año
            if (currentYear != _lastYear)
            {
                _lastYear = currentYear;
                _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] Nuevo año detectado ({currentYear}). Asignando rutas...");
                await AssignRoutesForYear(server, config, db, tokenService, currentYear, ct);
            }

            // Detectar cambio de mes
            if (currentMonth != _lastMonth)
            {
                _lastMonth = currentMonth;
                _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] Nuevo mes detectado ({currentYear}/{currentMonth:D2}). Generando planes...");
                await GeneratePlansForMonth(server, config, tokenService, currentYear, currentMonth, ct);
            }
        }

        /// <summary>
        /// Al inicio de año: verificar que haya al menos 8 rutas asignadas.
        /// Si hay menos, completar con rutas de mineral de la configuración general.
        /// </summary>
        private async Task AssignRoutesForYear(ServerConfig server, ProductionPlanBotConfig config, dbboot db, TokenService tokenService, int year, CancellationToken ct)
        {
            try
            {
                var host = GetHost(server);

                // Obtener rutas ya asignadas para este año
                var (plClient, _) = await CreateClient(server, tokenService);
                var plResp = await plClient.GetAsync($"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}", ct);
                var plBody = await plResp.Content.ReadAsStringAsync(ct);
                var existingPlans = JsonConvert.DeserializeObject<List<PlanRouteDto>>(plBody) ?? new List<PlanRouteDto>();

                int currentCount = existingPlans.Count;
                _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] Rutas asignadas actualmente: {currentCount}");

                if (currentCount >= 8) return; // Ya tiene suficientes

                // Obtener rutas de mineral de la configuración general
                var dataConfig = await db.DataConfigurationLocal
                    .Where(dc => dc.ServerConfigId == config.ServerConfigId)
                    .OrderByDescending(dc => dc.Id)
                    .FirstOrDefaultAsync(ct);

                if (dataConfig == null) return;

                var selectedRouteIds = JsonConvert.DeserializeObject<List<int>>(dataConfig.SelectedRoutes) ?? new List<int>();

                var mineralRoutes = await db.Routes
                    .Where(r => r.ServerConfigId == config.ServerConfigId
                        && selectedRouteIds.Contains(r.haulagePathId)
                        && r.selectedMaterialType == 0
                        && r.isEnabled)
                    .ToListAsync(ct);

                if (!mineralRoutes.Any()) return;

                // Filtrar rutas que ya están asignadas
                var assignedPathIds = existingPlans.Select(p => p.HaulagePathId).ToHashSet();
                var availableRoutes = mineralRoutes.Where(r => !assignedPathIds.Contains(r.haulagePathId)).ToList();

                int toAdd = Math.Min(8 - currentCount, availableRoutes.Count);
                if (toAdd <= 0) return;

                var routesToAssign = availableRoutes.Take(toAdd).Select(r => new
                {
                    haulagePathId = r.haulagePathId,
                    year = year
                }).ToList();

                var (aClient, _2) = await CreateClient(server, tokenService);
                var json = JsonConvert.SerializeObject(routesToAssign);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await aClient.PostAsync($"{host}/service/haulages/api/v2/ProductionPlans/assign/paths", content, ct);

                if (resp.IsSuccessStatusCode)
                {
                    _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] {toAdd} rutas asignadas para {year}.");
                }
                else
                {
                    var errBody = await resp.Content.ReadAsStringAsync(ct);
                    _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] Error asignando rutas: {resp.StatusCode} - {errBody}", true);
                }
            }
            catch (Exception ex)
            {
                _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] Error en assign: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Al inicio de mes: crear workdays + planes para todas las rutas asignadas.
        /// </summary>
        private async Task GeneratePlansForMonth(ServerConfig server, ProductionPlanBotConfig config, TokenService tokenService, int year, int month, CancellationToken ct)
        {
            try
            {
                var host = GetHost(server);

                // 1. Crear workdays (todos los días del mes)
                var daysInMonth = DateTime.DaysInMonth(year, month);
                var days = Enumerable.Range(1, daysInMonth)
                    .Select(d => new DateTime(year, month, d).ToString("yyyy-MM-dd") + "T06:00:00.000Z").ToList();

                var (wdClient, _) = await CreateClient(server, tokenService);
                var wdJson = JsonConvert.SerializeObject(new { year, month, days });
                await wdClient.PostAsync($"{host}/service/haulages/api/v2/ProductionPlans/add/workdays",
                    new StringContent(wdJson, Encoding.UTF8, "application/json"), ct);

                // 2. Obtener plannedWorkId
                var (awClient, _2) = await CreateClient(server, tokenService);
                var awResp = await awClient.GetAsync($"{host}/service/haulages/api/v2/productionplans/allworkbyyear/{year}", ct);
                var awBody = await awResp.Content.ReadAsStringAsync(ct);
                var allWorks = JsonConvert.DeserializeObject<List<WorkByYearDto>>(awBody);
                var monthWork = allWorks?.FirstOrDefault(w => w.Month == month);

                if (monthWork == null || monthWork.PlannedWorkId <= 0)
                {
                    _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] No se encontró plannedWorkId para mes {month}.", true);
                    return;
                }

                int plannedWorkId = monthWork.PlannedWorkId;

                // 3. GET planes existentes
                var (plClient, _3) = await CreateClient(server, tokenService);
                var plResp = await plClient.GetAsync($"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}", ct);
                var plBody = await plResp.Content.ReadAsStringAsync(ct);
                var plans = JsonConvert.DeserializeObject<List<PlanRouteDto>>(plBody);

                if (plans == null || !plans.Any())
                {
                    _logHistoryService.AddLog(config.ServerConfigId, "[ProductionPlanBot] No hay rutas asignadas.", true);
                    return;
                }

                // 4. Crear o actualizar cada ruta
                var random = new Random();
                int created = 0, updated = 0;

                var ores = new (int id, string name, string unit)[] {
                    (1,"AG","gr/tons"),(2,"PB","%"),(3,"FE","%"),(4,"AS","%"),
                    (5,"CU","%"),(6,"ZN","%"),(1352,"AU","gr/tons"),(1353,"NI","%")
                };

                foreach (var route in plans)
                {
                    try
                    {
                        var tons = random.Next(config.TonnageMin, config.TonnageMax + 1);
                        var lawDetails = ores.Select(o => (object)new
                        {
                            law = o.unit == "gr/tons"
                                ? Math.Round((decimal)(random.NextDouble() * (double)(config.LawMaxGrTon - config.LawMinGrTon) + (double)config.LawMinGrTon), 2)
                                : Math.Round((decimal)(random.NextDouble() * (double)(config.LawMaxPercent - config.LawMinPercent) + (double)config.LawMinPercent), 2),
                            oreName = o.name,
                            oreId = o.id
                        }).ToList();

                        var existingMonth = route.Months?.Where(m => m != null).FirstOrDefault(m => m.Month == month);
                        var (c, _4) = await CreateClient(server, tokenService);

                        if (existingMonth != null && existingMonth.ProductionPlanId > 0)
                        {
                            var payload = new { productionPlanId = existingMonth.ProductionPlanId, distance = route.Distance, timeInSite = route.TimeInHour, Tons = tons, lawDetails };
                            var resp = await c.PutAsync($"{host}/service/haulages/api/v2/productionplans/update/productionplan",
                                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"), ct);
                            if (resp.IsSuccessStatusCode) updated++;
                        }
                        else
                        {
                            var payload = new { pathProductionPlanId = route.PathProductionPlanId, haulagePathId = route.HaulagePathId, distance = route.Distance, timeInSite = route.TimeInHour, tons, plannedWorkId, lawDetails };
                            var resp = await c.PostAsync($"{host}/service/haulages/api/v2/productionplans/add/productionplan",
                                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"), ct);
                            if (resp.IsSuccessStatusCode) created++;
                        }
                    }
                    catch { /* skip route */ }
                }

                _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] {created} creados, {updated} actualizados para {year}/{month:D2}.");
            }
            catch (Exception ex)
            {
                _logHistoryService.AddLog(config.ServerConfigId, $"[ProductionPlanBot] Error generando planes: {ex.Message}", true);
            }
        }

        #region Helpers

        private string GetHost(ServerConfig server) =>
            server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";

        private async Task<(HttpClient client, string host)> CreateClient(ServerConfig server, TokenService tokenService)
        {
            var client = _httpClientFactory.CreateClient();
            var token = await tokenService.GetTokenAsync(server.Id);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, GetHost(server));
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
