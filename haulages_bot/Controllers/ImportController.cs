using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using haulages_bot.Services;
using haulages_bot.Data;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Models;
using System.Linq;

using Microsoft.AspNetCore.SignalR;
using haulages_bot.Hubs;

namespace haulages_bot.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ImportController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly TokenService _tokenService;
        private readonly dbboot _dbContext;
        private readonly LogHistoryService _logHistoryService;
        private readonly IHubContext<NotificationHub> _notificationHubContext;

        public ImportController(
            IHttpClientFactory httpClientFactory, 
            TokenService tokenService, 
            dbboot dbContext,
            LogHistoryService logHistoryService,
            IHubContext<NotificationHub> notificationHubContext)
        {
            _httpClient = httpClientFactory.CreateClient();
            _tokenService = tokenService;
            _dbContext = dbContext;
            _logHistoryService = logHistoryService;
            _notificationHubContext = notificationHubContext;
        }

        [HttpGet("DownloadTemplate")]
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template");
                
                // Agregamos las cabeceras necesarias legibles para humanos
                worksheet.Cell(1, 1).Value = "Vehículo (No. Económico)";
                worksheet.Cell(1, 2).Value = "Empleado (Nombre Completo)";
                worksheet.Cell(1, 3).Value = "Ruta (Descripción)";
                worksheet.Cell(1, 4).Value = "Peso (Toneladas)";
                worksheet.Cell(1, 5).Value = "Material (Nombre)";
                worksheet.Cell(1, 6).Value = "Fecha de Acarreo (YYYY-MM-DD HH:MM:SS)";
                
                // Formato básico a cabeceras
                var headerRange = worksheet.Range("A1:F1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ImportTemplate.xlsx");
                }
            }
        }

        [HttpPost("Upload")]
        public async Task<IActionResult> Upload(IFormFile file, [FromQuery] int serverId)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Por favor seleccione un archivo válido." });

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "El archivo debe ser un Excel (.xlsx)." });

            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null)
            {
                return BadRequest(new { success = false, message = "Servidor no encontrado." });
            }

            var token = await _tokenService.GetTokenAsync(serverId);
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized(new { success = false, message = "No se pudo obtener el token de autorización." });
            }

            _logHistoryService.AddLog(serverId, $"Iniciando importación desde archivo Excel: {file.FileName}...");
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Message = $"Iniciando importación desde archivo Excel..." });

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var apiEndpoint = $"{host}/service/haulages/api/v2/manualhaulages/manual/add";

            int rowsProcessed = 0;
            int rowsFailed = 0;
            var failedRowsBag = new System.Collections.Concurrent.ConcurrentBag<FailedRow>();

            // Pre-fetch catalogs in memory
            var vehiclesList = await _dbContext.Vehicles.Where(v => v.ServerConfigId == serverId).ToListAsync();
            var employeesList = await _dbContext.Employees.Where(e => e.ServerConfigId == serverId).ToListAsync();
            var routesList = await _dbContext.Routes.Where(r => r.ServerConfigId == serverId).ToListAsync();
            var materialsList = await _dbContext.Materials.Where(m => m.ServerConfigId == serverId).ToListAsync();

            var parsedRows = new List<ExcelParsedRow>();

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var workbook = new XLWorkbook(stream))
                {
                    var worksheet = workbook.Worksheet(1);
                    var rows = worksheet.RangeUsed().RowsUsed();
                    bool firstRow = true;

                    foreach (var row in rows)
                    {
                        if (firstRow)
                        {
                            firstRow = false; // Saltar cabeceras
                            continue;
                        }

                        string vehicleCode = "";
                        string employeeName = "";
                        string routeDescription = "";
                        decimal weight = 0;
                        string materialName = "";
                        string dateStr = "";

                        try
                        {
                            vehicleCode = row.Cell(1).GetString().Trim();
                            employeeName = row.Cell(2).GetString().Trim();
                            routeDescription = row.Cell(3).GetString().Trim();

                            // Obtener peso
                            if (row.Cell(4).DataType == XLDataType.Number) {
                                weight = row.Cell(4).GetValue<decimal>();
                            } else {
                                decimal.TryParse(row.Cell(4).GetString(), out weight);
                            }

                            materialName = row.Cell(5).GetString().Trim();

                            // Obtener fecha
                            DateTime dateOfCarries;
                            if (row.Cell(6).DataType == XLDataType.DateTime) {
                                dateOfCarries = row.Cell(6).GetDateTime();
                                dateStr = dateOfCarries.ToString("yyyy-MM-dd HH:mm:ss");
                            } else {
                                dateStr = row.Cell(6).GetString().Trim();
                                if (!DateTime.TryParse(dateStr, out dateOfCarries))
                                    dateOfCarries = DateTime.Now;
                            }

                            parsedRows.Add(new ExcelParsedRow
                            {
                                RowNumber = row.RowNumber(),
                                VehicleCode = vehicleCode,
                                EmployeeName = employeeName,
                                RouteDescription = routeDescription,
                                Weight = weight,
                                MaterialName = materialName,
                                DateStr = dateStr
                            });
                        }
                        catch (Exception ex)
                        {
                            rowsFailed++;
                            failedRowsBag.Add(new FailedRow
                            {
                                RowNumber = row.RowNumber(),
                                VehicleCode = vehicleCode,
                                EmployeeName = employeeName,
                                RouteDescription = routeDescription,
                                Weight = weight,
                                MaterialName = materialName,
                                DateStr = dateStr,
                                ErrorMessage = $"Error al leer celdas Excel: {ex.Message}"
                            });
                            var detailMsg = $"[Excel Fila {row.RowNumber()}] Excepción al leer: {ex.Message}";
                            _logHistoryService.AddLog(serverId, detailMsg, true);
                            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = detailMsg });
                        }
                    }
                }
            }

            using var semaphore = new SemaphoreSlim(10);
            var tasks = new List<Task>();
            var successfulHaulages = new System.Collections.Concurrent.ConcurrentBag<Haulage>();
            var random = new Random();
            object lockObj = new object();

            foreach (var row in parsedRows)
            {
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // 1. Resolver vehículo por número económico
                        var vehicle = vehiclesList.FirstOrDefault(v => 
                            v.EconomicNumber != null && string.Equals(v.EconomicNumber, row.VehicleCode, StringComparison.OrdinalIgnoreCase));
                        if (vehicle == null) throw new Exception($"Vehículo '{row.VehicleCode}' no encontrado.");

                        // 2. Resolver empleado por Nombre Completo
                        Employee employee = null;
                        if (!string.IsNullOrEmpty(row.EmployeeName))
                        {
                            employee = employeesList.FirstOrDefault(e => 
                                e.FullName != null && string.Equals(e.FullName, row.EmployeeName, StringComparison.OrdinalIgnoreCase));
                        }
                        if (employee == null) throw new Exception($"Empleado con Nombre '{row.EmployeeName}' no encontrado.");

                        // 3. Resolver ruta por descripción
                        var route = routesList.FirstOrDefault(r => 
                            r.description != null && string.Equals(r.description, row.RouteDescription, StringComparison.OrdinalIgnoreCase));
                        if (route == null) throw new Exception($"Ruta '{row.RouteDescription}' no encontrada.");

                        // 4. Resolver material por nombre
                        int resolvedMaterialTypeId;
                        if (string.IsNullOrEmpty(row.MaterialName))
                        {
                            var mineralMat = materialsList.FirstOrDefault(m => m.name != null && m.name.ToUpperInvariant().Contains("MINERAL"));
                            var desmonteMat = materialsList.FirstOrDefault(m => m.name != null && (m.name.ToUpperInvariant().Contains("DESMONTE") || m.name.ToUpperInvariant().Contains("ESTERIL") || m.name.ToUpperInvariant().Contains("ESTÉRIL")));
                            int mineralId = mineralMat?.materialTypeId ?? (materialsList.Any() ? materialsList.First().materialTypeId : 1);
                            int desmonteId = desmonteMat?.materialTypeId ?? mineralId;

                            int specificEsterilId = desmonteId;
                            if (route.materialTypeId.HasValue && route.materialTypeId.Value != 0 && route.materialTypeId.Value != mineralId)
                            {
                                specificEsterilId = route.materialTypeId.Value;
                            }

                            switch (route.selectedMaterialType)
                            {
                                case 1:
                                    resolvedMaterialTypeId = specificEsterilId;
                                    break;
                                case 2:
                                    bool pickMineral;
                                    lock (lockObj)
                                    {
                                        pickMineral = random.Next(2) == 0;
                                    }
                                    resolvedMaterialTypeId = pickMineral ? mineralId : specificEsterilId;
                                    break;
                                case 0:
                                default:
                                    resolvedMaterialTypeId = mineralId;
                                    break;
                            }
                        }
                        else
                        {
                            var material = materialsList.FirstOrDefault(m => 
                                m.name != null && string.Equals(m.name, row.MaterialName, StringComparison.OrdinalIgnoreCase));
                            if (material == null) throw new Exception($"Material '{row.MaterialName}' no encontrado.");
                            resolvedMaterialTypeId = material.materialTypeId;
                        }

                        DateTime dateOfCarries;
                        if (!DateTime.TryParse(row.DateStr, out dateOfCarries))
                            dateOfCarries = DateTime.Now;

                        var acarreo = new
                        {
                            VehicleId = vehicle.VehicleId,
                            EmployeeId = employee.EmployeeId,
                            PathId = route.haulagePathId,
                            Weight = row.Weight,
                            Date = dateOfCarries,
                            Comments = "Importado desde Excel",
                            materialTypeId = resolvedMaterialTypeId
                        };

                        var jsonContent = JsonConvert.SerializeObject(acarreo);
                        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync(apiEndpoint, content);

                        if (response.IsSuccessStatusCode)
                        {
                            successfulHaulages.Add(new Haulage
                            {
                                VehicleId = vehicle.VehicleId,
                                EmployeeId = employee.EmployeeId,
                                PathId = route.haulagePathId,
                                Weight = row.Weight,
                                Comments = "Importado desde Excel",
                                materialTypeId = resolvedMaterialTypeId,
                                ServerConfigId = serverId,
                                Dateofcarries = dateOfCarries.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                            
                            lock (lockObj)
                            {
                                rowsProcessed++;
                                _logHistoryService.AddLog(serverId, $"[Excel Fila {row.RowNumber}] Acarreo importado correctamente: Vehículo {row.VehicleCode}, Peso {row.Weight}t.");
                            }
                        }
                        else
                        {
                            var errorMsg = await response.Content.ReadAsStringAsync();
                            failedRowsBag.Add(new FailedRow
                            {
                                RowNumber = row.RowNumber,
                                VehicleCode = row.VehicleCode,
                                EmployeeName = row.EmployeeName,
                                RouteDescription = row.RouteDescription,
                                Weight = row.Weight,
                                MaterialName = row.MaterialName,
                                DateStr = row.DateStr,
                                ErrorMessage = errorMsg
                            });
                            var detailMsg = $"[Excel Fila {row.RowNumber}] Error al registrar: {errorMsg}";
                            lock (lockObj)
                            {
                                rowsFailed++;
                                _logHistoryService.AddLog(serverId, detailMsg, true);
                            }
                            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = detailMsg });
                        }
                    }
                    catch (Exception ex)
                    {
                        failedRowsBag.Add(new FailedRow
                        {
                            RowNumber = row.RowNumber,
                            VehicleCode = row.VehicleCode,
                            EmployeeName = row.EmployeeName,
                            RouteDescription = row.RouteDescription,
                            Weight = row.Weight,
                            MaterialName = row.MaterialName,
                            DateStr = row.DateStr,
                            ErrorMessage = ex.Message
                        });
                        var detailMsg = $"[Excel Fila {row.RowNumber}] Excepción: {ex.Message}";
                        lock (lockObj)
                        {
                            rowsFailed++;
                            _logHistoryService.AddLog(serverId, detailMsg, true);
                        }
                        await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = detailMsg });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            if (!successfulHaulages.IsEmpty)
            {
                foreach (var Mathaul in successfulHaulages)
                {
                    _dbContext.Haulages.Add(Mathaul);
                }
                await _dbContext.SaveChangesAsync();
            }

            var failedRows = failedRowsBag.OrderBy(fr => fr.RowNumber).ToList();
            var summaryMsg = $"Importación finalizada. Procesados: {rowsProcessed}, Fallidos: {rowsFailed}";
            _logHistoryService.AddLog(serverId, summaryMsg, rowsFailed > 0);
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = rowsFailed > 0, Message = summaryMsg });

            return Ok(new { 
                success = true, 
                message = summaryMsg,
                failedRows = failedRows
            });
        }

        [HttpPost("ImportRows")]
        public async Task<IActionResult> ImportRows([FromBody] List<RowDto> rows, [FromQuery] int serverId)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest(new { success = false, message = "Servidor no encontrado." });

            var token = await _tokenService.GetTokenAsync(serverId);
            if (string.IsNullOrEmpty(token))
                return Unauthorized(new { success = false, message = "No se pudo obtener el token." });

            _logHistoryService.AddLog(serverId, $"Procesando corrección manual de {rows.Count} filas desde la interfaz...");
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Message = "Procesando filas corregidas..." });

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var apiEndpoint = $"{host}/service/haulages/api/v2/manualhaulages/manual/add";

            int rowsProcessed = 0;
            int rowsFailed = 0;
            var failedRowsBag = new System.Collections.Concurrent.ConcurrentBag<FailedRow>();

            // Pre-fetch catalogs in memory
            var vehiclesList = await _dbContext.Vehicles.Where(v => v.ServerConfigId == serverId).ToListAsync();
            var employeesList = await _dbContext.Employees.Where(e => e.ServerConfigId == serverId).ToListAsync();
            var routesList = await _dbContext.Routes.Where(r => r.ServerConfigId == serverId).ToListAsync();
            var materialsList = await _dbContext.Materials.Where(m => m.ServerConfigId == serverId).ToListAsync();

            using var semaphore = new SemaphoreSlim(10);
            var tasks = new List<Task>();
            var successfulHaulages = new System.Collections.Concurrent.ConcurrentBag<Haulage>();
            var random = new Random();
            object lockObj = new object();

            foreach (var row in rows)
            {
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var vehicle = vehiclesList.FirstOrDefault(v => 
                            v.EconomicNumber != null && string.Equals(v.EconomicNumber, row.VehicleCode.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (vehicle == null) throw new Exception($"Vehículo '{row.VehicleCode}' no encontrado.");

                        var empNameCell = row.EmployeeName?.Trim() ?? "";
                        Employee employee = null;

                        if (!string.IsNullOrEmpty(empNameCell))
                        {
                            employee = employeesList.FirstOrDefault(e => 
                                e.FullName != null && string.Equals(e.FullName, empNameCell, StringComparison.OrdinalIgnoreCase));
                        }
                        if (employee == null) throw new Exception($"Empleado con Nombre '{empNameCell}' no encontrado.");

                        var route = routesList.FirstOrDefault(r => 
                            r.description != null && string.Equals(r.description, row.RouteDescription.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (route == null) throw new Exception($"Ruta '{row.RouteDescription}' no encontrada.");

                        int resolvedMaterialTypeId;
                        var matNameCell = row.MaterialName?.Trim() ?? "";
                        if (string.IsNullOrEmpty(matNameCell))
                        {
                            var mineralMat = materialsList.FirstOrDefault(m => m.name != null && m.name.ToUpperInvariant().Contains("MINERAL"));
                            var desmonteMat = materialsList.FirstOrDefault(m => m.name != null && (m.name.ToUpperInvariant().Contains("DESMONTE") || m.name.ToUpperInvariant().Contains("ESTERIL") || m.name.ToUpperInvariant().Contains("ESTÉRIL")));
                            int mineralId = mineralMat?.materialTypeId ?? (materialsList.Any() ? materialsList.First().materialTypeId : 1);
                            int desmonteId = desmonteMat?.materialTypeId ?? mineralId;

                            int specificEsterilId = desmonteId;
                            if (route.materialTypeId.HasValue && route.materialTypeId.Value != 0 && route.materialTypeId.Value != mineralId)
                            {
                                specificEsterilId = route.materialTypeId.Value;
                            }

                            switch (route.selectedMaterialType)
                            {
                                case 1:
                                    resolvedMaterialTypeId = specificEsterilId;
                                    break;
                                case 2:
                                    bool pickMineral;
                                    lock (lockObj)
                                    {
                                        pickMineral = random.Next(2) == 0;
                                    }
                                    resolvedMaterialTypeId = pickMineral ? mineralId : specificEsterilId;
                                    break;
                                case 0:
                                default:
                                    resolvedMaterialTypeId = mineralId;
                                    break;
                            }
                        }
                        else
                        {
                            var material = materialsList.FirstOrDefault(m => 
                                m.name != null && string.Equals(m.name, matNameCell, StringComparison.OrdinalIgnoreCase));
                            if (material == null) throw new Exception($"Material '{matNameCell}' no encontrado.");
                            resolvedMaterialTypeId = material.materialTypeId;
                        }

                        DateTime dateOfCarries;
                        if (!DateTime.TryParse(row.DateStr, out dateOfCarries))
                            dateOfCarries = DateTime.Now;

                        var acarreo = new
                        {
                            VehicleId = vehicle.VehicleId,
                            EmployeeId = employee.EmployeeId,
                            PathId = route.haulagePathId,
                            Weight = row.Weight,
                            Date = dateOfCarries,
                            Comments = "Importado corregido desde UI",
                            materialTypeId = resolvedMaterialTypeId
                        };

                        var jsonContent = JsonConvert.SerializeObject(acarreo);
                        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync(apiEndpoint, content);
                        if (response.IsSuccessStatusCode)
                        {
                            successfulHaulages.Add(new Haulage
                            {
                                VehicleId = vehicle.VehicleId,
                                EmployeeId = employee.EmployeeId,
                                PathId = route.haulagePathId,
                                Weight = row.Weight,
                                Comments = "Importado corregido desde UI",
                                materialTypeId = resolvedMaterialTypeId,
                                ServerConfigId = serverId,
                                Dateofcarries = dateOfCarries.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                            
                            lock (lockObj)
                            {
                                rowsProcessed++;
                                _logHistoryService.AddLog(serverId, $"[Corrección Excel] Registro exitoso: Vehículo {row.VehicleCode}, Peso {row.Weight}t.");
                            }
                        }
                        else
                        {
                            var errorMsg = await response.Content.ReadAsStringAsync();
                            failedRowsBag.Add(new FailedRow
                            {
                                VehicleCode = row.VehicleCode,
                                EmployeeName = row.EmployeeName,
                                RouteDescription = row.RouteDescription,
                                Weight = row.Weight,
                                MaterialName = row.MaterialName,
                                DateStr = row.DateStr,
                                ErrorMessage = errorMsg
                            });
                            var detailMsg = $"[Corrección Excel] Falló: {errorMsg}";
                            lock (lockObj)
                            {
                                rowsFailed++;
                                _logHistoryService.AddLog(serverId, detailMsg, true);
                            }
                            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = detailMsg });
                        }
                    }
                    catch (Exception ex)
                    {
                        failedRowsBag.Add(new FailedRow
                        {
                            VehicleCode = row.VehicleCode,
                            EmployeeName = row.EmployeeName,
                            RouteDescription = row.RouteDescription,
                            Weight = row.Weight,
                            MaterialName = row.MaterialName,
                            DateStr = row.DateStr,
                            ErrorMessage = ex.Message
                        });
                        var detailMsg = $"[Corrección Excel] Excepción: {ex.Message}";
                        lock (lockObj)
                        {
                            rowsFailed++;
                            _logHistoryService.AddLog(serverId, detailMsg, true);
                        }
                        await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = true, Message = detailMsg });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            if (!successfulHaulages.IsEmpty)
            {
                foreach (var Mathaul in successfulHaulages)
                {
                    _dbContext.Haulages.Add(Mathaul);
                }
                await _dbContext.SaveChangesAsync();
            }

            var failedRows = failedRowsBag.ToList();
            var summaryMsg = $"Importación de corregidos finalizada. Procesados: {rowsProcessed}, Fallidos: {rowsFailed}";
            _logHistoryService.AddLog(serverId, summaryMsg, rowsFailed > 0);
            await _notificationHubContext.Clients.All.SendAsync("ReceiveNotification", new { ServerId = serverId, Error = rowsFailed > 0, Message = summaryMsg });

            return Ok(new
            {
                success = true,
                message = summaryMsg,
                failedRows = failedRows
            });
        }
    }

    public class FailedRow
    {
        public int RowNumber { get; set; }
        public string VehicleCode { get; set; }
        public string EmployeeName { get; set; }
        public string RouteDescription { get; set; }
        public decimal Weight { get; set; }
        public string MaterialName { get; set; }
        public string DateStr { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class ExcelParsedRow
    {
        public int RowNumber { get; set; }
        public string VehicleCode { get; set; }
        public string EmployeeName { get; set; }
        public string RouteDescription { get; set; }
        public decimal Weight { get; set; }
        public string MaterialName { get; set; }
        public string DateStr { get; set; }
    }

    public class RowDto
    {
        public string VehicleCode { get; set; }
        public string EmployeeName { get; set; }
        public string RouteDescription { get; set; }
        public decimal Weight { get; set; }
        public string MaterialName { get; set; }
        public string DateStr { get; set; }
    }
}
