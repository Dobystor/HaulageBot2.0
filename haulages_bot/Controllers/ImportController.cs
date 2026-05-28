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
using Newtonsoft.Json;
using haulages_bot.Services;
using haulages_bot.Data;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Models;
using System.Linq;

namespace haulages_bot.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ImportController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly TokenService _tokenService;
        private readonly dbboot _dbContext;

        public ImportController(IHttpClientFactory httpClientFactory, TokenService tokenService, dbboot dbContext)
        {
            _httpClient = httpClientFactory.CreateClient();
            _tokenService = tokenService;
            _dbContext = dbContext;
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

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var apiEndpoint = $"{host}/service/haulages/api/v2/manualhaulages/manual/add";

            int rowsProcessed = 0;
            int rowsFailed = 0;
            var failedRows = new List<FailedRow>();

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

                            // 1. Resolver vehículo por número económico
                            var vehicle = await _dbContext.Vehicles.FirstOrDefaultAsync(v => 
                                v.ServerConfigId == serverId && 
                                v.EconomicNumber.ToLower() == vehicleCode.ToLower());
                            if (vehicle == null) throw new Exception($"Vehículo '{vehicleCode}' no encontrado.");

                            // 2. Resolver empleado por Nombre Completo (ya no viene columna de número económico)
                            Employee employee = null;
                            if (!string.IsNullOrEmpty(employeeName))
                            {
                                employee = await _dbContext.Employees.FirstOrDefaultAsync(e => 
                                    e.ServerConfigId == serverId && 
                                    e.FullName.ToLower() == employeeName.ToLower());
                            }
                            if (employee == null) throw new Exception($"Empleado con Nombre '{employeeName}' no encontrado.");

                            // 3. Resolver ruta por descripción
                            var route = await _dbContext.Routes.FirstOrDefaultAsync(r => 
                                r.ServerConfigId == serverId && 
                                r.description.ToLower() == routeDescription.ToLower());
                            if (route == null) throw new Exception($"Ruta '{routeDescription}' no encontrada.");

                            // 4. Resolver material por nombre (u obtenerlo automáticamente si está vacío en Excel)
                            int resolvedMaterialTypeId;
                            if (string.IsNullOrEmpty(materialName))
                            {
                                var materials = await _dbContext.Materials.Where(m => m.ServerConfigId == serverId).ToListAsync();
                                var mineralMat = materials.FirstOrDefault(m => m.name.ToUpperInvariant().Contains("MINERAL"));
                                var desmonteMat = materials.FirstOrDefault(m => m.name.ToUpperInvariant().Contains("DESMONTE") || m.name.ToUpperInvariant().Contains("ESTERIL") || m.name.ToUpperInvariant().Contains("ESTÉRIL"));
                                int mineralId = mineralMat?.materialTypeId ?? (materials.Any() ? materials.First().materialTypeId : 1);
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
                                        resolvedMaterialTypeId = new Random().Next(2) == 0 ? mineralId : specificEsterilId;
                                        break;
                                    case 0:
                                    default:
                                        resolvedMaterialTypeId = mineralId;
                                        break;
                                }
                            }
                            else
                            {
                                var material = await _dbContext.Materials.FirstOrDefaultAsync(m => 
                                    m.ServerConfigId == serverId && 
                                    m.name.ToLower() == materialName.ToLower());
                                if (material == null) throw new Exception($"Material '{materialName}' no encontrado.");
                                resolvedMaterialTypeId = material.materialTypeId;
                            }

                            var acarreo = new
                            {
                                VehicleId = vehicle.VehicleId,
                                EmployeeId = employee.EmployeeId,
                                PathId = route.haulagePathId,
                                Weight = weight,
                                Date = dateOfCarries,
                                Comments = "Importado desde Excel",
                                materialTypeId = resolvedMaterialTypeId
                            };

                            var jsonContent = JsonConvert.SerializeObject(acarreo);
                            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                            var response = await _httpClient.PostAsync(apiEndpoint, content);

                            if (response.IsSuccessStatusCode)
                            {
                                rowsProcessed++;

                                // Guardar en local
                                _dbContext.Haulages.Add(new Haulage
                                {
                                    VehicleId = vehicle.VehicleId,
                                    EmployeeId = employee.EmployeeId,
                                    PathId = route.haulagePathId,
                                    Weight = weight,
                                    Comments = "Importado desde Excel",
                                    materialTypeId = resolvedMaterialTypeId,
                                    ServerConfigId = serverId,
                                    Dateofcarries = dateOfCarries.ToString("yyyy-MM-dd HH:mm:ss")
                                });
                                await _dbContext.SaveChangesAsync();
                            }
                            else
                            {
                                var errorMsg = await response.Content.ReadAsStringAsync();
                                rowsFailed++;
                                failedRows.Add(new FailedRow
                                {
                                    RowNumber = row.RowNumber(),
                                    VehicleCode = vehicleCode,
                                    EmployeeNo = "",
                                    EmployeeName = employeeName,
                                    RouteDescription = routeDescription,
                                    Weight = weight,
                                    MaterialName = materialName,
                                    DateStr = dateStr,
                                    ErrorMessage = errorMsg
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            rowsFailed++;
                            failedRows.Add(new FailedRow
                            {
                                RowNumber = row.RowNumber(),
                                VehicleCode = vehicleCode,
                                EmployeeNo = "",
                                EmployeeName = employeeName,
                                RouteDescription = routeDescription,
                                Weight = weight,
                                MaterialName = materialName,
                                DateStr = dateStr,
                                ErrorMessage = ex.Message
                            });
                        }
                    }
                }
            }

            return Ok(new { 
                success = true, 
                message = $"Importación finalizada. Procesados: {rowsProcessed}, Fallidos: {rowsFailed}",
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

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var apiEndpoint = $"{host}/service/haulages/api/v2/manualhaulages/manual/add";

            int rowsProcessed = 0;
            int rowsFailed = 0;
            var failedRows = new List<FailedRow>();

            foreach (var row in rows)
            {
                try
                {
                    var vehicle = await _dbContext.Vehicles.FirstOrDefaultAsync(v => 
                        v.ServerConfigId == serverId && 
                        v.EconomicNumber.ToLower() == row.VehicleCode.Trim().ToLower());
                    if (vehicle == null) throw new Exception($"Vehículo '{row.VehicleCode}' no encontrado.");

                    var empNoCell = row.EmployeeNo?.Trim() ?? "";
                    var empNameCell = row.EmployeeName?.Trim() ?? "";
                    Employee employee = null;

                    if (decimal.TryParse(empNoCell, out decimal noEmp))
                    {
                        employee = await _dbContext.Employees.FirstOrDefaultAsync(e => 
                            e.ServerConfigId == serverId && 
                            e.NoEmployee == noEmp);
                    }
                    else if (!string.IsNullOrEmpty(empNoCell))
                    {
                        employee = await _dbContext.Employees.FirstOrDefaultAsync(e => 
                            e.ServerConfigId == serverId && 
                            e.FullName.ToLower() == empNoCell.ToLower());
                    }

                    if (employee == null && !string.IsNullOrEmpty(empNameCell))
                    {
                        employee = await _dbContext.Employees.FirstOrDefaultAsync(e => 
                            e.ServerConfigId == serverId && 
                            e.FullName.ToLower() == empNameCell.ToLower());
                    }
                    if (employee == null) throw new Exception($"Empleado con Nombre '{empNameCell}' no encontrado.");

                    var route = await _dbContext.Routes.FirstOrDefaultAsync(r => 
                        r.ServerConfigId == serverId && 
                        r.description.ToLower() == row.RouteDescription.Trim().ToLower());
                    if (route == null) throw new Exception($"Ruta '{row.RouteDescription}' no encontrada.");

                    int resolvedMaterialTypeId;
                    var matNameCell = row.MaterialName?.Trim() ?? "";
                    if (string.IsNullOrEmpty(matNameCell))
                    {
                        var materials = await _dbContext.Materials.Where(m => m.ServerConfigId == serverId).ToListAsync();
                        var mineralMat = materials.FirstOrDefault(m => m.name.ToUpperInvariant().Contains("MINERAL"));
                        var desmonteMat = materials.FirstOrDefault(m => m.name.ToUpperInvariant().Contains("DESMONTE") || m.name.ToUpperInvariant().Contains("ESTERIL") || m.name.ToUpperInvariant().Contains("ESTÉRIL"));
                        int mineralId = mineralMat?.materialTypeId ?? (materials.Any() ? materials.First().materialTypeId : 1);
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
                                resolvedMaterialTypeId = new Random().Next(2) == 0 ? mineralId : specificEsterilId;
                                break;
                            case 0:
                            default:
                                resolvedMaterialTypeId = mineralId;
                                break;
                        }
                    }
                    else
                    {
                        var material = await _dbContext.Materials.FirstOrDefaultAsync(m => 
                            m.ServerConfigId == serverId && 
                            m.name.ToLower() == matNameCell.ToLower());
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
                        rowsProcessed++;

                        _dbContext.Haulages.Add(new Haulage
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
                        await _dbContext.SaveChangesAsync();
                    }
                    else
                    {
                        var errorMsg = await response.Content.ReadAsStringAsync();
                        rowsFailed++;
                        failedRows.Add(new FailedRow
                        {
                            VehicleCode = row.VehicleCode,
                            EmployeeNo = row.EmployeeNo,
                            EmployeeName = row.EmployeeName,
                            RouteDescription = row.RouteDescription,
                            Weight = row.Weight,
                            MaterialName = row.MaterialName,
                            DateStr = row.DateStr,
                            ErrorMessage = errorMsg
                        });
                    }
                }
                catch (Exception ex)
                {
                    rowsFailed++;
                    failedRows.Add(new FailedRow
                    {
                        VehicleCode = row.VehicleCode,
                        EmployeeNo = row.EmployeeNo,
                        EmployeeName = row.EmployeeName,
                        RouteDescription = row.RouteDescription,
                        Weight = row.Weight,
                        MaterialName = row.MaterialName,
                        DateStr = row.DateStr,
                        ErrorMessage = ex.Message
                    });
                }
            }

            return Ok(new
            {
                success = true,
                message = $"Importación de corregidos finalizada. Procesados: {rowsProcessed}, Fallidos: {rowsFailed}",
                failedRows = failedRows
            });
        }
    }

    public class FailedRow
    {
        public int RowNumber { get; set; }
        public string VehicleCode { get; set; }
        public string EmployeeNo { get; set; }
        public string EmployeeName { get; set; }
        public string RouteDescription { get; set; }
        public decimal Weight { get; set; }
        public string MaterialName { get; set; }
        public string DateStr { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class RowDto
    {
        public string VehicleCode { get; set; }
        public string EmployeeNo { get; set; }
        public string EmployeeName { get; set; }
        public string RouteDescription { get; set; }
        public decimal Weight { get; set; }
        public string MaterialName { get; set; }
        public string DateStr { get; set; }
    }
}
