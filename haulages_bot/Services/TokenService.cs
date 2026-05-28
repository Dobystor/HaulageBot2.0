using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using haulages_bot.Data;
using haulages_bot.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Security;

namespace haulages_bot.Services
{
    public class TokenService
    {
        private readonly dbboot _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TokenService> _logger;

        public TokenService(dbboot context, IConfiguration configuration, ILogger<TokenService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        // Para mantener compatibilidad con logins tradicionales del bot local
        public void SetToken(string token, string refreshToken)
        {
            var server = _context.ServerConfigs.FirstOrDefault(s => s.IsActive);
            if (server != null)
            {
                server.AccessToken = token;
                server.RefreshToken = refreshToken;
                server.TokenExpiry = DateTime.Now.AddMinutes(30);
                _context.ServerConfigs.Update(server);
                _context.SaveChanges();
            }
            else
            {
                var newServer = new ServerConfig
                {
                    Name = "Servidor Local",
                    ApiUrl = _configuration["Authentication:ApiUrl"] ?? "https://demo-acarreos.smartflow.com.mx",
                    ClientId = _configuration["Authentication:ClientId"] ?? "private.networking.app",
                    ClientSecret = _configuration["Authentication:ClientSecret"] ?? "UxwYJsELeTnSc2Zz642K",
                    Username = _configuration["Authentication:Username"] ?? "root",
                    Password = _configuration["Authentication:Password"] ?? "St4rtTheChange.",
                    AccessToken = token,
                    RefreshToken = refreshToken,
                    TokenExpiry = DateTime.Now.AddMinutes(30),
                    IsActive = true
                };
                _context.ServerConfigs.Add(newServer);
                _context.SaveChanges();
            }
        }

        // Obtener token para el servidor por defecto (primer servidor activo)
        public async Task<string> GetTokenAsync()
        {
            var server = await _context.ServerConfigs.FirstOrDefaultAsync(s => s.IsActive);
            if (server == null)
            {
                var newServer = new ServerConfig
                {
                    Name = "Servidor Local",
                    ApiUrl = _configuration["Authentication:ApiUrl"] ?? "https://demo-acarreos.smartflow.com.mx",
                    ClientId = _configuration["Authentication:ClientId"] ?? "private.networking.app",
                    ClientSecret = _configuration["Authentication:ClientSecret"] ?? "UxwYJsELeTnSc2Zz642K",
                    Username = _configuration["Authentication:Username"] ?? "root",
                    Password = _configuration["Authentication:Password"] ?? "St4rtTheChange.",
                    IsActive = true
                };
                _context.ServerConfigs.Add(newServer);
                await _context.SaveChangesAsync();
                server = newServer;
            }

            return await GetTokenAsync(server.Id);
        }

        // Obtener token para un servidor específico
        public async Task<string> GetTokenAsync(int serverId)
        {
            var server = await _context.ServerConfigs.FindAsync(serverId);
            if (server == null)
            {
                throw new Exception($"No se encontró la configuración del servidor con ID: {serverId}");
            }

            var buffer = TimeSpan.FromMinutes(1);
            if (string.IsNullOrEmpty(server.AccessToken) || !server.TokenExpiry.HasValue || DateTime.Now >= server.TokenExpiry.Value.Subtract(buffer))
            {
                _logger.LogInformation($"El token para el servidor '{server.Name}' ha expirado o es nulo. Intentando refrescar...");
                await RefreshOrAcquireTokenAsync(server);
            }

            return server.AccessToken;
        }

        private async Task RefreshOrAcquireTokenAsync(ServerConfig server)
        {
            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var options = new RestClientOptions(host)
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
                Timeout = TimeSpan.FromSeconds(15)
            };
            var client = new RestClient(options);

            // 1. Intentar primero con Refresh Token si está disponible
            if (!string.IsNullOrEmpty(server.RefreshToken))
            {
                try
                {
                    _logger.LogInformation($"Refrescando token para '{server.Name}' usando RefreshToken...");
                    var request = new RestRequest("/api/openid/connect/token", Method.Post);
                    request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
                    request.AddParameter("grant_type", "refresh_token");
                    request.AddParameter("refresh_token", server.RefreshToken);
                    request.AddParameter("client_id", server.ClientId);
                    request.AddParameter("client_secret", server.ClientSecret);

                    var response = await client.ExecuteAsync(request);
                    if (response.IsSuccessful)
                    {
                        var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(response.Content);
                        if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                        {
                            server.AccessToken = tokenResponse.AccessToken;
                            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
                            {
                                server.RefreshToken = tokenResponse.RefreshToken;
                            }
                            server.TokenExpiry = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn);

                            _context.ServerConfigs.Update(server);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Token refrescado exitosamente usando RefreshToken para '{server.Name}'.");
                            return;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Fallo al refrescar token con RefreshToken para '{server.Name}' (invalid_grant o similar). Limpiando RefreshToken fallido e intentando password grant... Error: {response.Content}");
                        // Limpiar refresh token corrupto/expirado para forzar uso de password grant en el siguiente paso
                        server.RefreshToken = null;
                        _context.ServerConfigs.Update(server);
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Excepción al refrescar token con RefreshToken para '{server.Name}': {ex.Message}. Intentando password grant...");
                    server.RefreshToken = null;
                    _context.ServerConfigs.Update(server);
                    await _context.SaveChangesAsync();
                }
            }

            // 2. Si no había RefreshToken o el refresh falló y se limpió, usar Password Grant si hay credenciales
            if (!string.IsNullOrEmpty(server.Username) && !string.IsNullOrEmpty(server.Password))
            {
                _logger.LogInformation($"Adquiriendo nuevo token para '{server.Name}' con usuario/contraseña...");
                var request = new RestRequest("/api/openid/connect/token", Method.Post);
                request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
                request.AddParameter("grant_type", "password");
                request.AddParameter("username", server.Username);
                request.AddParameter("password", server.Password);
                request.AddParameter("scope", "smartflow IdentityServerApi offline_access");
                request.AddParameter("client_id", server.ClientId);
                request.AddParameter("client_secret", server.ClientSecret);

                try
                {
                    var response = await client.ExecuteAsync(request);
                    if (!response.IsSuccessful)
                    {
                        _logger.LogError($"Fallo de autenticación para '{server.Name}': {response.ErrorMessage} - {response.Content}");
                        var detailedError = !string.IsNullOrWhiteSpace(response.ErrorMessage) ? response.ErrorMessage : response.Content;
                        if (string.IsNullOrWhiteSpace(detailedError)) detailedError = response.StatusDescription;
                        throw new Exception($"Error de autenticación en '{server.Name}': {detailedError}");
                    }

                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(response.Content);
                    if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        throw new Exception($"La respuesta de autenticación para '{server.Name}' no devolvió un token.");
                    }

                    server.AccessToken = tokenResponse.AccessToken;
                    if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
                    {
                        server.RefreshToken = tokenResponse.RefreshToken;
                    }
                    server.TokenExpiry = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn);

                    _context.ServerConfigs.Update(server);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Token adquirido con usuario/contraseña y guardado para el servidor '{server.Name}'.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Excepción al adquirir token con usuario/contraseña para el servidor '{server.Name}': {ex.Message}");
                    throw;
                }
            }
            else
            {
                throw new Exception($"No hay credenciales ni refresh token configurados para el servidor '{server.Name}'");
            }
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
}
