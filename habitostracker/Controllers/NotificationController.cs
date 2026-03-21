using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class NotificationController : Controller
    {
        private readonly HabitDbContext _context;

        public NotificationController(HabitDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var notifications = _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();

            // marcar como leídas
            foreach (var n in notifications)
            {
                n.IsRead = true;
            }

            _context.SaveChanges();

            return View(notifications);
        }
    }
}