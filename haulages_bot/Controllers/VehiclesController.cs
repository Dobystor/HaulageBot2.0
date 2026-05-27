using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using haulages_bot.Data;

namespace haulages_bot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VehiclesController : ControllerBase
    {
        private readonly dbboot _context;

        public VehiclesController(dbboot context)
        {
            _context = context;
        }

        [HttpGet("GetVehicles")]
        public IActionResult GetVehicles([FromQuery] int serverId)
        {
            var vehicles = _context.Vehicles
                .Where(v => v.ServerConfigId == serverId)
                .Select(v => new
                {
                    v.VehicleId,
                    v.EconomicNumber
                })
                .ToList();

            return Ok(vehicles);
        }
    }
}
