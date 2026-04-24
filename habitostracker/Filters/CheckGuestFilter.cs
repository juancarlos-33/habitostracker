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

        public CheckGuestFilter(HabitDbContext context) => _context = context;

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
            var action = context.RouteData.Values["action"]?.ToString() ?? "";

            // Saltar filtro en Account y Home para evitar loops
            if (controller.Equals("Account", StringComparison.OrdinalIgnoreCase) ||
                controller.Equals("Home", StringComparison.OrdinalIgnoreCase)) return;

            var isGuest = context.HttpContext.User.FindFirst("IsGuest")?.Value == "true";

            if (isGuest)
            {
                bool allowed =
                    (controller.Equals("Habit", StringComparison.OrdinalIgnoreCase) && _allowedHabitActions.Contains(action)) ||
                    (controller.Equals("Account", StringComparison.OrdinalIgnoreCase) && _allowedAccountActions.Contains(action));

                if (!allowed)
                    context.Result = new RedirectToActionResult("Index", "Habit", new { showGuestModal = true });

                return;
            }
        }

        public void OnActionExecuted(ActionExecutingContext context) { }
        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}