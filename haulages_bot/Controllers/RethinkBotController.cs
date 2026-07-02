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

        /// <summary>Limpiar toda la tabla HaulageProcess en RethinkDB</summary>
        [HttpPost("{serverId}/clear")]
        public async Task<IActionResult> ClearData(int serverId)
        {
            var config = await _dbContext.RethinkBotConfigs
                .FirstOrDefaultAsync(c => c.ServerConfigId == serverId);

            if (config == null || string.IsNullOrWhiteSpace(config.RethinkHost))
                return BadRequest(new { message = "Configura el host de RethinkDB primero." });

            var baseUrl = $"https://{config.RethinkHost}:{config.RethinkPort}";

            try
            {
                // ReQL: r.db('SmartFlow').table('HaulageProcess').delete()
                var reql = "[1,[54,[[15,[[14,[\"SmartFlow\"]],\"HaulageProcess\"]]]],{\"binary_format\":\"raw\",\"time_format\":\"raw\",\"profile\":false}]";

                // Obtener conn_id
                var psi1 = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/curl",
                    Arguments = $"-sk -X POST {baseUrl}/ajax/reql/open-new-connection",
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                };
                string connId;
                using (var p = System.Diagnostics.Process.Start(psi1))
                {
                    if (p == null) return StatusCode(500, new { message = "No se pudo ejecutar curl" });
                    connId = (await p.StandardOutput.ReadToEndAsync()).Trim().Trim('"');
                    await p.WaitForExitAsync();
                }

                if (string.IsNullOrWhiteSpace(connId))
                    return StatusCode(500, new { message = "No se pudo obtener conn_id" });

                // Escribir payload
                var tmpFile = $"/tmp/reql_clear_{serverId}.bin";
                var queryBytes = System.Text.Encoding.UTF8.GetBytes(reql);
                var payload = new byte[8 + queryBytes.Length];
                System.BitConverter.GetBytes(1L).CopyTo(payload, 0);
                queryBytes.CopyTo(payload, 8);
                System.IO.File.WriteAllBytes(tmpFile, payload);

                // Enviar
                var psi2 = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/curl",
                    Arguments = $"-sk -X POST \"{baseUrl}/ajax/reql/?conn_id={connId}\" -H \"Content-Type: application/octet-stream\" --data-binary @{tmpFile}",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi2))
                {
                    if (p != null)
                    {
                        await p.StandardOutput.ReadToEndAsync();
                        await p.WaitForExitAsync();
                    }
                    try { System.IO.File.Delete(tmpFile); } catch { }
                }

                return Ok(new { message = "Tabla HaulageProcess limpiada." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al limpiar", detail = ex.Message });
            }
        }
    }
}
