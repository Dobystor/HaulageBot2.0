using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using haulages_bot.Data;

namespace haulages_bot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeesController : ControllerBase
    {
        private readonly dbboot _context;

        public EmployeesController(dbboot context)
        {
            _context = context;
        }

        [HttpGet("GetEmployees")]
        public IActionResult GetEmployees([FromQuery] int serverId)
        {
            var employees = _context.Employees
                .Where(e => e.ServerConfigId == serverId)
                .Select(e => new
                {
                    e.EmployeeId,
                    nombreCompleto = e.Name + " " + e.PaternalLastName + " " + e.MaternalLastName
                }).ToList();

            return Ok(employees);
        }
    }
}
