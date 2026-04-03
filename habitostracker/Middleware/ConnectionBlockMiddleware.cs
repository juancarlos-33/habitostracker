using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authentication;

public class ConnectionBlockMiddleware
{
    private readonly RequestDelegate _next;

    public ConnectionBlockMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, HabitDbContext db)
    {
        var user = context.User;
        var path = context.Request.Path.Value.ToLower();

        // 🔥 RUTAS PERMITIDAS
        if (path.Contains("/account/login") ||
            path.Contains("/account/register") ||
            path.Contains("/signin-google") ||
            path.Contains("/home/connectionblocked") ||
            path.Contains("/css") ||
            path.Contains("/js") ||
            path.Contains("/lib"))
        {
            await _next(context);
            return;
        }

        // 🔎 IP REAL NORMALIZADA (ngrok fix)
        var ip = context.Connection.RemoteIpAddress?.ToString();

        if (!string.IsNullOrEmpty(ip))
        {
            if (ip.Contains("::ffff:"))
                ip = ip.Replace("::ffff:", "");

            if (ip == "::1")
                ip = "127.0.0.1";
        }

        Console.WriteLine("🌐 IP FINAL: " + ip);

        // 🔎 VALIDAR USUARIO (🔥 PRIORIDAD REAL)
        if (user.Identity != null && user.Identity.IsAuthenticated)
        {
            var userIdClaim = user.FindFirst("UserId");

            if (userIdClaim != null)
            {
                var userId = int.Parse(userIdClaim.Value);
                var dbUser = db.Users.FirstOrDefault(u => u.Id == userId);

                // 🚫 BLOQUEO POR USUARIO (🔥 ESTE MANDA)
                if (dbUser != null && dbUser.IsIpBlocked)
                {
                    Console.WriteLine("🚫 BLOQUEADO POR USUARIO: " + userId);

                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?ipblocked=true");
                    return;
                }

                // 🚫 CUENTA ELIMINADA
                if (dbUser == null)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?deleted=true");
                    return;
                }

                // 🚫 BANEADO
                if (dbUser.IsBanned)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?banned=true");
                    return;
                }

                // 🚫 DESACTIVADO
                if (!dbUser.IsActive)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?deactivated=true");
                    return;
                }

                // 🔥 GUARDAR IP (solo info)
                if (dbUser.LastIp != ip)
                {
                    dbUser.LastIp = ip;
                    db.SaveChanges();
                }
            }
        }

        // 🛡 SUPERADMIN
        if (user.Identity != null &&
            user.Identity.IsAuthenticated &&
            user.IsInRole("SuperAdmin"))
        {
            await _next(context);
            return;
        }

        // 🚫 BLOQUEO GLOBAL
        var block = db.ConnectionBlocks.FirstOrDefault();

        if (block != null && block.IsBlocked)
        {
            if (user.Identity != null && user.Identity.IsAuthenticated)
            {
                context.Response.Redirect("/Home/ConnectionBlocked");
                return;
            }

            await _next(context);
            return;
        }

        await _next(context);
    }
}