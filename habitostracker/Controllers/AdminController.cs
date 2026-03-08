using HabitTrackerApp.Data;
using HabitTrackerApp.Hubs;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApp.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        public AdminController(HabitDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public IActionResult Index()
        {
            var totalUsers = _context.Users.Count();
            var totalHabits = _context.Habits.Count();
            var totalMessages = _context.Messages.Count();

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalHabits = totalHabits;
            ViewBag.TotalMessages = totalMessages;

            return View();
        }

        public IActionResult Users()
        {
            var users = _context.Users.ToList();

            // 📊 estadísticas
            ViewBag.TotalUsers = users.Count;
            ViewBag.BannedUsers = users.Count(u => u.IsBanned);
            ViewBag.DisabledUsers = users.Count(u => !u.IsActive);
            ViewBag.ActiveUsers = users.Count(u => u.LastOnline != null && (DateTime.Now - u.LastOnline.Value).TotalSeconds < 60);

            return View(users);
        }

        public IActionResult Habits()
        {
            var habits = _context.Habits
                .Include(h => h.User)
                .ToList();

            return View(habits);
        }

        public IActionResult Messages()
        {
            var messages = _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Receiver)
                .OrderByDescending(m => m.SentAt)
                .ToList();

            return View(messages);
        }

        public IActionResult Statistics()
        {
            var totalUsers = _context.Users.Count();
            var totalHabits = _context.Habits.Count();
            var totalMessages = _context.Messages.Count();

            var totalFeedbacks = _context.Feedbacks
        .Count(f => !f.IsRead);

            ViewBag.TotalFeedbacks = totalFeedbacks;

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalHabits = totalHabits;
            ViewBag.TotalMessages = totalMessages;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            // ❌ evitar auto eliminarse
            if (id == myId)
            {
                TempData["Error"] = "No puedes desactivar tu propia cuenta.";
                return RedirectToAction("Users");
            }

            var currentUser = _context.Users.FirstOrDefault(u => u.Id == myId);
            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            // ❌ nadie puede eliminar al propietario
            if (user.Role == "SuperAdmin")
            {
                TempData["Error"] = "No puedes eliminar al propietario del sistema.";
                return RedirectToAction("Users");
            }

            // ❌ un admin no puede eliminar a otro admin
            if (currentUser.Role != "SuperAdmin" && user.Role == "Admin")
            {
                TempData["Error"] = "No puedes eliminar a otro administrador.";
                return RedirectToAction("Users");
            }

            user.IsActive = false;

            var log = new AdminLog
            {
                AdminId = currentUser.Id,
                AdminName = currentUser.Username,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Desactivar usuario",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            // 🔔 avisar al usuario desactivado
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ForceLogout", "Tu cuenta fue desactivada por un administrador bro :(");

            return RedirectToAction("Users");
        }

        [HttpGet]
        public async Task<IActionResult> BanUser(int id)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            // ❌ evitar autobanearse
            if (id == myId)
            {
                TempData["Error"] = "No puedes banear tu propia cuenta.";
                return RedirectToAction("Users");
            }

            var currentUser = _context.Users.FirstOrDefault(u => u.Id == myId);
            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            if (user.Role == "SuperAdmin")
            {
                TempData["Error"] = "No puedes banear al propietario.";
                return RedirectToAction("Users");
            }

            if (currentUser.Role != "SuperAdmin" && user.Role == "Admin")
            {
                TempData["Error"] = "No puedes banear a otro administrador.";
                return RedirectToAction("Users");
            }

            user.IsBanned = true;

            var log = new AdminLog
            {
                AdminId = currentUser.Id,
                AdminName = currentUser.Username,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Banear usuario",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            // 🔔 avisar al usuario baneado
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ForceLogout", "Tu cuenta ha sido baneada por un administrador bro :(");

            return RedirectToAction("Users");
        }
        [HttpGet]
        public IActionResult UnbanUser(int id)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            user.IsBanned = false;

            _context.SaveChanges();

            return RedirectToAction("Users");
        }

        // 👑 HACER ADMIN
        [HttpGet]
        public async Task<IActionResult> MakeAdmin(int id)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            user.Role = "Admin";

            var myId = int.Parse(User.FindFirst("UserId").Value);
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == myId);

            var log = new AdminLog
            {
                AdminId = currentUser.Id,
                AdminName = currentUser.Username,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Hacer administrador",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            // 🔔 avisar al usuario que su rol cambió
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ForceLogout", "Ahora eres Admin bro" +
                "Inicia Sesion Nuevamente");

            return RedirectToAction("Users");
        }

        [HttpGet]
        public IActionResult ReactivateUser(int id)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            user.IsActive = true;

            var myId = int.Parse(User.FindFirst("UserId").Value);
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == myId);

            var log = new AdminLog
            {
                AdminId = currentUser.Id,
                AdminName = currentUser.Username,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Reactivar usuario",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            return RedirectToAction("Users");
        }

        // 👤 QUITAR ADMIN
        [HttpGet]
        public async Task<IActionResult> RemoveAdmin(int id)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var myRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            if (user.Role == "SuperAdmin")
            {
                TempData["Error"] = "No puedes modificar los permisos del propietario.";
                return RedirectToAction("Users");
            }

            // ❌ no puedes modificarte a ti mismo
            if (id == myId)
            {
                TempData["Error"] = "No puedes quitarte admin a ti mismo.";
                return RedirectToAction("Users");
            }

            // ❌ solo el SuperAdmin puede quitar admins
            if (user.Role == "Admin" && myRole != "SuperAdmin")
            {
                TempData["Error"] = "Solo el SuperAdmin puede quitar administradores.";
                return RedirectToAction("Users");
            }

            user.Role = "User";

            var log = new AdminLog
            {
                AdminId = int.Parse(User.FindFirst("UserId").Value),
                AdminName = User.Identity.Name,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Quitar administrador",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            // 🔔 avisar al usuario que perdió admin
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ForceLogout", "Ya no eres Admin bro" +
                "Inicia Sesion Nuevamente.");

            return RedirectToAction("Users");
        }

        public IActionResult Feedbacks()
        {
            var feedbacks = _context.Feedbacks
                .Include(f => f.User)
                .OrderByDescending(f => f.CreatedAt)
                .ToList();

            // marcar todos como leídos
            foreach (var f in feedbacks)
            {
                f.IsRead = true;
            }

            _context.SaveChanges();

            return View(feedbacks);
        }
    

    public IActionResult AdminLogs()
        {
            var logs = _context.AdminLogs
                .OrderByDescending(l => l.CreatedAt)
                .ToList();

            return View(logs);
        }
    } }