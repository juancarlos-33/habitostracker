using HabitTrackerApp.Data;
using HabitTrackerApp.Filters;
using HabitTrackerApp.Hubs;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace HabitTrackerApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSession();

            // 🔥 Base de datos
            builder.Services.AddDbContext<HabitDbContext>(options =>
               options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // 🔥 Filtro
            builder.Services.AddScoped<CheckBannedFilter>();

            builder.Services.AddControllersWithViews(options =>
            {
                options.Filters.Add<CheckBannedFilter>();

                options.Filters.Add<CheckGuestFilter>();
            });
        

            // 🔐 AUTENTICACIÓN
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
              
            })


            .AddGoogle(options =>
            {
               options.ClientId = builder.Configuration["Google:ClientId"];
options.ClientSecret = builder.Configuration["Google:ClientSecret"];
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
                };
            });

            // 🔥 SignalR
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<OnlineUsersService>();
            builder.Services.AddSingleton<IUserIdProvider, HabitTrackerApp.Hubs.CustomUserIdProvider>();
            builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, HabitTrackerApp.UserIdProvider>();

            // 🔥 Email
            builder.Services.AddScoped<EmailService>();

            // 🔥 Memoria
            builder.Services.AddDistributedMemoryCache();

            // 🔥 Swagger
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
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] {}
                    }
                });
            });

            var app = builder.Build();
            app.UseSession();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            if (!app.Environment.IsDevelopment())
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

            app.UseMiddleware<ConnectionBlockMiddleware>();

            app.UseAuthorization();

            // 🔥 BLOQUEO USUARIO
            app.Use(async (context, next) =>
            {
                if (context.User.Identity != null && context.User.Identity.IsAuthenticated)
                {
                    var userIdClaim = context.User.FindFirst("UserId");

                    if (userIdClaim != null)
                    {
                        var userId = int.Parse(userIdClaim.Value);

                        using (var scope = context.RequestServices.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<HabitDbContext>();
                            var user = db.Users.FirstOrDefault(u => u.Id == userId);

                            if (user != null && (!user.IsActive || user.IsBanned))
                            {
                                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                                context.Response.Redirect("/Account/Login");
                                return;
                            }
                        }
                    }
                }

                await next();
            });

            // 🔥 NOTIFICACIONES
            app.Use(async (context, next) =>
            {
                if (context.User.Identity != null && context.User.Identity.IsAuthenticated)
                {
                    var db = context.RequestServices.GetRequiredService<HabitTrackerApp.Data.HabitDbContext>();

                    var userIdClaim = context.User.FindFirst("UserId");

                    if (userIdClaim != null)
                    {
                        var userId = int.Parse(userIdClaim.Value);

                        var count = db.Notifications
                            .Count(n => n.UserId == userId && !n.IsRead);

                        context.Items["NewNotifications"] = count;
                    }
                }

                await next();
            });

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Habit}/{action=Index}/{id?}");

            app.MapHub<HabitTrackerApp.Hubs.ChatHub>("/chatHub");

            // 🔥 Auto-migración al arrancar
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HabitDbContext>();
                db.Database.Migrate();
            }

            app.Run();
        }
    }
}
