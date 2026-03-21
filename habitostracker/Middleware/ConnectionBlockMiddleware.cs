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

        // 🔎 verificar usuario autenticado
        if (user.Identity != null && user.Identity.IsAuthenticated)
        {
            var userIdClaim = user.FindFirst("UserId");

            if (userIdClaim != null)
            {
                var userId = int.Parse(userIdClaim.Value);

                var dbUser = db.Users.FirstOrDefault(u => u.Id == userId);

                // 🚫 CUENTA ELIMINADA
                if (dbUser == null)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?deleted=true");
                    return;
                }
                // 🚫 USUARIO BANEADO
                if (dbUser.IsBanned)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?banned=true");
                    return;
                }

                // ⚠ USUARIO DESACTIVADO
                if (!dbUser.IsActive)
                {
                    await context.SignOutAsync("Cookies");
                    context.Response.Redirect("/Account/Login?deactivated=true");
                    return;
                }
            }
        }

        // 🔎 obtener IP real
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (!string.IsNullOrEmpty(ip))
        {
            ip = ip.Split(',').First().Trim();
        }
        else
        {
            ip = context.Connection.RemoteIpAddress?.ToString();
        }

        var path = context.Request.Path.Value;

        // 🛡 permitir siempre al SuperAdmin
        if (user.Identity != null && user.Identity.IsAuthenticated && user.IsInRole("SuperAdmin"))
        {
            await _next(context);
            return;
        }

        // 🚫 IP bloqueada
        var blockedIp = db.BlockedIPs.FirstOrDefault(x => x.IpAddress == ip);

        if (blockedIp != null)
        {
            if (!path.Contains("/Account/Login"))
            {
                context.Response.Redirect("/Account/Login?ipblocked=true");
                return;
            }
        }

        var block = db.ConnectionBlocks.FirstOrDefault();

        // permitir login y registro
        if (path.Contains("/Account/Login") || path.Contains("/Account/Register"))
        {
            await _next(context);
            return;
        }

        // permitir página de bloqueo
        if (path.Contains("/Home/ConnectionBlocked"))
        {
            await _next(context);
            return;
        }

        // desbloqueo automático si entra SuperAdmin
        if (user.Identity != null && user.Identity.IsAuthenticated && user.IsInRole("SuperAdmin"))
        {
            if (block != null && block.IsBlocked)
            {
                block.IsBlocked = false;
                db.SaveChanges();
            }

            await _next(context);
            return;
        }

        // 🚫 bloqueo global
        if (block != null && block.IsBlocked)
        {
            context.Response.Redirect("/Home/ConnectionBlocked");
            return;
        }

        await _next(context);
    }
}