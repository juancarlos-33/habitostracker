using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authentication;

namespace HabitTrackerApp.Filters
{
    public class CheckBannedFilter : IActionFilter
    {
        private readonly HabitDbContext _context;

        public CheckBannedFilter(HabitDbContext context)
        {
            _context = context;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
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