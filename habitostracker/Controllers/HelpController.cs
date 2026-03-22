using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApp.Controllers
{
    public class HelpController : Controller
    {
        private readonly HabitDbContext _context;

        public HelpController(HabitDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendFeedback(string message)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            if (string.IsNullOrWhiteSpace(message))
            {
                TempData["Error"] = "Escribe un mensaje.";
                return RedirectToAction("Index");
            }

            // 🔥 LIMITE 1 POR DÍA
            var alreadySent = await _context.Feedbacks
                .AnyAsync(f => f.UserId == userId && f.CreatedAt.Date == DateTime.Today);

            if (alreadySent)
            {
                TempData["Error"] = "Ya enviaste una recomendación hoy.";
                return RedirectToAction("Index");
            }

            var feedback = new Feedback
            {
                UserId = userId,
                Message = message,
                CreatedAt = DateTime.Now
            };

            _context.Feedbacks.Add(feedback);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Gracias por tu opinión ❤️";
            return RedirectToAction("Index");
        }
    }
}