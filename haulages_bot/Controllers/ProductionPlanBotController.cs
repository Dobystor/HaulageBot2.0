using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using haulages_bot.Data;
using haulages_bot.Models;
using haulages_bot.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace haulages_bot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductionPlanBotController : ControllerBase
    {
        private readonly dbboot _dbContext;

        public ProductionPlanBotController(dbboot dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>Diagnóstico: ver qué devuelve la API de planes de producción para un año (raw)</summary>
        [HttpGet("{serverId}/debug/{year}")]
        public async Task<IActionResult> DebugPlans(
            int serverId, int year,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest("Servidor no encontrado");

            var client = httpClientFactory.CreateClient();
            var token = await tokenService.GetTokenAsync(server.Id);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";
            var url = $"{host}/service/haulages/api/v2/productionplans/plans/extraction/mineral/{year}";

            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            // Devolver raw text para ver la estructura real
            return Content(body, "application/json");
        }

        /// <summary>Diagnóstico: ver qué devuelve la API de workdays</summary>
        [HttpGet("{serverId}/debug-workdays/{year}/{month}")]
        public async Task<IActionResult> DebugWorkdays(
            int serverId, int year, int month,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] TokenService tokenService)
        {
            var server = await _dbContext.ServerConfigs.FindAsync(serverId);
            if (server == null) return BadRequest("Servidor no encontrado");

            var client = httpClientFactory.CreateClient();
            var token = await tokenService.GetTokenAsync(server.Id);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var host = server.ApiUrl.StartsWith("http") ? server.ApiUrl : $"https://{server.ApiUrl}";

            // Probar GET de workdays para ese mes
            var getUrl = $"{host}/service/haulages/api/v2/productionplans/workdays/{year}/{month}";
            var getResp = await client.GetAsync(getUrl);
            var getBody = await getResp.Content.ReadAsStringAsync();

            object? getParsed = null;
            try { getParsed = JsonConvert.DeserializeObject(getBody); } catch { }

            return Ok(new
            {
                getUrl,
                getStatus = (int)getResp.StatusCode,
                getData = getParsed ?? getBody
            });
        }
    }
}
