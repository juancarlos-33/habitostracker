using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HabitTrackerApp.Filters
{
    public class CheckGuestFilter : IActionFilter
    {
        private readonly HabitDbContext _context;

        private static readonly HashSet<string> _allowedHabitActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "Index", "Create", "History", "Calendar"
        };

        private static readonly HashSet<string> _allowedAccountActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "Login", "Logout", "GuestRegister", "GuestLogin", "GuestLoginExisting", "UpgradeAccount"
        };

        public CheckGuestFilter(HabitDbContext context)
        {
            _context = context;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var isGuest = context.HttpContext.User.FindFirst("IsGuest")?.Value == "true";
            var userIdClaim = context.HttpContext.User.FindFirst("UserId");

            if (isGuest)
            {
                var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
                var action = context.RouteData.Values["action"]?.ToString() ?? "";

                bool allowed =
                    (controller.Equals("Habit", StringComparison.OrdinalIgnoreCase) && _allowedHabitActions.Contains(action)) ||
                    (controller.Equals("Account", StringComparison.OrdinalIgnoreCase) && _allowedAccountActions.Contains(action));

                if (!allowed)
                {
                    context.Result = new RedirectToActionResult("Index", "Habit", new { showGuestModal = true });
                }
                return;
            }

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

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}