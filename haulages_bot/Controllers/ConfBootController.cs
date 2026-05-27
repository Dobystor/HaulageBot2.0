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
    }
}
