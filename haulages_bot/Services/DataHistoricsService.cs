using System.Globalization;
using System.Net.Http.Headers;
using haulages_bot.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;

namespace haulages_bot.Services
{
    public class DataHistoricsService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly TokenService _tokenService; // Servicio para obtener el token
        private readonly ILogger<DataHistoricsService> _logger;
        private readonly IServiceProvider _serviceProvider; // Proveedor de servicios para manejar la inyección de dependencias

        public DataHistoricsService(HttpClient httpClient, TokenService tokenService, ILogger<DataHistoricsService> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _httpClient = httpClient;
            _tokenService = tokenService;
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        public async Task<List<HistoricDataGridDto>> GetHistoricData(string startDate, string endDate)
        {
            // Validación de fechas
            if (!DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end))
            {
                throw new ArgumentException("Formato de fecha no válido. Se espera 'yyyy-MM-ddTHH:mm:ss'.");
            }
                       
            // Obtener el token de acceso
            var token = await _tokenService.GetTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Construir la URL de la API para enviar los datos
            var apiUrl = _configuration["Authentication:ApiUrl"];
            var apiEndpoint = $"{apiUrl}/service/haulages/api/v2/productionmonitors/history/haulage/extraction/{start:yyyy-MM-ddTHH:mm:ss}/{end:yyyy-MM-ddTHH:mm:ss}";

            // Usar el método genérico para obtener datos
            var rawData = await GetDataFromApi<List<HistoricApiResponse>>(apiEndpoint);

            if (rawData == null || !rawData.Any())
            {
                throw new HttpRequestException("No se pudieron procesar los datos de la API o la respuesta fue vacía.");
            }

            // Mapear los datos de la API al modelo DTO necesario para el frontend
            var mappedData = rawData.Select(d =>
            {
                return new HistoricDataGridDto
                {
                    Vehicle = d.Vehicle,
                    Employee = d.Employee,
                    WorkshiftName = d.WorkshiftName,
                    VehicleCompanyName = d.VehicleCompanyName,
                    EmployeeCompanyName = d.EmployeCompanyName,
                    OperationTime = d.OperationTime,
                    TonsTransported = d.TonsTransported,
                    MaterialTypeName = d.MaterialTypeName,
                    LoadPointName = d.LoadPointName,
                    UnloadPointName = d.UnloadPointName,
                    WeighingType = GetWeighingTypeDescription(d.WeighingType),
                    WeightType = GetWeightTypeDescription(d.WeightType),
                    UserRegister = d.UserRegister,
                    ModifiedDate = d.ModifiedDate,
                    UnloadDate = d.UnloadDate,
                    Comments = d.Comments
                };
            }).ToList();

            return mappedData;
        }

