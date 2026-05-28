using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using haulages_bot.Data;
using haulages_bot.Models;

namespace haulages_bot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HaulagesController : ControllerBase
    {
        private readonly dbboot _context;

        public HaulagesController(dbboot context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetHaulages([FromQuery] int serverId, [FromQuery] int limit = 100)
        {
            var query = from h in _context.Haulages.Where(h => h.ServerConfigId == serverId)
                        join v in _context.Vehicles.Where(v => v.ServerConfigId == serverId) on h.VehicleId equals v.VehicleId into vg
                        from v in vg.DefaultIfEmpty()
                        join e in _context.Employees.Where(e => e.ServerConfigId == serverId) on h.EmployeeId equals e.EmployeeId into eg
                        from e in eg.DefaultIfEmpty()
                        join r in _context.Routes.Where(r => r.ServerConfigId == serverId) on h.PathId equals r.haulagePathId into rg
                        from r in rg.DefaultIfEmpty()
                        join m in _context.Materials.Where(m => m.ServerConfigId == serverId) on h.materialTypeId equals m.materialTypeId into mg
                        from m in mg.DefaultIfEmpty()
                        orderby h.HaulageId descending
                        select new
                        {
                            h.HaulageId,
                            VehicleId = h.VehicleId,
                            VehicleEconomicNumber = v != null ? v.EconomicNumber : "",
                            EmployeeId = h.EmployeeId,
                            EmployeeFullName = e != null ? e.FullName : "",
                            PathId = h.PathId,
                            RouteDescription = r != null ? r.description : "",
                            Weight = h.Weight,
                            Dateofcarries = h.Dateofcarries,
                            Comments = h.Comments,
                            MaterialName = m != null ? m.name : h.MaterialType ?? ""
                        };

            if (limit <= 0) limit = 100;
            var list = await query.Take(limit).ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> CreateHaulage([FromBody] Haulage haulage)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Datos inválidos");
            }

            try
            {
                _context.Haulages.Add(haulage);
                await _context.SaveChangesAsync();
                return Ok("Registro insertado correctamente");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al insertar los datos: {ex.Message}");
            }
        }

        [HttpGet("GetEmpleados")]
        public IActionResult GetEmpleados([FromQuery] int serverId)
        {
            var empleados = _context.Employees
                .Where(e => e.ServerConfigId == serverId)
                .Select(e => new
                {
                    e.EmployeeId,
                    nombreCompleto = e.Name + " " + e.PaternalLastName + " " + e.MaternalLastName
                }).ToList();

            return Ok(empleados);
        }
    }
}
