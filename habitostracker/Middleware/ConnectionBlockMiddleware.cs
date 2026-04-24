using HabitTrackerApp.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

public class ConnectionBlockMiddleware
{
    private readonly RequestDelegate _next;
    public ConnectionBlockMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, HabitDbContext db)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // Rutas libres
        if (path.StartsWith("/account/") ||
            path.StartsWith("/home/") ||
            path.StartsWith("/signin-google") ||
            path.StartsWith("/chathub") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/images") ||
            path.StartsWith("/favicon"))
        {
            // Aun en rutas libres, si está autenticado y bloqueado, borrar cookie
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userIdClaim2 = context.User.FindFirst("UserId");
                if (userIdClaim2 != null && int.TryParse(userIdClaim2.Value, out int uid2))
                {
                    var dbUser2 = db.Users.FirstOrDefault(u => u.Id == uid2);
                    if (dbUser2 != null && dbUser2.IsIpBlocked)
                    {
                        // Borrar cookie manualmente
                        foreach (var cookie in context.Request.Cookies.Keys)
                            context.Response.Cookies.Delete(cookie);
                        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        // No redirigir si ya estamos en login
                        if (!path.StartsWith("/account/login"))
                        {
                            context.Response.Redirect("/Account/Login?blocked=true");
                            return;
                        }
                    }
                }
            }
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
                    foreach (var cookie in context.Request.Cookies.Keys)
                        context.Response.Cookies.Delete(cookie);
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/Account/Login?deleted=true");
                    return;
                }

                if (dbUser.IsIpBlocked)
                {
                    foreach (var cookie in context.Request.Cookies.Keys)
                        context.Response.Cookies.Delete(cookie);
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/Account/Login?blocked=true");
                    return;
                }

                if (dbUser.IsBanned)
                {
                    foreach (var cookie in context.Request.Cookies.Keys)
                        context.Response.Cookies.Delete(cookie);
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/Account/Login?banned=true");
                    return;
                }

                if (!dbUser.IsActive)
                {
                    foreach (var cookie in context.Request.Cookies.Keys)
                        context.Response.Cookies.Delete(cookie);
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/Account/Login?deactivated=true");
                    return;
                }
            }
        }

        // Bloqueo global
        var block = db.ConnectionBlocks.FirstOrDefault();
        if (block != null && block.IsBlocked && user.Identity?.IsAuthenticated == true)
        {
            context.Response.Redirect("/Home/ConnectionBlocked");
            return;
        }

        await _next(context);
    }
}