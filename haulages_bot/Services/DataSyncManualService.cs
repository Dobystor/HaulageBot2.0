using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net.Http;
using haulages_bot.Controllers;
using haulages_bot.Data;
using haulages_bot.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace haulages_bot.Services
{
    public class DataSyncManualService : Controller
    {
        private readonly ILogger<DataSyncManualService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly TokenService _tokenService;

        public DataSyncManualService(ILogger<DataSyncManualService> logger, IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider, IConfiguration configuration, TokenService tokenService)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _tokenService = tokenService;
        }

        public async Task SyncData(int serverId)
        {            
            _logger.LogInformation($"Starting manual data sync for server ID {serverId}...");

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<dbboot>();
                var server = await dbContext.ServerConfigs.FindAsync(serverId);
                if (server == null)
                {
                    _logger.LogError($"Server config {serverId} not found for manual sync.");
                    throw new Exception("Configuración de servidor no encontrada.");
                }

                try
                {
                    var token = await _tokenService.GetTokenAsync(serverId);
                    if (string.IsNullOrEmpty(token))
                    {
                        _logger.LogError("Token no disponible. Sincronización cancelada.");
                        throw new Exception("Token de autenticación no disponible.");
                    }

                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";

                    // Sincronizar VehicleType
                    var vehiculesTypeUrl = $"{host}/service/catalog/api/v1/vehicletypes/all";
                    var vehiculesType = await GetDataFromApi<List<VehicleType>>(vehiculesTypeUrl);
                    await SyncEntity(dbContext, vehiculesType, serverId, c => c.VehicleTypeId, c => c);

                    // Sincronizar Company
                    var companieUrl = $"{host}/service/catalog/api/v1/companies/all";
                    var companies = await GetDataFromApi<List<Company>>(companieUrl);
                    await SyncEntity(dbContext, companies, serverId, c => c.CompanyId, c => c);
                    _logger.LogInformation("Companies sync completed.");

                    // Sincronizar Turnos de Trabajo
                    var workShiftsUrl = $"{host}/service/catalog/api/v1/WorkShifts/all";
                    var workShifts = await GetDataFromApi<List<Shift>>(workShiftsUrl);
                    await SyncEntity(dbContext, workShifts, serverId, w => w.WorkShiftId, w => w);
                    _logger.LogInformation("Work Shifts sync completed.");

                    // Sincronizar Tipos de Material
                    var materialTypesUrl = $"{host}/service/haulages/api/v2/materialtypes/all";
                    var materialTypes = await GetDataFromApi<List<Material>>(materialTypesUrl);
                    await SyncEntity(dbContext, materialTypes, serverId, m => m.materialTypeId, m => m);
                    _logger.LogInformation("Material Types sync completed.");

                    // Sincronizar Rutas de Acarreo
                    var haulagePathsUrl = $"{host}/service/haulages/api/v2/haulagepaths/all";
                    var haulagePaths = await GetDataFromApi<List<haulages_bot.Models.Route>>(haulagePathsUrl);
                    var materialDict = materialTypes?.ToDictionary(m => m.materialTypeId, m => m.name) ?? new Dictionary<int, string>();
                    foreach (var route in haulagePaths)
                    {
                        if (route.materialTypeId.HasValue && materialDict.TryGetValue(route.materialTypeId.Value, out var matName))
                        {
                            route.materialType = matName;
                        }
                    }
                    await SyncEntity(dbContext, haulagePaths, serverId, v => v.haulagePathId, v => v);
                    _logger.LogInformation("Haulage Paths sync completed.");

                    // Sincronizar Empleados
                    await SyncEmployees(dbContext, server, token);
                    _logger.LogInformation("Employees sync completed.");

                    // Sincronizar Vehículos
                    await SyncVehicles(dbContext, server, token);
                    _logger.LogInformation("Vehicles sync completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"An error occurred while syncing data for server {server.Name}: {ex.Message}");
                    throw;
                }
            }

            _logger.LogInformation("Manual data sync completed.");
        }

        private async Task<T> GetDataFromApi<T>(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Error al obtener datos de la API {url}: {response.StatusCode} - {responseString}");
                    throw new Exception($"Error al obtener datos de la API: {response.StatusCode} - {responseString}");
                }

                return JsonConvert.DeserializeObject<T>(responseString);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener datos de la API: {ex.Message}");
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
            var employees = await GetDataFromApi<List<EmployeeMapping>>(employeesUrl);

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
            var vehicles = await GetDataFromApi<List<Vehicle>>(vehiclesUrl);

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
    }
}
