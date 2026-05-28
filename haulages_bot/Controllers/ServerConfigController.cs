using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;
using haulages_bot.Models;
using haulages_bot.Services;
using RestSharp;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Security;
using Microsoft.Extensions.Configuration;

namespace haulages_bot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServerConfigController : ControllerBase
    {
        private readonly dbboot _dbContext;
        private readonly IConfiguration _configuration;

        public ServerConfigController(dbboot dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> GetServers()
        {
            var servers = await _dbContext.ServerConfigs.ToListAsync();
            return Ok(servers);
        }

        // Devuelve el offset de zona horaria efectivo para un servidor dado.
        // Si el servidor tiene configurado un offset propio, se usa ese; si no, el global de appsettings.
        [HttpGet("timezone")]
        public async Task<IActionResult> GetTimezone([FromQuery] int? serverId)
        {
            int globalOffset = _configuration.GetValue<int>("TimezoneOffsetHours", 0);
            
            if (serverId.HasValue)
            {
                var server = await _dbContext.ServerConfigs.FindAsync(serverId.Value);
                if (server != null && server.TimezoneOffsetHours.HasValue)
                {
                    return Ok(new { offsetHours = server.TimezoneOffsetHours.Value, source = "server" });
                }
            }
            
            return Ok(new { offsetHours = globalOffset, source = "global" });
        }

        // Actualiza el offset de zona horaria de un servidor específico.
        [HttpPut("{id}/timezone")]
        public async Task<IActionResult> UpdateTimezone(int id, [FromBody] TimezoneUpdateDto dto)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(id);
            if (server == null) return NotFound();

            server.TimezoneOffsetHours = dto.OffsetHours; // null = usar global
            await _dbContext.SaveChangesAsync();
            
            int effectiveOffset = dto.OffsetHours ?? _configuration.GetValue<int>("TimezoneOffsetHours", 0);
            return Ok(new { offsetHours = effectiveOffset });
        }

        [HttpPost]
        public async Task<IActionResult> AddServer([FromBody] ServerConfig server)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            _dbContext.ServerConfigs.Add(server);
            await _dbContext.SaveChangesAsync();
            return Ok(server);
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ConnectServer([FromBody] ServerConfig server)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var options = new RestClientOptions(host)
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
                Timeout = TimeSpan.FromSeconds(15)
            };
            var client = new RestClient(options);
            var request = new RestRequest("/api/openid/connect/token", Method.Post);
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("grant_type", "password");
            request.AddParameter("username", server.Username);
            request.AddParameter("password", server.Password);
            request.AddParameter("client_id", server.ClientId);
            request.AddParameter("client_secret", server.ClientSecret);
            request.AddParameter("scope", "smartflow IdentityServerApi offline_access");

            try
            {
                var response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    var errorMsg = response.ErrorMessage;
                    try
                    {
                        var errObj = JsonConvert.DeserializeObject<dynamic>(response.Content);
                        if (errObj != null && errObj.error_description != null)
                        {
                            errorMsg = errObj.error_description;
                        }
                    }
                    catch { }
                    return BadRequest(new { success = false, message = $"Fallo al conectar al servidor: {errorMsg}" });
                }

                var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(response.Content);
                if (tokenResponse != null)
                {
                    server.AccessToken = tokenResponse.AccessToken;
                    server.RefreshToken = tokenResponse.RefreshToken;
                    server.TokenExpiry = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Excepción al conectar: {ex.Message}" });
            }

            _dbContext.ServerConfigs.Add(server);
            await _dbContext.SaveChangesAsync();

            // Crear configuración de bot inicial para este servidor
            var existingConfig = await _dbContext.DataConfigurationLocal.AnyAsync(dc => dc.ServerConfigId == server.Id);
            if (!existingConfig)
            {
                _dbContext.DataConfigurationLocal.Add(new DataConfigurationLocal
                {
                    ServerConfigId = server.Id,
                    TonnageVariation = JsonConvert.SerializeObject(new List<int> { 10, 20 }),
                    Time = JsonConvert.SerializeObject(new List<int> { 5, 15 }),
                    SelectedRoutes = JsonConvert.SerializeObject(new List<int>()),
                    SelectedEmployees = JsonConvert.SerializeObject(new List<int>()),
                    SelectedVehicles = JsonConvert.SerializeObject(new List<int>())
                });
                await _dbContext.SaveChangesAsync();
            }

            // Sincronizar catálogo inicial en segundo plano
            _ = Task.Run(async () =>
            {
                try
                {
                    using (var scope = HttpContext.RequestServices.CreateScope())
                    {
                        var syncService = scope.ServiceProvider.GetRequiredService<DataSyncManualService>();
                        await syncService.SyncData(server.Id);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error en sincronización inicial para servidor ID {server.Id}: {ex.Message}");
                }
            });

            return Ok(server);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateServer(int id, [FromBody] ServerConfig updatedServer)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(id);
            if (server == null) return NotFound();

            server.Name = updatedServer.Name;
            server.ApiUrl = updatedServer.ApiUrl;
            // Solo actualizar ClientId/ClientSecret si vienen con valor; de lo contrario conservar los existentes
            if (!string.IsNullOrWhiteSpace(updatedServer.ClientId))
                server.ClientId = updatedServer.ClientId;
            if (!string.IsNullOrWhiteSpace(updatedServer.ClientSecret))
                server.ClientSecret = updatedServer.ClientSecret;
            if (!string.IsNullOrWhiteSpace(updatedServer.Username))
                server.Username = updatedServer.Username;
            if (!string.IsNullOrWhiteSpace(updatedServer.Password))
                server.Password = updatedServer.Password;
            server.IsActive = updatedServer.IsActive;

            await _dbContext.SaveChangesAsync();
            return Ok(server);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteServer(int id)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(id);
            if (server == null) return NotFound();

            // Eliminar registros asociados en cascada
            var configs = _dbContext.DataConfigurationLocal.Where(c => c.ServerConfigId == id);
            _dbContext.DataConfigurationLocal.RemoveRange(configs);

            var haulages = _dbContext.Haulages.Where(h => h.ServerConfigId == id);
            _dbContext.Haulages.RemoveRange(haulages);

            _dbContext.ServerConfigs.Remove(server);
            await _dbContext.SaveChangesAsync();
            return Ok();
        }

        private class TokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; }

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }

    public class TimezoneUpdateDto
    {
        // null = usar el valor global de appsettings.json
        public int? OffsetHours { get; set; }
    }
}
