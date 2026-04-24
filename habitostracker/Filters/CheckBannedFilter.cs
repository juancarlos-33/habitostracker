using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

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
            // Saltar filtro en Account y Home para evitar loops
            var controller = context.RouteData.Values["controller"]?.ToString();
            if (controller == "Account" || controller == "Home") return;

            var userIdClaim = context.HttpContext.User.FindFirst("UserId");
            if (userIdClaim == null) return;

            int userId = int.Parse(userIdClaim.Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null) return;

            if (!user.IsActive || user.IsBanned || user.IsIpBlocked)
            {
                _ = context.HttpContext.SignOutAsync();
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // Refrescar foto si está vacía
            var currentProfileImage = context.HttpContext.User.FindFirst("ProfileImage")?.Value;
            var realPhoto = user.ProfileImage ?? user.ProfilePicture ?? "";

            if (string.IsNullOrEmpty(currentProfileImage) && !string.IsNullOrEmpty(realPhoto))
            {
                var claims = new List<Claim>
                {
                    new Claim("UserId", user.Id.ToString()),
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username ?? "Usuario"),
                    new Claim(ClaimTypes.Role, user.Role ?? "User"),
                    new Claim("ProfileImage", realPhoto)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                _ = context.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}