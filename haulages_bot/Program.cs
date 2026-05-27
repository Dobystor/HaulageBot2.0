using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;
using Hangfire;
using Hangfire.Storage.SQLite;
using haulages_bot.Services;
using haulages_bot.Hubs;
using Newtonsoft.Json;
using haulages_bot.Models;

using System.Net.Http;
using System.Net.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

// Agregar servicios al contenedor.
builder.Services.AddControllersWithViews();
// Registrar HttpClient con bypass de verificación SSL para IPs industriales
// Registrar HttpClient con bypass de verificación SSL para IPs industriales
builder.Services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => {
    var handler = new HttpClientHandler();
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
    return handler;
});

// Registrar servicios específicos de la aplicación.
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<DataSyncManualService>();
builder.Services.AddScoped<DataSyncJobManual>();
builder.Services.AddScoped<BootConfigurationService>();
builder.Services.AddSingleton<DataSyncService>();
builder.Services.AddSingleton<DataSyncJobService>();
builder.Services.AddScoped<DataHistoricsService>();
builder.Services.AddSingleton<LogHistoryService>();

// Habilitar SignalR para la comunicación en tiempo real.
builder.Services.AddSignalR();

// Agregar DbContext.
builder.Services.AddDbContext<dbboot>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
Console.WriteLine($"DefaultConnection string: {builder.Configuration.GetConnectionString("DefaultConnection")}");

// Configuración de logging.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

bool isDatabaseAvailable = false;

// Probar la conexión con la base de datos y aplicar migraciones si es posible.
using (var serviceProvider = builder.Services.BuildServiceProvider())
{
    using (var scope = serviceProvider.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<dbboot>();
        int retries = 5; // Número de reintentos permitidos.
        int delayBetweenRetries = 5000; // Tiempo entre reintentos en milisegundos.

        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (dbContext.Database.CanConnect())
                {
                    Console.WriteLine("Conexión a la base de datos exitosa.");
                    isDatabaseAvailable = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Intento {i + 1} fallido: {ex.Message}");
            }

            Thread.Sleep(delayBetweenRetries);
        }

        if (isDatabaseAvailable)
        {
            Console.WriteLine("Aplicando migraciones...");
            dbContext.Database.Migrate();

            if (!dbContext.DataConfigurationLocal.Any())
            {
                Console.WriteLine("Insertando datos iniciales...");
                dbContext.DataConfigurationLocal.Add(new DataConfigurationLocal
                {
                    TonnageVariation = JsonConvert.SerializeObject(new List<int> { 10, 20 }),
                    Time = JsonConvert.SerializeObject(new List<int> { 5, 15 }),
                    SelectedRoutes = JsonConvert.SerializeObject(new List<int> { 1, 2, 3 }),
                    SelectedEmployees = JsonConvert.SerializeObject(new List<int> { 101, 102 }),
                    SelectedVehicles = JsonConvert.SerializeObject(new List<int> { 201, 202 })
                });
                dbContext.SaveChanges();
            }
        }
        else
        {
            Console.WriteLine("No se pudo establecer conexión con la base de datos después de varios intentos.");
        }
    }
}

// Configurar servicios en segundo plano siempre, independientemente de Hangfire.
builder.Services.AddHostedService<DataSyncService>();
builder.Services.AddHostedService<DataSyncJobService>(provider => provider.GetRequiredService<DataSyncJobService>());

// Configurar Hangfire solo si la base de datos está disponible.
if (isDatabaseAvailable)
{
    builder.Services.AddHangfire(config =>
    {
        config.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
              .UseSimpleAssemblyNameTypeSerializer()
              .UseRecommendedSerializerSettings()
              .UseSQLiteStorage(builder.Configuration.GetConnectionString("DefaultConnection"));
    });

    builder.Services.AddHangfireServer();
    Console.WriteLine("Hangfire configurado y servidor iniciado.");
}
else
{
    Console.WriteLine("Base de datos no disponible. Hangfire no se configurará.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", builder => builder
        .SetIsOriginAllowed(origin => true)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()
    );
});

var app = builder.Build();

// Configuración del pipeline de middleware.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

if (isDatabaseAvailable)
{
    app.UseHangfireDashboard("/hangfire");
    Console.WriteLine("Dashboard de Hangfire habilitado.");
}

// Rutas de la aplicación.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Authentication}/{action=Login}/{id?}");

app.MapControllerRoute(
    name: "auth",
    pattern: "{controller=Authentication}/{action=Login}/{id?}");

app.MapControllerRoute(
    name: "import",
    pattern: "{controller=Import}/{action=Import}/{id?}");

// Mapea cualquier ruta adicional que se haya definido.
app.MapControllerRoute(
    name: "dashboard",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "sync",
    pattern: "{controller=Sync}/{action=Start}/{id?}");

app.MapHub<NotificationHub>("/notificationHub");

try
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<dbboot>();

        if (!dbContext.Database.CanConnect())
        {
            Console.WriteLine("Base de datos no encontrada. Creándola automáticamente...");
        }

        Console.WriteLine("Aplicando migraciones...");
        dbContext.Database.Migrate();
        Console.WriteLine("Migraciones aplicadas correctamente o la base de datos ya estaba actualizada.");
    }

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine("Ocurrió un error durante el inicio de la aplicación.", ex);
}
