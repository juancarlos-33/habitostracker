using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Authentication;

public class ConnectionBlockMiddleware
{
    private readonly RequestDelegate _next;
    public ConnectionBlockMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, HabitDbContext db)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // ── Rutas siempre libres ──
        if (path.StartsWith("/account/login") ||
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

        // ── IP real ──
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(ip))
        {
            ip = ip.Split(',').First().Trim();
            if (ip.Contains("::ffff:")) ip = ip.Replace("::ffff:", "");
            if (ip == "::1") ip = "127.0.0.1";
        }
        Console.WriteLine("🌐 IP FINAL: " + ip);

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
                    if (!string.IsNullOrEmpty(ip)) { dbUser.LastIp = ip; db.SaveChanges(); }
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

                // SuperAdmin pasa sin más verificaciones
                if (dbUser.Role == "SuperAdmin")
                {
                    await _next(context);
                    return;
                }

                // Actualizar IP si cambió
                if (dbUser.LastIp != ip && !string.IsNullOrEmpty(ip))
                {
                    dbUser.LastIp = ip;
                    db.SaveChanges();
                }
            }
        }
        else
        {
            // ── No autenticado — bloquear rutas sensibles por IP ──
            var blockedPaths = new[] {
                "/account/register",
                "/account/guestregister",
                "/account/forgotpassword",
                "/account/externallogin",
                "/account/completeprofile"
            };

            if (!string.IsNullOrEmpty(ip) && blockedPaths.Any(p => path.StartsWith(p)))
            {
                var blockedUser = db.Users.FirstOrDefault(u => u.LastIp == ip && u.IsIpBlocked);
                if (blockedUser != null)
                {
                    context.Response.Redirect("/Account/Login?blocked=true");
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