using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using haulages_bot.Data;
using haulages_bot.Models;
using haulages_bot.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace haulages_bot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfBootController : ControllerBase
    {
        private readonly DataSyncJobManual _dataSyncJobManual;
        private readonly BootConfigurationService _bootConfigurationService;
        private readonly dbboot _dbContext;

        public ConfBootController(DataSyncJobManual dataSyncJobManual, BootConfigurationService bootConfigurationService, dbboot dbContext)
        { 
            _dataSyncJobManual = dataSyncJobManual;
            _bootConfigurationService = bootConfigurationService;
            _dbContext = dbContext;
        }

        [HttpPost("dataconf")]
        public async Task<IActionResult> SaveDataConfiguration([FromBody] DataConfBoot datos, [FromQuery] int serverId)
        {
            if (ModelState.IsValid)
            {
                await _bootConfigurationService.SaveDataConfiguration(datos, serverId);
                return await LoadDataFromDatabase(serverId);
            }

            return BadRequest(new { success = false, message = "Datos inválidos." });
        }

        [HttpGet("loadDataFromDb")]
        public async Task<IActionResult> LoadDataFromDatabase([FromQuery] int serverId)
        {
            var datos = await _dbContext.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == serverId)
                .OrderByDescending(dc => dc.Id)
                .FirstOrDefaultAsync();

            if (datos != null)
            {
                var response = new DataConfBoot
                {
                    TonnageVariation = JsonConvert.DeserializeObject<List<int>>(datos.TonnageVariation) ?? new List<int>(),
                    Time = JsonConvert.DeserializeObject<List<int>>(datos.Time) ?? new List<int>(),
                    SelectedRoutes = JsonConvert.DeserializeObject<List<int>>(datos.SelectedRoutes) ?? new List<int>(),
                    SelectedEmployees = JsonConvert.DeserializeObject<List<int>>(datos.SelectedEmployees) ?? new List<int>(),
                    SelectedVehicles = JsonConvert.DeserializeObject<List<int>>(datos.SelectedVehicles) ?? new List<int>()
                };

                return Ok(response);
            }

            // Si no existe, devolver valores por defecto
            return Ok(new DataConfBoot
            {
                TonnageVariation = new List<int> { 10, 20 },
                Time = new List<int> { 5, 15 },
                SelectedRoutes = new List<int>(),
                SelectedEmployees = new List<int>(),
                SelectedVehicles = new List<int>()
            });
        }

        [HttpGet("getdataconf")]
        public async Task<IActionResult> GetDataConfiguration([FromQuery] int serverId)
        {
            var config = await _bootConfigurationService.GetDataConfiguration(serverId);

            if (config != null)
            {
                return Ok(config);
            }

            return NotFound(new { success = false, message = "No se encontró configuración." });
        }

        /// <summary>
        /// Calcula el tonelaje estimado que se registraría en 24 horas según la configuración actual del bot.
        /// Fórmula: (1440 / tiempoPromedio) * vehículos * capacidadPromedio * (1 + variacionPromedio/100)
        /// </summary>
        [HttpGet("estimatedDailyTonnage")]
        public async Task<IActionResult> GetEstimatedDailyTonnage([FromQuery] int serverId)
        {
            var config = await _dbContext.DataConfigurationLocal
                .Where(dc => dc.ServerConfigId == serverId)
                .OrderByDescending(dc => dc.Id)
                .FirstOrDefaultAsync();

            if (config == null)
            {
                return Ok(new { estimatedTonnage = 0.0, detail = "Sin configuración" });
            }

            var timeList = JsonConvert.DeserializeObject<List<int>>(config.Time) ?? new List<int> { 5, 15 };
            var tonnageVar = JsonConvert.DeserializeObject<List<int>>(config.TonnageVariation) ?? new List<int> { 80, 110 };
            var selectedVehicleIds = JsonConvert.DeserializeObject<List<int>>(config.SelectedVehicles) ?? new List<int>();

            if (selectedVehicleIds.Count == 0 || timeList.Count < 2)
            {
                return Ok(new { estimatedTonnage = 0.0, detail = "Sin vehículos o tiempos configurados" });
            }

            // Intervalo promedio en minutos
            double avgInterval = (timeList[0] + timeList[1]) / 2.0;
            if (avgInterval <= 0) avgInterval = 10;

            // Acarreos estimados en 24 horas
            double haulagesPerDay = 1440.0 / avgInterval;

            // Capacidad promedio de los vehículos seleccionados (LoadingCapacity en toneladas)
            var vehicles = await _dbContext.Vehicles
                .Where(v => v.ServerConfigId == serverId && selectedVehicleIds.Contains(v.VehicleId))
                .ToListAsync();

            double avgCapacity = vehicles.Count > 0 ? (double)vehicles.Average(v => v.LoadingCapacity) : 25.0;

            // Factor de variación de tonelaje (el porcentaje indica cuánto del peso nominal se carga)
            // tonnageVar es [min%, max%], ej: [80, 110] significa entre 80% y 110% de la capacidad
            double avgTonnageFactor = (tonnageVar[0] + tonnageVar[1]) / 200.0; // Promedio como fracción

            // Estimado: acarreos/día * capacidad_promedio * factor_variación
            double estimatedTonnage = haulagesPerDay * avgCapacity * avgTonnageFactor;

            return Ok(new
            {
                estimatedTonnage = Math.Round(estimatedTonnage, 1),
                haulagesPerDay = Math.Round(haulagesPerDay, 0),
                avgCapacity = Math.Round(avgCapacity, 2),
                avgInterval,
                vehicleCount = vehicles.Count,
                detail = $"~{Math.Round(haulagesPerDay, 0)} acarreos/día × {Math.Round(avgCapacity, 1)}t promedio"
            });
        }
    }
}
