using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using haulages_bot.Data;
using haulages_bot.Models;
using haulages_bot.Controllers;
using haulages_bot.Services;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Linq;

public class DataSyncService : IHostedService, IDisposable
{
    private readonly ILogger<DataSyncService> _logger;
    private Timer _timer;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public DataSyncService(ILogger<DataSyncService> logger, IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Data Sync Service started.");
        // Ejecutar cada 15 minutos
        _timer = new Timer(SyncData, null, TimeSpan.Zero, TimeSpan.FromMinutes(15));
        return Task.CompletedTask;
    }

    public async void SyncData(object state)
    {
        _logger.LogInformation("Iniciando sincronización periódica de catálogos para todos los servidores...");

        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<dbboot>();
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
            try
            {
                var servers = await dbContext.ServerConfigs.Where(s => s.IsActive).ToListAsync();
                foreach (var server in servers)
                {
                    if (!server.IsSyncEnabledLocal)
                    {
                        _logger.LogInformation($"Sincronización de catálogos deshabilitada para el servidor '{server.Name}'.");
                        continue;
                    }

                    _logger.LogInformation($"Iniciando sincronización de catálogos para el servidor '{server.Name}'...");
                    await SyncDataForServer(dbContext, server, tokenService);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error general en SyncData: {ex.Message}");
            }
        }
    }

