// Services/AuthenticationService.cs
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using haulages_bot.Models; // Asegúrate de tener el namespace correcto para el modelo AuthResponse
using System.Net.Security;
using System.Collections.Generic;

public class AuthenticationService
{
    private readonly IConfiguration _configuration; // Campo para acceder a las configuraciones de la aplicación
    private readonly RestClient _client; // Cliente RestSharp para realizar solicitudes HTTP

    // Constructor que inyecta las configuraciones de la aplicación
    public AuthenticationService(IConfiguration configuration)
    {
        _configuration = configuration;

        // Configura el cliente RestSharp con la URL base y un timeout infinito
        var options = new RestClientOptions(_configuration["Authentication:ApiUrl"])
        {
            Timeout = Timeout.InfiniteTimeSpan // Sin límite de tiempo para las solicitudes
        };
        _client = new RestClient(options); // Inicializa el cliente RestSharp con las opciones configuradas
    }

    // Método asíncrono para autenticar al usuario y obtener un token contra un servidor específico
    public async Task<AuthResponse> AuthenticateAsync(string serverUrl, string username, string password)
    {
        string rawHost = serverUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
        var hosts = new List<string> { $"https://{rawHost}", $"http://{rawHost}" };

        var clients = new List<(string id, string secret)>
        {
            (_configuration["Authentication:ClientId"] ?? "private.networking.app", _configuration["Authentication:ClientSecret"] ?? "UxwYJsELeTnSc2Zz642K"),
            ("smartflow.csharp.client", "secret")
        };

        Console.WriteLine($"[Auth Service] Iniciando proceso de autenticación. ServerUrl original: '{serverUrl}'. Hosts a probar: {string.Join(", ", hosts)}");

        foreach (var host in hosts)
        {
            foreach (var creds in clients)
            {
                var result = await TryAuthenticateAsync(host, username, password, creds.id, creds.secret);
                if (result != null)
                {
                    Console.WriteLine($"[Auth Success] Autenticado exitosamente en {host} con ClientId: {creds.id}");
                    return result;
                }
            }
        }

        Console.WriteLine($"[Auth Error] No se pudo autenticar en ningún host con ninguna credencial de cliente para: {serverUrl}");
        return null;
    }

    private async Task<AuthResponse> TryAuthenticateAsync(string host, string username, string password, string clientId, string clientSecret)
    {
        var options = new RestClientOptions(host)
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
            Timeout = TimeSpan.FromSeconds(10)
        };
        var client = new RestClient(options);
        
        var request = new RestRequest("/api/openid/connect/token", Method.Post);
        request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
        request.AddParameter("client_id", clientId);
        request.AddParameter("client_secret", clientSecret);
        request.AddParameter("grant_type", "password");
        request.AddParameter("username", username);
        request.AddParameter("password", password);
        request.AddParameter("scope", "smartflow IdentityServerApi offline_access");

        try
        {
            Console.WriteLine($"[Auth Probing] Probando conexión a {host}/api/openid/connect/token con ClientId: {clientId}");
            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            {
                var authRes = JsonSerializer.Deserialize<AuthResponse>(response.Content);
                if (authRes != null && !string.IsNullOrEmpty(authRes.access_token))
                {
                    return authRes;
                }
            }
            else
            {
                Console.WriteLine($"[Auth Try Failed] Host: {host}, Client: {clientId}, Status: {response.ResponseStatus}, StatusCode: {response.StatusCode}, Error: {response.ErrorMessage}, Content: {response.Content}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth Try Exception] Host: {host}, Client: {clientId}, Error: {ex.Message}");
        }
        return null;
    }

    // Método asíncrono para refrescar el token usando un refresh token
    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        // Configura la solicitud HTTP POST para refrescar el token de autenticación
        var request = new RestRequest("/api/openid/connect/token", Method.Post);
        request.AddHeader("Content-Type", "application/x-www-form-urlencoded"); // Establece el encabezado de tipo de contenido
        request.AddParameter("client_id", _configuration["Authentication:ClientId"]); // Añade el parámetro client_id
        request.AddParameter("client_secret", _configuration["Authentication:ClientSecret"]); // Añade el parámetro client_secret
        request.AddParameter("grant_type", "refresh_token"); // Establece el tipo de grant como "refresh_token"
        request.AddParameter("refresh_token", refreshToken); // Añade el refresh token

        // Ejecuta la solicitud y espera la respuesta
        var response = await _client.ExecuteAsync(request);

        // Verifica si la respuesta fue exitosa
        if (!response.IsSuccessful)
        {
            return null; // Si la solicitud falló, devuelve null
        }

        // Deserializa y devuelve el contenido de la respuesta como un objeto AuthResponse
        return JsonSerializer.Deserialize<AuthResponse>(response.Content);
    }
}
