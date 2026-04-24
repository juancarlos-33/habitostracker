using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Authentication;

public class ConnectionBlockMiddleware
{
    private readonly RequestDelegate _next;
    public ConnectionBlockMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, HabitDbContext db)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";
        Console.WriteLine($"🔍 PATH: {path} | Auth: {context.User.Identity?.IsAuthenticated}");

        // ── Rutas siempre libres ──
        if (path == "/" ||
            path.StartsWith("/account/") ||
            path.StartsWith("/home/") ||
            path.StartsWith("/signin-google") ||
            path.StartsWith("/chathub") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/images") ||
            path.StartsWith("/favicon"))
        {
            await _next(context);
            return;
        }

        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = user.FindFirst("UserId");
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                var dbUser = db.Users.FirstOrDefault(u => u.Id == userId);

                if (dbUser == null)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?deleted=true");
                    return;
                }

                if (dbUser.IsIpBlocked)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?blocked=true");
                    return;
                }

                if (dbUser.IsBanned)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?banned=true");
                    return;
                }

                if (!dbUser.IsActive)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?deactivated=true");
                    return;
                }
            }
        }

        // ── Bloqueo global ──
        var block = db.ConnectionBlocks.FirstOrDefault();
        if (block != null && block.IsBlocked && user.Identity?.IsAuthenticated == true)
        {
            context.Response.Redirect("/Home/ConnectionBlocked");
            return;
        }

        await _next(context);
    }
}