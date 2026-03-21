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

namespace HabitTrackerApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 🔥 Base de datos
            builder.Services.AddDbContext<HabitDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection")));

            // 🔥 Registrar filtro de baneo
            builder.Services.AddScoped<CheckBannedFilter>();

            // 🔥 MVC + filtro global
            builder.Services.AddControllersWithViews(options =>
            {
                options.Filters.Add<CheckBannedFilter>();
            });

            builder.Services.AddSignalR();


            builder.Services.AddSingleton<OnlineUsersService>();



            builder.Services.AddSingleton<IUserIdProvider, HabitTrackerApp.Hubs.CustomUserIdProvider>();

            builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, HabitTrackerApp.UserIdProvider>();

            // 🔥 Registrar EmailService
            builder.Services.AddScoped<EmailService>();

            // 🔥 Memoria para sesiones
            builder.Services.AddDistributedMemoryCache();

            // 🔐 Autenticación híbrida (Cookies + JWT)
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Cookies";
                options.DefaultChallengeScheme = "Cookies";
            })
            .AddCookie("Cookies", options =>
            {
                options.LoginPath = "/Account/Login";
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                options.SessionStore = new MemoryCacheTicketStore();
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

            // 🔥 Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "HabitTracker API",
                    Version = "v1"
                });

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

            app.UseRouting();

            app.UseAuthentication();
            app.UseMiddleware<ConnectionBlockMiddleware>();
            app.UseAuthorization();
            app.Use(async (context, next) =>
            {
                if (context.User.Identity != null && context.User.Identity.IsAuthenticated)
                {
                    var userId = context.User.FindFirst("UserId")?.Value;

                    if (userId != null)
                    {
                        using (var scope = context.RequestServices.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<HabitDbContext>();

                            var user = db.Users.FirstOrDefault(u => u.Id == int.Parse(userId));

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

            app.Use(async (context, next) =>
            {
                if (context.User.Identity != null && context.User.Identity.IsAuthenticated)
                {
                    var db = context.RequestServices.GetRequiredService<HabitTrackerApp.Data.HabitDbContext>();

                    var userId = int.Parse(context.User.FindFirst("UserId").Value);

                    var count = db.Notifications
                        .Count(n => n.UserId == userId && !n.IsRead);

                    context.Items["NewNotifications"] = count;
                }

                await next();
            });


            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Habit}/{action=Index}/{id?}");

            app.MapHub<HabitTrackerApp.Hubs.ChatHub>("/chatHub");

            app.Run();
        }
    }
}