    public async Task SyncDataForServer(dbboot dbContext, ServerConfig server, TokenService tokenService)
    {
        try
        {
            var token = await tokenService.GetTokenAsync(server.Id);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError($"Token no disponible para servidor '{server.Name}'. Sincronización cancelada.");
                return;
            }

            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";

            // Sincronizar VehicleType
            var vehicleTypeUrl = $"{host}/service/catalog/api/v1/vehicletypes/all";
            var vehicleTypes = await GetDataFromApi<List<VehicleType>>(vehicleTypeUrl, token);
            await SyncEntity(dbContext, vehicleTypes, server.Id, vt => vt.VehicleTypeId, vt => vt);

            // Sincronizar Company
            var companyUrl = $"{host}/service/catalog/api/v1/companies/all";
            var companies = await GetDataFromApi<List<Company>>(companyUrl, token);
            await SyncEntity(dbContext, companies, server.Id, c => c.CompanyId, c => c);

            // Sincronizar Shift
            var workShiftsUrl = $"{host}/service/catalog/api/v1/WorkShifts/all";
            var workShifts = await GetDataFromApi<List<Shift>>(workShiftsUrl, token);
            await SyncEntity(dbContext, workShifts, server.Id, w => w.WorkShiftId, w => w);

            // Sincronizar Material
            var materialTypesUrl = $"{host}/service/haulages/api/v2/materialtypes/all";
            var materialTypes = await GetDataFromApi<List<Material>>(materialTypesUrl, token);
            await SyncEntity(dbContext, materialTypes, server.Id, m => m.materialTypeId, m => m);

            // Sincronizar Route
            var haulagePathsUrl = $"{host}/service/haulages/api/v2/haulagepaths/all";
            var haulagePaths = await GetDataFromApi<List<haulages_bot.Models.Route>>(haulagePathsUrl, token);
            var materialDict = materialTypes?.ToDictionary(m => m.materialTypeId, m => m.name) ?? new Dictionary<int, string>();
            foreach (var route in haulagePaths)
            {
                if (route.materialTypeId.HasValue && materialDict.TryGetValue(route.materialTypeId.Value, out var matName))
                {
                    route.materialType = matName;
                }
            }
            await SyncEntity(dbContext, haulagePaths, server.Id, r => r.haulagePathId, r => r);

            // Sincronizar Employees
            await SyncEmployees(dbContext, server, token);

            // Sincronizar Vehicles
            await SyncVehicles(dbContext, server, token);

            _logger.LogInformation($"Sincronización de catálogos completada exitosamente para el servidor '{server.Name}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al sincronizar catálogos del servidor '{server.Name}': {ex.Message}");
        }
    }

    private async Task<T> GetDataFromApi<T>(string url, string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error al obtener datos de la API: {response.StatusCode} - {responseString}");
            }

            return JsonConvert.DeserializeObject<T>(responseString);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al llamar a la API {url}: {ex.Message}");
            throw;
        }
    }

    public async Task SyncEntity<T>(dbboot dbContext, IEnumerable<T> entities, int serverId, Func<T, int> getId, Func<T, T> createEntity) where T : class
    {
        string tableName = typeof(T).Name;
        if (TableNameExceptions.ContainsKey(tableName))
        {
            tableName = TableNameExceptions[tableName];
        }

        bool isSqlServer = dbContext.Database.IsSqlServer();
        if (!isSqlServer)
        {
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        }

        using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                var incomingIds = entities.Select(getId).ToList();

                // Limpieza de entidades obsoletas antes de guardar las nuevas
                if (typeof(T) == typeof(haulages_bot.Models.Route))
                {
                    var haulagesToNullify = await dbContext.Haulages
                        .Where(h => h.ServerConfigId == serverId && h.PathId != null && !incomingIds.Contains(h.PathId.Value))
                        .ToListAsync();
                    foreach (var h in haulagesToNullify)
                    {
                        h.PathId = null;
                    }
                    if (haulagesToNullify.Any())
                    {
                        await dbContext.SaveChangesAsync();
                    }

                    var routesToDelete = await dbContext.Routes
                        .Where(r => r.ServerConfigId == serverId && !incomingIds.Contains(r.haulagePathId))
                        .ToListAsync();
                    if (routesToDelete.Any())
                    {
                        dbContext.Routes.RemoveRange(routesToDelete);
                        await dbContext.SaveChangesAsync();
                    }

                    var config = await dbContext.DataConfigurationLocal.FirstOrDefaultAsync(c => c.ServerConfigId == serverId);
                    if (config != null && !string.IsNullOrEmpty(config.SelectedRoutes))
                    {
                        try
                        {
                            var selectedRoutesList = JsonConvert.DeserializeObject<List<int>>(config.SelectedRoutes) ?? new List<int>();
                            var updatedList = selectedRoutesList.Where(id => incomingIds.Contains(id)).ToList();
                            config.SelectedRoutes = JsonConvert.SerializeObject(updatedList);
                            await dbContext.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"No se pudo limpiar la configuración de rutas seleccionadas para el servidor {serverId}: {ex.Message}");
                        }
                    }
                }
                else if (typeof(T) == typeof(Material))
                {
                    var haulagesToNullify = await dbContext.Haulages
                        .Where(h => h.ServerConfigId == serverId && h.materialTypeId != null && !incomingIds.Contains(h.materialTypeId.Value))
                        .ToListAsync();
                    foreach (var h in haulagesToNullify)
                    {
                        h.materialTypeId = null;
                    }
                    if (haulagesToNullify.Any())
                    {
                        await dbContext.SaveChangesAsync();
                    }

                    var materialsToDelete = await dbContext.Materials
                        .Where(m => m.ServerConfigId == serverId && !incomingIds.Contains(m.materialTypeId))
                        .ToListAsync();
                    if (materialsToDelete.Any())
                    {
                        dbContext.Materials.RemoveRange(materialsToDelete);
                        await dbContext.SaveChangesAsync();
                    }
                }
                else if (typeof(T) == typeof(Company))
                {
                    var companiesToDelete = await dbContext.Companies
                        .Where(c => c.ServerConfigId == serverId && !incomingIds.Contains(c.CompanyId))
                        .ToListAsync();
                    if (companiesToDelete.Any())
                    {
                        dbContext.Companies.RemoveRange(companiesToDelete);
                        await dbContext.SaveChangesAsync();
                    }
                }
                else if (typeof(T) == typeof(Shift))
                {
                    var haulagesToNullify = await dbContext.Haulages
                        .Where(h => h.ServerConfigId == serverId && h.ShiftId != null && !incomingIds.Contains(h.ShiftId.Value))
                        .ToListAsync();
                    foreach (var h in haulagesToNullify)
                    {
                        h.ShiftId = null;
                    }
                    if (haulagesToNullify.Any())
                    {
                        await dbContext.SaveChangesAsync();
                    }

                    var shiftsToDelete = await dbContext.Shifts
                        .Where(s => s.ServerConfigId == serverId && !incomingIds.Contains(s.WorkShiftId))
                        .ToListAsync();
                    if (shiftsToDelete.Any())
                    {
                        dbContext.Shifts.RemoveRange(shiftsToDelete);
                        await dbContext.SaveChangesAsync();
                    }
                }

                if (isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {tableName} ON;");
                }

                foreach (var entity in entities)
                {
                    var prop = typeof(T).GetProperty("ServerConfigId");
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(entity, serverId);
                    }

                    var existingEntity = await dbContext.Set<T>().FindAsync(getId(entity), serverId);

                    if (existingEntity == null)
                    {
                        var newEntity = createEntity(entity);
                        if (prop != null && prop.CanWrite)
                        {
                            prop.SetValue(newEntity, serverId);
                        }
                        await dbContext.Set<T>().AddAsync(newEntity);
                    }
                    else
                    {
                        dbContext.Entry(existingEntity).CurrentValues.SetValues(entity);
                    }
                    await dbContext.SaveChangesAsync();
                }

                if (isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {tableName} OFF;");
                }
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al sincronizar la entidad {typeof(T).Name}: {ex.Message}");
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                if (!isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
                }
            }
        }
    }

    private static readonly Dictionary<string, string> TableNameExceptions = new Dictionary<string, string>
    {
        { "Company", "Companies" },
        { "Shift", "Shifts" },
        { "Route", "Routes" },
        { "Employee", "Employees" },
        { "Vehicle", "Vehicles" },
        { "Material", "Materials" },
    };

    private async Task SyncEmployees(dbboot dbContext, ServerConfig server, string token)
    {
        var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
        var employeesUrl = $"{host}/service/haulages/api/v1/GeneralSettings/employees/all";
        var employees = await GetDataFromApi<List<EmployeeMapping>>(employeesUrl, token);

        if (employees == null || employees.Count == 0)
        {
            _logger.LogError("No se encontraron empleados en la respuesta de la API.");
            return;
        }

        var filteredEmployees = employees;

        using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                bool isSqlServer = dbContext.Database.IsSqlServer();

                // Limpieza de empleados obsoletos (desvinculando históricos si existen)
                var incomingIds = filteredEmployees.Select(e => e.EmployeeId).ToList();

                var haulagesToNullify = await dbContext.Haulages
                    .Where(h => h.ServerConfigId == server.Id && h.EmployeeId != null && !incomingIds.Contains(h.EmployeeId.Value))
                    .ToListAsync();
                foreach (var h in haulagesToNullify)
                {
                    h.EmployeeId = null;
                }
                if (haulagesToNullify.Count > 0)
                {
                    await dbContext.SaveChangesAsync();
                }

                var programmingRecordsToDelete = await dbContext.ProgrammingRecords
                    .Where(pr => pr.ServerConfigId == server.Id && !incomingIds.Contains(pr.EmployeeId))
                    .ToListAsync();
                if (programmingRecordsToDelete.Count > 0)
                {
                    dbContext.ProgrammingRecords.RemoveRange(programmingRecordsToDelete);
                    await dbContext.SaveChangesAsync();
                }

                var employeesToDelete = await dbContext.Employees
                    .Where(e => e.ServerConfigId == server.Id && !incomingIds.Contains(e.EmployeeId))
                    .ToListAsync();

                if (employeesToDelete.Count > 0)
                {
                    dbContext.Employees.RemoveRange(employeesToDelete);
                    await dbContext.SaveChangesAsync();
                }

                if (isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Employees ON;");
                }

                foreach (var employee in filteredEmployees)
                {
                    var existingEmployee = await dbContext.Employees.AsNoTracking()
                        .FirstOrDefaultAsync(e => e.EmployeeId == employee.EmployeeId && e.ServerConfigId == server.Id);

                    decimal.TryParse(employee.NoEmployee, out var noEmpDec);
                    if (existingEmployee == null)
                    {
                        var newEmployee = new Employee
                        {
                            EmployeeId = employee.EmployeeId,
                            NoEmployee = noEmpDec,
                            Name = employee.Name,
                            PaternalLastName = employee.PaternalLastName,
                            MaternalLastName = string.IsNullOrEmpty(employee.MaternalLastName) ? string.Empty : employee.MaternalLastName,
                            FullName = $"{employee.Name} {employee.PaternalLastName} {employee.MaternalLastName}",
                            CompanyId = employee.CompanyId,
                            ServerConfigId = server.Id
                        };

                        await dbContext.Employees.AddAsync(newEmployee);
                    }
                    else
                    {
                        var updatedEmployee = new Employee
                        {
                            EmployeeId = employee.EmployeeId,
                            NoEmployee = noEmpDec,
                            Name = employee.Name,
                            PaternalLastName = employee.PaternalLastName,
                            MaternalLastName = string.IsNullOrEmpty(employee.MaternalLastName) ? string.Empty : employee.MaternalLastName,
                            FullName = $"{employee.Name} {employee.PaternalLastName} {employee.MaternalLastName}",
                            CompanyId = employee.CompanyId,
                            ServerConfigId = server.Id
                        };

                        dbContext.Employees.Update(updatedEmployee);
                    }
                    await dbContext.SaveChangesAsync();
                }

                if (isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Employees OFF;");
                }
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al sincronizar los empleados: {ex.Message}");
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    private async Task SyncVehicles(dbboot dbContext, ServerConfig server, string token)
    {
        var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
        var vehiclesUrl = $"{host}/service/haulages/api/v2/generalsettings/vehicles/all";
        var vehicles = await GetDataFromApi<List<Vehicle>>(vehiclesUrl, token);

        if (vehicles == null || vehicles.Count == 0)
        {
            _logger.LogError("No se encontraron vehículos en la respuesta de la API.");
            return;
        }

        // Desactivar FK checks en SQLite para evitar violaciones por orden de inserción
        bool isSqlServer = dbContext.Database.IsSqlServer();
        if (!isSqlServer)
        {
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        }

        using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                // Limpieza de vehículos obsoletos (desvinculando históricos si existen)
                var incomingVehicleIds = vehicles.Select(v => v.VehicleId).ToList();

                var vehicleHaulagesToNullify = await dbContext.Haulages
                    .Where(h => h.ServerConfigId == server.Id && h.VehicleId != null && !incomingVehicleIds.Contains(h.VehicleId.Value))
                    .ToListAsync();
                foreach (var h in vehicleHaulagesToNullify)
                {
                    h.VehicleId = null;
                }
                if (vehicleHaulagesToNullify.Count > 0)
                {
                    await dbContext.SaveChangesAsync();
                }

                var vehiclesToDelete = await dbContext.Vehicles
                    .Where(v => v.ServerConfigId == server.Id && !incomingVehicleIds.Contains(v.VehicleId))
                    .ToListAsync();

                if (vehiclesToDelete.Count > 0)
                {
                    dbContext.Vehicles.RemoveRange(vehiclesToDelete);
                    await dbContext.SaveChangesAsync();
                }

                if (isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Vehicles ON;");
                }

                foreach (var vehicle in vehicles)
                {
                    var existingVehicle = await dbContext.Vehicles.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.VehicleId == vehicle.VehicleId && v.ServerConfigId == server.Id);

                    if (existingVehicle == null)
                    {
                        var newVehicle = new Vehicle
                        {
                            VehicleId = vehicle.VehicleId,
                            Plates = vehicle.Plates ?? string.Empty,
                            EconomicNumber = vehicle.EconomicNumber ?? string.Empty,
                            CompanyId = vehicle.CompanyId,
                            Model = vehicle.Model ?? string.Empty,
                            EmptyWeight = vehicle.EmptyWeight,
                            FuelTankCapacity = vehicle.FuelTankCapacity,
                            Weight = vehicle.Weight,
                            VehicleTypeId = vehicle.VehicleTypeId,
                            Description = vehicle.Description ?? string.Empty,
                            LoadingCapacity = vehicle.LoadingCapacity,
                            ServerConfigId = server.Id
                        };

                        await dbContext.Vehicles.AddAsync(newVehicle);
                    }
                    else
                    {
                        var updatedVehicle = new Vehicle
                        {
                            VehicleId = vehicle.VehicleId,
                            Plates = vehicle.Plates ?? string.Empty,
                            EconomicNumber = vehicle.EconomicNumber ?? string.Empty,
                            CompanyId = vehicle.CompanyId,
                            Model = vehicle.Model ?? string.Empty,
                            EmptyWeight = vehicle.EmptyWeight,
                            FuelTankCapacity = vehicle.FuelTankCapacity,
                            Weight = vehicle.Weight,
                            VehicleTypeId = vehicle.VehicleTypeId,
                            Description = vehicle.Description ?? string.Empty,
                            LoadingCapacity = vehicle.LoadingCapacity,
                            ServerConfigId = server.Id
                        };

                        dbContext.Vehicles.Update(updatedVehicle);
                    }
                    await dbContext.SaveChangesAsync();
                }

                if (isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Vehicles OFF;");
                }
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al sincronizar los vehículos: {ex.Message}");
                await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                // Re-activar FK checks en SQLite
                if (!isSqlServer)
                {
                    await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Data Sync Service stopped.");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
