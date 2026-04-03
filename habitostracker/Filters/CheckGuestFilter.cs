using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HabitTrackerApp.Filters
{
    public class CheckGuestFilter : IActionFilter
    {
        private readonly HabitDbContext _context;

        public CheckGuestFilter(HabitDbContext context)
        {
            _context = context;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            // 🔥 DETECTAR INVITADO
            var isGuest = context.HttpContext.User.FindFirst("IsGuest")?.Value == "true";

            if (isGuest)
            {
                return; // 🚀 NO validar invitados
            }

            var userIdClaim = context.HttpContext.User.FindFirst("UserId");

            if (userIdClaim != null)
            {
                int userId = int.Parse(userIdClaim.Value);

                var user = _context.Users.FirstOrDefault(u => u.Id == userId);

                if (user != null)
                {
                    if (!user.IsActive)
                    {
                        context.HttpContext.SignOutAsync();
                        context.Result = new RedirectToActionResult("Login", "Account", null);
                        return;
                    }

                    if (user.IsBanned)
                    {
                        context.HttpContext.SignOutAsync();
                        context.Result = new RedirectToActionResult("Login", "Account", null);
                        return;
                    }
                }
            }
        }
        
        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}