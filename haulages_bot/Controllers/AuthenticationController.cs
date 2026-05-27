using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using haulages_bot.Services;
using haulages_bot.Models;
using haulages_bot.Data;
using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class AuthenticationController : Controller
{
    private readonly AuthenticationService _authenticationService;
    private readonly TokenService _tokenService;
    private readonly dbboot _dbContext;

    public AuthenticationController(AuthenticationService authenticationService, TokenService tokenService, dbboot dbContext)
    {
        _authenticationService = authenticationService;
        _tokenService = tokenService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Login()
    {
        var authToken = HttpContext.Request.Cookies["AuthToken"];
        if (!string.IsNullOrEmpty(authToken))
        {
            return RedirectToAction("gene", "Home");
        }

        // Obtener los servidores registrados en SQLite
        List<ServerConfig> servers = new List<ServerConfig>();
        try
        {
            servers = await _dbContext.ServerConfigs.ToListAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al consultar servidores para login: {ex.Message}");
        }

        ViewBag.Servers = servers;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string serverUrl, string username, string password, string? serverName = null)
    {
        Console.WriteLine($"[Login Controller] Recibido POST. serverUrl='{serverUrl}', username='{username}', serverName='{serverName}'");
        if (string.IsNullOrEmpty(serverUrl))
        {
            TempData["ErrorMessage"] = "Debes ingresar o seleccionar un servidor.";
            return RedirectToAction("Login");
        }

        // Autenticar contra el servidor seleccionado
        var tokenResponse = await _authenticationService.AuthenticateAsync(serverUrl, username, password);

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.access_token))
        {
            TempData["ErrorMessage"] = "Intento de inicio de sesión no válido en el servidor especificado.";
            return RedirectToAction("Login");
        }

        // Buscar si ya existe el servidor configurado en la base de datos local
        var server = await _dbContext.ServerConfigs.FirstOrDefaultAsync(s => s.ApiUrl.ToLower() == serverUrl.ToLower());
        
        if (server == null)
        {
            // Registrar nuevo servidor
            server = new ServerConfig
            {
                Name = !string.IsNullOrEmpty(serverName) ? serverName : serverUrl,
                ApiUrl = serverUrl,
                ClientId = "smartflow.csharp.client",
                ClientSecret = "secret",
                IsActive = true,
                Username = username,
                Password = password,
                AccessToken = tokenResponse.access_token,
                RefreshToken = tokenResponse.refresh_token,
                TokenExpiry = DateTime.Now.AddSeconds(tokenResponse.expires_in),
                IsBotRunning = false,
                IsSyncEnabledLocal = false
            };

            _dbContext.ServerConfigs.Add(server);
            await _dbContext.SaveChangesAsync();

            // Configuración del bot inicial para el servidor registrado
            var existingConfig = await _dbContext.DataConfigurationLocal.AnyAsync(dc => dc.ServerConfigId == server.Id);
            if (!existingConfig)
            {
                _dbContext.DataConfigurationLocal.Add(new DataConfigurationLocal
                {
                    ServerConfigId = server.Id,
                    TonnageVariation = Newtonsoft.Json.JsonConvert.SerializeObject(new List<int> { 10, 20 }),
                    Time = Newtonsoft.Json.JsonConvert.SerializeObject(new List<int> { 5, 15 }),
                    SelectedRoutes = Newtonsoft.Json.JsonConvert.SerializeObject(new List<int>()),
                    SelectedEmployees = Newtonsoft.Json.JsonConvert.SerializeObject(new List<int>()),
                    SelectedVehicles = Newtonsoft.Json.JsonConvert.SerializeObject(new List<int>())
                });
                await _dbContext.SaveChangesAsync();
            }

            // Sincronizar catálogo inicial de forma asíncrona
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
        }
        else
        {
            // Actualizar credenciales y tokens del servidor existente
            server.Username = username;
            server.Password = password;
            server.AccessToken = tokenResponse.access_token;
            server.RefreshToken = tokenResponse.refresh_token;
            server.TokenExpiry = DateTime.Now.AddSeconds(tokenResponse.expires_in);
            
            _dbContext.ServerConfigs.Update(server);
            await _dbContext.SaveChangesAsync();
        }

        // Configurar cookies de sesión
        HttpContext.Response.Cookies.Append("AuthToken", tokenResponse.access_token, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });

        HttpContext.Response.Cookies.Append("TokenType", tokenResponse.token_type, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Strict
        });

        HttpContext.Response.Cookies.Append("Username", username, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Strict
        });

        // Guardar la ID del servidor seleccionado para que gene.js la tome de inicio
        HttpContext.Response.Cookies.Append("ActiveServerId", server.Id.ToString(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            HttpOnly = false, // Permite leerlo desde JavaScript frontend
            Secure = false,
            SameSite = SameSiteMode.Strict
        });

        return RedirectToAction("gene", "Home");
    }

    public IActionResult SomeProtectedAction()
    {
        var authToken = HttpContext.Request.Cookies["AuthToken"];
        if (string.IsNullOrEmpty(authToken))
        {
            return RedirectToAction("Login", "Authentication");  // Redirige al login si no hay token
        }

        // Continuar con la lógica de la acción si el token está presente
        return View();
    }

    [HttpPost]
    public IActionResult Logout()
    {
        // Elimina las cookies de autenticación
        HttpContext.Response.Cookies.Delete("AuthToken");
        HttpContext.Response.Cookies.Delete("TokenType");
        HttpContext.Response.Cookies.Delete("Username");  // También eliminamos la cookie de nombre de usuario si la usamos

        // Redirige al inicio de sesión
        return RedirectToAction("Login", "Authentication");
    }

}
