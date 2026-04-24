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
            path.StartsWith("/account/login") ||
            path.StartsWith("/home/privacy") ||
            path.StartsWith("/home/error") ||
            path.StartsWith("/home/connectionblocked") ||
            path.StartsWith("/signin-google") ||
            path.StartsWith("/chathub") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/favicon"))
        {
            await _next(context);
            return;
        }

        var user = context.User;

        // ── Usuario autenticado ──
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
                    context.Response.Cookies.Delete(".AspNetCore.Cookies");
                    context.Response.Cookies.Delete(".AspNetCore.Antiforgery");
                    if (!path.StartsWith("/account/login"))
                    {
                        context.Response.Redirect("/Account/Login?blocked=true");
                        return;
                    }
                    await _next(context);
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

                if (dbUser.Role == "SuperAdmin")
                {
                    await _next(context);
                    return;
                }
            }
        }
        // No autenticado — no verificar por IP (Railway usa IPs internas compartidas)
        // El bloqueo se hace en Login/Register/Google POST directamente

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