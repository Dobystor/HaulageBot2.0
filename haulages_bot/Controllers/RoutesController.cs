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
    public class RoutesController : ControllerBase
    {
        private readonly dbboot _context;

        public RoutesController(dbboot context)
        {
            _context = context;
        }

        [HttpGet("GetRoutes")]
        public async Task<IActionResult> GetRoutes([FromQuery] int serverId)
        {
            try
            {
                var routes = await _context.Routes
                    .Where(r => r.ServerConfigId == serverId)
                    .Select(r => new
                    {
                        r.haulagePathId,
                        r.description,
                        r.selectedMaterialType
                    }).ToListAsync();

                return Ok(routes);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error al obtener las rutas: {ex.Message}");
            }
        }
    }
}
