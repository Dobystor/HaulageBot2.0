using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using haulages_bot.Models;
using Microsoft.Extensions.Logging;

namespace haulages_bot.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult login()
        {
            return View();
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Import()
        {
            var authToken = HttpContext.Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Authentication");
            }
            return View();
        }

        public IActionResult conf()
        {
            var authToken = HttpContext.Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Authentication");
            }
            return View();
        }

        public IActionResult gene()
        {
            var authToken = HttpContext.Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Authentication");
            }
            return View();
        }

        public IActionResult datos()
        {
            var authToken = HttpContext.Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Authentication");
            }
            return View();
        }

        public IActionResult welcome()
        {
            var authToken = HttpContext.Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Authentication");
            }
            return View();
        }

        public IActionResult servers()
        {
            var authToken = HttpContext.Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Authentication");
            }
            return View();
        }

        public IActionResult Logs()
        {
            var authToken = HttpContext.Request.Cookies["AuthToken"];

            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Authentication");
            }
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
