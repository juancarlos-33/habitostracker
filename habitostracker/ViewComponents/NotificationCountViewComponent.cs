using Microsoft.AspNetCore.Mvc;
using HabitTrackerApp.Data;
using System.Linq;
using System.Security.Claims;

namespace HabitTrackerApp.ViewComponents
{
    public class NotificationCountViewComponent : ViewComponent
    {
        private readonly HabitDbContext _context;

        public NotificationCountViewComponent(HabitDbContext context)
        {
            _context = context;
        }

        public IViewComponentResult Invoke()
        {
            int unread = 0;

            try
            {
                if (User.Identity.IsAuthenticated && HttpContext.User.FindFirst("UserId") != null)
                {
                    var userId = int.Parse(HttpContext.User.FindFirst("UserId").Value);

                    unread = _context.Notifications
                        .Where(n => n.UserId == userId && !n.IsRead)
                        .Count();
                }
            }
            catch
            {
                unread = 0; // 🔥 evita que crashee
            }

            return View(unread);
        }
    }
}