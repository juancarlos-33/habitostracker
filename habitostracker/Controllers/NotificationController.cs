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

        [HttpGet]
        public IActionResult GetLatestUnread()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return Json(new List<object>());
            var myId = int.Parse(userIdClaim.Value);

            var notifs = _context.Notifications
                .Where(n => n.UserId == myId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .Take(3)
                .Select(n => new {
                    n.Id,
                    message = n.Message,
                    fromUsername = n.FromUsername,
                    fromUserImage = n.FromUserImage,
                    link = "/Message/Inbox",
                    n.CreatedAt
                })
                .ToList();

            return Json(notifs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkAllRead()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var notifs = _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToList();
            foreach (var n in notifs)
                n.IsRead = true;
            _context.SaveChanges();
            return RedirectToAction("Index");
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