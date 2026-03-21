using habitostracker.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace habitostracker.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public IActionResult ConnectionBlocked()
        {
            return View();
        }

        [HttpGet]
        public IActionResult CheckConnection()
        {
            using (var db = HttpContext.RequestServices.GetService<HabitTrackerApp.Data.HabitDbContext>())
            {
                var block = db.ConnectionBlocks.FirstOrDefault();

                bool blocked = block != null && block.IsBlocked;

                return Json(new { blocked = blocked });
            }
        }
        public IActionResult GetMyIP()
        {
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrEmpty(ip))
            {
                ip = ip.Split(',').First().Trim();
            }
            else
            {
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            return Content(ip ?? "");
        }
    }
}
