using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Historicos.Controllers
{
    public class ControlController : Controller
    {
        // Inyección de dependencias para el ciclo de vida de la aplicación y el host
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IHost _host;

        // Constructor que recibe el ciclo de vida de la aplicación y el host a través de inyección de dependencias
        public ControlController(IHostApplicationLifetime lifetime, IHost host)
        {
            _lifetime = lifetime;
            _host = host;
        }

        // Método para manejar solicitudes GET a la ruta "Index"
        public IActionResult Index()
        {
            // Devuelve la vista de índice
            return View();
        }

        // Método para manejar solicitudes GET a la ruta "Start"
        public async Task<IActionResult> Start()
        {
            // Lógica para iniciar el servicio
            // No es necesario hacer nada, ya que el servicio se inicia automáticamente al iniciar la aplicación
            // El comentario indica que el servicio se inicia automáticamente con la aplicación
            return RedirectToAction("Index");
        }

        // Método para manejar solicitudes GET a la ruta "Stop"
        public async Task<IActionResult> Stop()
        {
            // Lógica para detener el servicio
            // Llama a StopAsync en el host para detener la aplicación
            await _host.StopAsync();

            // Redirige de vuelta a la página de índice
            return RedirectToAction("Index");
        }
    }
}
