using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using HabitTrackerApp.Hubs;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class FeedbackController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        public FeedbackController(HabitDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(string message)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            // verificar si ya envió comentario hoy
            var today = DateTime.Today;

            var alreadySent = _context.Feedbacks
                .Any(f => f.UserId == userId && f.CreatedAt.Date == today);

            if (alreadySent)
            {
                TempData["Error"] = "Solo puedes enviar un comentario por día 🙈";
                return RedirectToAction("Create");
            }

            var feedback = new Feedback
            {
                UserId = userId,
                Message = message,
                CreatedAt = DateTime.Now
            };

            _context.Feedbacks.Add(feedback);
            _context.SaveChanges();

            // 🔔 NOTIFICAR AL ADMIN EN TIEMPO REAL
            await _hubContext.Clients.All.SendAsync("NewFeedback");

            TempData["Success"] = "Gracias por tu comentario 🙈";

            return RedirectToAction("Index", "Habit");
        }
    }
}