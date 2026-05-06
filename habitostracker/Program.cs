using HabitTrackerApp.Data;
using HabitTrackerApp.Filters;
using HabitTrackerApp.Hubs;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

namespace HabitTrackerApp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 104857600;
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 104857600;
            });
            builder.Services.AddSession();

            builder.Services.AddDbContext<HabitDbContext>(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                if (builder.Environment.IsProduction())
                    options.UseNpgsql(connectionString);
                else
                    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            });

            builder.Services.AddScoped<CheckBannedFilter>();
            builder.Services.AddControllersWithViews(options =>
            {
                options.Filters.Add<CheckBannedFilter>();
                options.Filters.Add<CheckGuestFilter>();
            });

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Cookies";
                options.DefaultChallengeScheme = "Cookies";
                options.DefaultSignInScheme = "Cookies";
            })
            .AddCookie("Cookies", options =>
            {
                options.AccessDeniedPath = "/Account/Login";
                options.LoginPath = "/Account/Login";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                // 🔒 hardening de cookies de auth
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.Cookie.SecurePolicy = builder.Environment.IsProduction()
                    ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
                    : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            })
            .AddGoogle(options =>
            {
                options.ClientId = builder.Configuration["Google:ClientId"] ?? "";
                options.ClientSecret = builder.Configuration["Google:ClientSecret"] ?? "";
                options.SignInScheme = "Cookies";
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var jwtKey = builder.Configuration["Jwt:Key"];
                if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
                {
                    Console.WriteLine("⚠️  Jwt:Key no configurada o muy corta. Configura una clave fuerte (>=32 chars) en appsettings.Development.json (dev), User Secrets, o env var Jwt__Key (prod).");
                    // placeholder para que la app arranque; los JWT no funcionarán hasta que configures la clave real
                    jwtKey = "DEV_PLACEHOLDER_NO_USAR_EN_PROD_" + Guid.NewGuid().ToString("N");
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };
            });

            builder.Services.AddSignalR();
            builder.Services.AddSingleton<OnlineUsersService>();
            builder.Services.AddSingleton<IUserIdProvider, HabitTrackerApp.Hubs.CustomUserIdProvider>();
            builder.Services.AddScoped<EmailService>();
            builder.Services.AddScoped<CloudinaryService>();
            builder.Services.AddDistributedMemoryCache();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "HabitTracker API", Version = "v1" });
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Introduce el token así: Bearer {tu_token}"
                });
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                        new string[] {}
                    }
                });
            });

            builder.Services.AddDataProtection().SetApplicationName("habitostracker");

            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
            {
                o.MultipartBodyLengthLimit = 524288000;
            });
            builder.WebHost.ConfigureKestrel(o =>
            {
                o.Limits.MaxRequestBodySize = 524288000;
            });

            var app = builder.Build();
            app.UseSession();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                KnownNetworks = { },
                KnownProxies = { }
            });

            app.UseRouting();
            app.UseAuthentication();

            // ── Sesión activa ──
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value?.ToLower() ?? "";
                if (!path.StartsWith("/account/") && !path.StartsWith("/home/") &&
                    context.User.Identity?.IsAuthenticated == true)
                {
                    var sessionToken = context.User.FindFirst("SessionToken")?.Value;
                    if (!string.IsNullOrEmpty(sessionToken))
                    {
                        using var scope = context.RequestServices.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<HabitDbContext>();
                        var session = db.UserSessions.FirstOrDefault(s => s.SessionToken == sessionToken);
                        if (session == null || !session.IsActive)
                        {
                            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            context.Response.Redirect("/Account/Login");
                            return;
                        }
                    }
                }
                await next();
            });

            // ── Bloqueo conexión y seguridad ──
            app.UseMiddleware<ConnectionBlockMiddleware>();

            app.UseAuthorization();

            // ── Notificaciones ──
            app.Use(async (context, next) =>
            {
                if (context.User.Identity?.IsAuthenticated == true)
                {
                    var userIdClaim = context.User.FindFirst("UserId");
                    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
                    {
                        var db = context.RequestServices.GetRequiredService<HabitDbContext>();
                        var count = db.Notifications.Count(n => n.UserId == userId && !n.IsRead);
                        context.Items["NewNotifications"] = count;
                    }
                }
                await next();
            });

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Habit}/{action=Index}/{id?}");

            app.MapHub<HabitTrackerApp.Hubs.ChatHub>("/chatHub");

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HabitDbContext>();
                db.Database.Migrate();
            }

            app.Run();
        }
    }
}