        private async Task<T> GetDataFromApi<T>(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url); // Hacer la solicitud HTTP GET
                var responseString = await response.Content.ReadAsStringAsync(); // Leer la respuesta como string
                //_logger.LogInformation($"Respuesta de la API para {url}: {responseString}");
                _logger.LogInformation($"Respuesta de la API para {url}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Error al obtener datos de la API {url}: {response.StatusCode} - {responseString}");
                    throw new Exception($"Error al obtener datos de la API: {response.StatusCode} - {responseString}");
                }

                return JsonConvert.DeserializeObject<T>(responseString) ?? throw new Exception("No se pudo deserializar la respuesta de la API.");
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError($"Error al deserializar JSON: {jsonEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener datos de la API: {ex.Message}");
                throw;
            }
        }

        private string GetWeighingTypeDescription(int weighingType)
        {
            return weighingType switch
            {
                0 => "Sin peso",
                1 => "Peso bruto",
                2 => "Peso neto",
                _ => "Desconocido"
            };
        }

        private string GetWeightTypeDescription(int weightType)
        {
            return weightType switch
            {
                0 => "Ligero",
                1 => "Pesado",
                _ => "Desconocido"
            };
        }

        public async Task ValidateAndSendHistoricData(List<HistoricDataGridDto> records)
        {
            var validationErrors = new List<string>();
            var validRecords = new List<dynamic>();

            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<dbboot>();

                // Validar todos los registros
                foreach (var record in records)
                {
                    try
                    {
                        // Validar campos obligatorios
                        if (string.IsNullOrWhiteSpace(record.Vehicle) || string.IsNullOrWhiteSpace(record.Employee) ||
                            string.IsNullOrWhiteSpace(record.LoadPointName) || string.IsNullOrWhiteSpace(record.MaterialTypeName) ||
                            record.UnloadDate == null || record.ModifiedDate == null)
                        {
                            validationErrors.Add($"Registro omitido debido a datos faltantes: {record.Vehicle}, {record.Employee}, {record.LoadPointName}, {record.MaterialTypeName}");
                            continue;
                        }

                        // Validar empleado
                        var employee = await context.Employees.FirstOrDefaultAsync(e => e.FullName == record.Employee);
                        if (employee == null)
                        {
                            validationErrors.Add($"Empleado no encontrado: {record.Employee}");
                            continue;
                        }

                        // Validar vehículo
                        var vehicle = await context.Vehicles.FirstOrDefaultAsync(v => v.EconomicNumber == record.Vehicle);
                        if (vehicle == null)
                        {
                            validationErrors.Add($"Vehículo no encontrado: {record.Vehicle}");
                            continue;
                        }

                        // Validar ruta
                        var path = await context.Routes
                            .FirstOrDefaultAsync(r => r.loadPointName == record.LoadPointName && r.unLoadPointName == record.UnloadPointName);
                        if (path == null)
                        {
                            validationErrors.Add($"Ruta no encontrada para LoadPointName: {record.LoadPointName} y UnLoadPointName: {record.UnloadPointName}");
                            continue;
                        }

                        // Validar tipo de material
                        var materialType = await context.Materials.FirstOrDefaultAsync(m => m.name == record.MaterialTypeName);
                        if (materialType == null)
                        {
                            validationErrors.Add($"Tipo de material no encontrado: {record.MaterialTypeName}");
                            continue;
                        }

                        // Agregar registro válido
                        validRecords.Add(new
                        {
                            vehicleId = vehicle.VehicleId,
                            employeeId = employee.EmployeeId,
                            pathId = path.haulagePathId,
                            weight = record.TonsTransported,
                            date = record.UnloadDate?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                            comments = record.Comments,
                            materialTypeId = materialType.materialTypeId
                        });
                    }
                    catch (Exception ex)
                    {
                        validationErrors.Add($"Error inesperado en el registro: {record.Vehicle}. Detalles: {ex.Message}");
                    }
                }

                // Si hay errores, no proceder
                if (validationErrors.Any())
                {
                    throw new Exception($"Errores de validación encontrados. Total registros: {records.Count}, Errores: {validationErrors.Count}. Detalles: {string.Join(", ", validationErrors)}");
                }

                // Si todos los registros son válidos, enviarlos a la API
                foreach (var payload in validRecords)
                {
                    var jsonContent = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var token = await _tokenService.GetTokenAsync();
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var apiUrl = _configuration["Authentication:ApiUrl"];
                    var apiEndpoint = $"{apiUrl}/service/haulages/api/v2/manualhaulages/manual/add";

                    var response = await _httpClient.PostAsync(apiEndpoint, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorResponse = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Error al enviar datos a la API. Status: {response.StatusCode}. Detalles: {errorResponse}");
                    }
                }
            }
        }


        //public async Task SendHistoricDataAsHaulage(List<HistoricDataGridDto> records)
        //{
        //    var validationErrors = new List<string>();
        //    var validRecords = new List<dynamic>();

        //    try
        //    {
        //        using (var scope = _serviceProvider.CreateScope())
        //        {
        //            var context = scope.ServiceProvider.GetRequiredService<dbboot>();

        //            // Validar y preparar los registros
        //            foreach (var record in records)
        //            {
        //                try
        //                {
        //                    // Validar campos obligatorios
        //                    if (string.IsNullOrWhiteSpace(record.Vehicle) || string.IsNullOrWhiteSpace(record.Employee) ||
        //                        string.IsNullOrWhiteSpace(record.LoadPointName) || string.IsNullOrWhiteSpace(record.MaterialTypeName) ||
        //                        record.UnloadDate == null || record.ModifiedDate == null)
        //                    {
        //                        validationErrors.Add($"Registro omitido debido a datos faltantes: {record.Vehicle}, {record.Employee}, {record.LoadPointName}, {record.MaterialTypeName}");
        //                        continue;
        //                    }

        //                    // Buscar empleado
        //                    var employee = await context.Employees.FirstOrDefaultAsync(e => e.FullName == record.Employee);
        //                    if (employee == null)
        //                    {
        //                        validationErrors.Add($"Empleado no encontrado: {record.Employee}");
        //                        continue;
        //                    }

        //                    // Buscar vehículo
        //                    var vehicle = await context.Vehicles.FirstOrDefaultAsync(v => v.EconomicNumber == record.Vehicle);
        //                    if (vehicle == null)
        //                    {
        //                        validationErrors.Add($"Vehículo no encontrado: {record.Vehicle}");
        //                        continue;
        //                    }

        //                    // Buscar ruta
        //                    var path = await context.Routes
        //                        .FirstOrDefaultAsync(r => r.loadPointName == record.LoadPointName && r.unLoadPointName == record.UnloadPointName);
        //                    if (path == null)
        //                    {
        //                        validationErrors.Add($"Ruta no encontrada para LoadPointName: {record.LoadPointName} y UnLoadPointName: {record.UnloadPointName}");
        //                        continue;
        //                    }

        //                    // Buscar tipo de material
        //                    var materialType = await context.Materials.FirstOrDefaultAsync(m => m.name == record.MaterialTypeName);
        //                    if (materialType == null)
        //                    {
        //                        validationErrors.Add($"Tipo de material no encontrado: {record.MaterialTypeName}");
        //                        continue;
        //                    }

        //                    // Preparar el objeto válido
        //                    var haulagePayload = new
        //                    {
        //                        vehicleId = vehicle.VehicleId,
        //                        employeeId = employee.EmployeeId,
        //                        pathId = path.haulagePathId,
        //                        weight = record.TonsTransported,
        //                        date = record.UnloadDate?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        //                        comments = record.Comments,
        //                        materialTypeId = materialType.materialTypeId
        //                    };

        //                    validRecords.Add(haulagePayload);
        //                }
        //                catch (Exception ex)
        //                {
        //                    validationErrors.Add($"Error inesperado en el registro: {record.Vehicle}. Detalles: {ex.Message}");
        //                }
        //            }

        //            // Si no hay registros válidos, detener aquí
        //            if (!validRecords.Any())
        //            {
        //                throw new Exception("No hay registros válidos para procesar.");
        //            }

        //            // Enviar los registros válidos a la API en lote
        //            foreach (var payload in validRecords)
        //            {
        //                var jsonContent = JsonConvert.SerializeObject(payload);
        //                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        //                var token = await _tokenService.GetTokenAsync();
        //                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        //                var apiUrl = _configuration["Authentication:ApiUrl"];
        //                var apiEndpoint = $"{apiUrl}/service/haulages/api/v2/manualhaulages/manual/add";

        //                var response = await _httpClient.PostAsync(apiEndpoint, content);

        //                if (!response.IsSuccessStatusCode)
        //                {
        //                    var errorResponse = await response.Content.ReadAsStringAsync();
        //                    _logger.LogError($"Error al enviar datos de acarreo a la API. Status: {response.StatusCode}. Detalles: {errorResponse}");
        //                    throw new Exception($"Error al enviar datos de acarreo a la API: {response.StatusCode}");
        //                }

        //                _logger.LogInformation($"Registro de acarreo enviado exitosamente para el vehículo: {payload.vehicleId}");
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError($"Error al procesar los registros de acarreo: {ex.Message}");
        //        throw;
        //    }
        //    finally
        //    {
        //        if (validationErrors.Any())
        //        {
        //            _logger.LogWarning("Errores encontrados durante la validación:");
        //            foreach (var error in validationErrors)
        //            {
        //                _logger.LogWarning(error);
        //            }
        //        }
        //    }
        //}

    }
}
