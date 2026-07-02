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
    public class RethinkBotController : ControllerBase
    {
        private readonly dbboot _dbContext;

        public RethinkBotController(dbboot dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>Obtener config del bot de RethinkDB para un servidor</summary>
        [HttpGet("{serverId}")]
        public async Task<IActionResult> GetConfig(int serverId)
        {
            var config = await _dbContext.RethinkBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null)
            {
                // Devolver config por defecto
                return Ok(new RethinkBotConfig
                {
                    ServerConfigId = serverId,
                    RethinkHost = "",
                    RethinkPort = 28015,
                    IntervalSeconds = 30,
                    MaxSimultaneousVehicles = 5,
                    IsEnabled = false
                });
            }

            return Ok(config);
        }

        /// <summary>Guardar o actualizar config del bot de RethinkDB</summary>
        [HttpPost("{serverId}")]
        public async Task<IActionResult> SaveConfig(int serverId, [FromBody] RethinkBotConfig input)
        {
            var existing = await _dbContext.RethinkBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (existing == null)
            {
                var newConfig = new RethinkBotConfig
                {
                    ServerConfigId = serverId,
                    RethinkHost = input.RethinkHost,
                    RethinkPort = input.RethinkPort,
                    RethinkPassword = input.RethinkPassword ?? "",
                    IntervalSeconds = input.IntervalSeconds,
                    MaxSimultaneousVehicles = input.MaxSimultaneousVehicles,
                    ScooptramCount = input.ScooptramCount,
                    IsEnabled = input.IsEnabled
                };
                _dbContext.RethinkBotConfigs.Add(newConfig);
                await _dbContext.SaveChangesAsync();
                return Ok(newConfig);
            }

            existing.RethinkHost = input.RethinkHost;
            existing.RethinkPort = input.RethinkPort;
            existing.RethinkPassword = input.RethinkPassword ?? "";
            existing.IntervalSeconds = input.IntervalSeconds;
            existing.MaxSimultaneousVehicles = input.MaxSimultaneousVehicles;
            existing.ScooptramCount = input.ScooptramCount;
            existing.IsEnabled = input.IsEnabled;

            await _dbContext.SaveChangesAsync();
            return Ok(existing);
        }

        /// <summary>Toggle rápido del bot</summary>
        [HttpPost("{serverId}/toggle")]
        public async Task<IActionResult> Toggle(int serverId, [FromBody] bool enabled)
        {
            var config = await _dbContext.RethinkBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null)
            {
                return BadRequest(new { message = "Configura primero el host de RethinkDB antes de activar el bot." });
            }

            if (enabled && string.IsNullOrWhiteSpace(config.RethinkHost))
            {
                return BadRequest(new { message = "Configura el host de RethinkDB antes de activar el bot." });
            }

            config.IsEnabled = enabled;
            await _dbContext.SaveChangesAsync();
            return Ok(new { isEnabled = config.IsEnabled });
        }

        /// <summary>Estado actual del bot con último log</summary>
        [HttpGet("{serverId}/status")]
        public async Task<IActionResult> GetStatus(int serverId, [FromServices] LogHistoryService logHistory)
        {
            var config = await _dbContext.RethinkBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            var logs = logHistory.GetLogs(serverId);
            var lastRethinkLog = logs?
                .Where(l => (l.Message ?? "").Contains("[RethinkBot]"))
                .OrderByDescending(l => l.Timestamp)
                .FirstOrDefault();

            return Ok(new
            {
                isEnabled = config?.IsEnabled ?? false,
                host = config?.RethinkHost ?? "",
                port = config?.RethinkPort ?? 28015,
                lastLog = lastRethinkLog?.Message?.Replace("[RethinkBot] ", "") ?? null,
                lastLogTime = lastRethinkLog?.Timestamp
            });
        }
    }
}
