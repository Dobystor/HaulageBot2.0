using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;
using haulages_bot.Models;
using haulages_bot.Services;
using System.Linq;
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
    }
}
