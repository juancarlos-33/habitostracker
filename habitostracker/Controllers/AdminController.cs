using HabitTrackerApp.Data;
using HabitTrackerApp.Hubs;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace HabitTrackerApp.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly EmailService _emailService;

        public AdminController(HabitDbContext context, IHubContext<ChatHub> hubContext, EmailService emailService)
        {
            _context = context;
            _hubContext = hubContext;
            _emailService = emailService;
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



        public IActionResult Conversation(int user1, int user2)
        {
            var messages = _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Receiver)
                .Where(m =>
                    (m.SenderId == user1 && m.ReceiverId == user2) ||
                    (m.SenderId == user2 && m.ReceiverId == user1))
                .OrderBy(m => m.SentAt)
                .ToList();

            ViewBag.User1 = user1;
            ViewBag.User2 = user2;

            return View(messages);
        }
        public async Task<IActionResult> Users()
        {
            var users = _context.Users.ToList();


            // 📊 estadísticas
            ViewBag.TotalUsers = users.Count;
            ViewBag.BannedUsers = users.Count(u => u.IsBanned);
            ViewBag.DisabledUsers = users.Count(u => !u.IsActive);
            ViewBag.ActiveUsers = users.Count(u => u.LastOnline != null &&
                (DateTime.Now - u.LastOnline.Value).TotalSeconds < 60);

            // 🌍 Obtener país e ISP de cada IP
            var ipInfo = new Dictionary<string, (string country, string city, string isp)>();

            foreach (var user in users)
            {
                if (!string.IsNullOrEmpty(user.LastIp) && !ipInfo.ContainsKey(user.LastIp))
                {
                    var info = await GetIPInfo(user.LastIp);
                    ipInfo[user.LastIp] = info;
                }
            }

            ViewBag.IpInfo = ipInfo;

            // 🚫 IPs bloqueadas
            ViewBag.BlockedIps = _context.BlockedIPs
                .Select(x => x.IpAddress)
                .ToList();

            // ⚠ detectar múltiples cuentas por IP
            var multiAccounts = users
                .Where(u => !string.IsNullOrEmpty(u.LastIp))
                .GroupBy(u => u.LastIp)
                .Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key, g => g.Count());

            ViewBag.MultiAccounts = multiAccounts;

            return View(users);
        }


        [HttpPost]
        public async Task<IActionResult> ResetPassword(int userId)
        {
            var adminId = int.Parse(User.FindFirst("UserId").Value);
            var admin = _context.Users.FirstOrDefault(u => u.Id == adminId);

            // 🔒 solo SuperAdmin puede hacerlo
            if (admin.Role != "SuperAdmin")
                return Unauthorized();

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return NotFound();

            // generar nueva contraseña
            var newPassword = Guid.NewGuid().ToString().Substring(0, 8);

            // encriptar contraseña
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

            _context.SaveChanges();

            // 📧 enviar correo al usuario
            await _emailService.SendEmailAsync(
     user.Email,
     "HabitTracker - Tu contraseña fue restablecida",
     $@"
    <div style='font-family:Arial,Helvetica,sans-serif;background:#f4f6f8;padding:40px'>

        <div style='max-width:500px;margin:auto;background:white;border-radius:10px;
                    box-shadow:0 8px 25px rgba(0,0,0,0.1);overflow:hidden'>

            <div style='background:#2563eb;color:white;padding:20px;text-align:center;
                        font-size:22px;font-weight:bold'>
                HabitTracker
            </div>

            <div style='padding:30px;color:#333'>

                <h2 style='margin-top:0'>Hola {user.Username} 👋</h2>

                <p>
                Un administrador ha restablecido tu contraseña.
                </p>

                <p>
                Usa la siguiente contraseña para iniciar sesión:
                </p>

                <div style='background:#f1f5f9;
                            padding:15px;
                            border-radius:8px;
                            font-size:20px;
                            font-weight:bold;
                            text-align:center;
                            letter-spacing:2px;
                            margin:20px 0;
                            color:#111'>
                    {newPassword}
                </div>

                <p style='font-size:14px;color:#555'>
                Por seguridad, te recomendamos cambiar tu contraseña después de iniciar sesión.
                </p>

                <div style='margin-top:30px;font-size:12px;color:#888;text-align:center'>
                    © HabitTracker
                </div>
 <div style='margin-top:30px;font-size:12px;color:#888;text-align:center'>
                    Este es un correo automático, no respondas a este mensaje.
                </div>

            </div>

        </div>

    </div>
    "
 );

            TempData["NewPassword"] = newPassword;

            return RedirectToAction("Users");
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
                .Take(500)
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
                AdminProfileImage = currentUser.ProfileImage,
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
                AdminProfileImage = currentUser.ProfileImage,
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

        [HttpPost]
        public async Task<IActionResult> BanAllFromIP(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return RedirectToAction("Users");

            var users = _context.Users
                .Where(u => u.LastIp == ip)
                .ToList();

            foreach (var user in users)
            {
                if (user.Role == "SuperAdmin") continue;

                user.IsBanned = true;

                await _hubContext.Clients.User(user.Id.ToString())
                    .SendAsync("ForceLogout", "Tu cuenta fue baneada por uso de múltiples cuentas");
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Users");
        }

        [HttpGet]
        public async Task<IActionResult> UnbanUser(int id)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            user.IsBanned = false;

            var myId = int.Parse(User.FindFirst("UserId").Value);

            var currentUser = _context.Users.FirstOrDefault(u => u.Id == myId);

            var log = new AdminLog
            {
                AdminId = currentUser.Id,
                AdminName = currentUser.Username,
                AdminProfileImage = currentUser.ProfileImage,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Desbanear usuario",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            // 🔔 avisar al usuario que fue desbaneado
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ReceiveNotification", "Tu cuenta ha sido desbaneada por un administrador.");

            return RedirectToAction("Users");
        }




        public async Task<IActionResult> ApprovePayment(int id)
        {
            var payment = _context.Payments.FirstOrDefault(p => p.Id == id);

            if (payment == null)
                return RedirectToAction("Payments");

            payment.IsApproved = true;
            payment.IsRejected = false;

            var user = _context.Users.FirstOrDefault(u => u.Id == payment.UserId);

            if (user != null)
            {
                user.IsPremium = true;

                await _emailService.SendEmailAsync(
    user.Email,
    "🎉 Pago aprobado - HabitTracker",
    $@"
    <div style='font-family:sans-serif;padding:20px'>
        <h2 style='color:#16a34a'>✅ Pago aprobado</h2>
        <p>Hola <b>{user.Username}</b>,</p>
        <p>Tu pago ha sido aprobado correctamente.</p>
        <p>Ahora eres <b>usuario PREMIUM</b> 🚀🔥</p>
    </div>
    "
);



                // 🔔 NOTIFICACIÓN
                _context.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Message = "🎉 Tu pago fue aprobado. Ya eres PREMIUM 😈💸",
                    CreatedAt = DateTime.Now,
                    IsRead = false,
                    FromUserImage = user.ProfileImage ?? "",
                    FromUsername = "Sistema",
                    Link = "/User/Profile"
                });
            }

            _context.SaveChanges();

            TempData["Success"] = "Pago aprobado y usuario premium 😈💸";

            return RedirectToAction("Payments");
        }

        public IActionResult Payments()
        {
            var payments = _context.Payments
                .Include(p => p.User)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return View(payments);
        }

        public async Task<IActionResult> RejectPayment(int id)
        {
            var payment = _context.Payments.FirstOrDefault(p => p.Id == id);

            // 🔒 validar payment primero
            if (payment == null)
                return RedirectToAction("Payments");

            var user = _context.Users.FirstOrDefault(u => u.Id == payment.UserId);

            // 🔒 validar user
            if (user == null)
                return RedirectToAction("Payments");

            payment.IsRejected = true;
            payment.IsApproved = false;

            // 📧 EMAIL
            await _emailService.SendEmailAsync(
                user.Email,
                "❌ Pago rechazado - HabitTracker",
                $@"
        <div style='font-family:sans-serif;padding:20px'>
            <h2 style='color:#dc2626'>Pago rechazado</h2>
            <p>Hola <b>{user.Username}</b>,</p>
            <p>Tu comprobante fue rechazado.</p>
            <p>Intenta nuevamente con un comprobante válido.</p>
        </div>
        "
            );

            // 🔔 NOTIFICACIÓN
            _context.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Message = "❌ Tu pago fue rechazado. Verifica el comprobante.",
                CreatedAt = DateTime.Now,
                IsRead = false,
                FromUserImage = user.ProfileImage ?? "",
                FromUsername = "Sistema"
            });

            _context.SaveChanges();

            TempData["Success"] = "Pago rechazado ❌";

            return RedirectToAction("Payments");
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
                AdminProfileImage = currentUser.ProfileImage,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Hacer administrador",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            // 🔔 avisar al usuario que su rol cambió
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ForceLogout", "Ahora eres Admin bro. Inicia sesión nuevamente.");

            return RedirectToAction("Users");
        }
        public IActionResult Reports()
        {
            var reports = _context.PostReports
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(reports);
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
                AdminProfileImage = currentUser.ProfileImage,
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
                AdminId = myId,
                AdminName = User.Identity.Name,
                AdminProfileImage = User.FindFirst("ProfileImage")?.Value,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Quitar administrador",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            _context.SaveChanges();

            // 🔔 avisar al usuario que perdió admin
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ForceLogout", "Ya no eres Admin bro. Inicia sesión nuevamente.");

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

            ViewBag.Users = _context.Users.ToList();

            return View(logs);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleConnectionBlock()
        {
            var block = _context.ConnectionBlocks.FirstOrDefault();

            if (block == null)
            {
                block = new ConnectionBlock
                {
                    IsBlocked = true,
                    Message = "Esta conexión fue bloqueada por el propietario",
                    UpdatedAt = DateTime.Now
                };

                _context.ConnectionBlocks.Add(block);
            }
            else
            {
                block.IsBlocked = !block.IsBlocked;
                block.UpdatedAt = DateTime.Now;
            }

            _context.SaveChanges();

            // 🚨 EXPULSAR A TODOS
            if (block.IsBlocked)
            {
                await _hubContext.Clients.All.SendAsync(
                    "ConnectionBlocked",
                    "⚠ Esta conexión fue bloqueada por el propietario"
                );
            }

            return RedirectToAction("Index");
        }

        public IActionResult Security()
        {
            var activities = _context.SuspiciousActivities
                .OrderByDescending(a => a.CreatedAt)
                .ToList();

            return View(activities);
        }

        [HttpPost]
        public async Task<IActionResult> BlockIP(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return RedirectToAction("Users");

            var exists = _context.BlockedIPs.FirstOrDefault(x => x.IpAddress == ip);

            if (exists == null)
            {
                var blocked = new BlockedIP
                {
                    IpAddress = ip,
                    CreatedAt = DateTime.Now
                };

                _context.BlockedIPs.Add(blocked);
                _context.SaveChanges();

                // 🔎 buscar usuarios con esa IP
                var users = _context.Users
                    .Where(u => u.LastIp == ip)
                    .ToList();

                // 🚨 aviso en tiempo real
                foreach (var user in users)
                {
                    await _hubContext.Clients.User(user.Id.ToString())
                        .SendAsync("IPBlockedNow", "Tu dirección IP ha sido bloqueada");
                }
            }

            return RedirectToAction("Users");
        }

        [HttpPost]
        public IActionResult UnblockIP(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return RedirectToAction("Users");

            var blocked = _context.BlockedIPs.FirstOrDefault(x => x.IpAddress == ip);

            if (blocked != null)
            {
                _context.BlockedIPs.Remove(blocked);
                _context.SaveChanges();
            }

            return RedirectToAction("Users");
        }
        private async Task<string> GetCountryFromIP(string ip)
        {
            try
            {
                if (string.IsNullOrEmpty(ip) || ip == "127.0.0.1" || ip == "::1")
                    return "Local";

                using var client = new HttpClient();

                var response = await client.GetStringAsync($"https://ipwho.is/{ip}");

                var json = System.Text.Json.JsonDocument.Parse(response);

                if (json.RootElement.GetProperty("success").GetBoolean())
                {
                    var country = json.RootElement.GetProperty("country").GetString();
                    var isp = json.RootElement.GetProperty("connection")
                                              .GetProperty("isp")
                                              .GetString();

                    return $"{country} - {isp}";
                }

                return "Desconocido";
            }
            catch
            {
                return "Desconocido";
            }


        }

        private async Task<(string country, string city, string isp)> GetIPInfo(string ip)
        {
            try
            {
                using var client = new HttpClient();

                var json = await client.GetStringAsync($"http://ip-api.com/json/{ip}");

                dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                string country = data.country ?? "Desconocido";
                string city = data.city ?? "Desconocido";
                string isp = data.isp ?? "Desconocido";

                return (country, city, isp);
            }
            catch
            {
                return ("Desconocido", "Desconocido", "Desconocido");
            }
        }

       
        public async Task<IActionResult> DeleteUserPermanently(int id)
        {
            

            var user = await _context.Users.FindAsync(id);

            if (user == null)
                return RedirectToAction("Users");

            // 🔎 obtener admin actual
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == myId);

            // ❌ SOLO SUPERADMIN puede eliminar
            if (currentUser.Role != "SuperAdmin")
            {
                TempData["Error"] = "Solo el SuperAdmin puede eliminar usuarios.";
                return RedirectToAction("Users");
            }

            // ❌ no se puede eliminar a sí mismo
            if (user.Id == myId)
            {
                TempData["Error"] = "No puedes eliminar tu propia cuenta.";
                return RedirectToAction("Users");
            }

            // ❌ no se puede eliminar a otro admin
            // ❌ no se puede eliminar al SuperAdmin (pero sí Admin normales)
            if (user.Role == "SuperAdmin")
            {
                TempData["Error"] = "No puedes eliminar al SuperAdmin.";
                return RedirectToAction("Users");
            }

            // 📝 registrar acción en historial admin
            var log = new AdminLog
            {
                AdminId = currentUser.Id,
                AdminName = currentUser.Username,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Eliminar usuario permanentemente",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            // 🔥 eliminar hábitos del usuario
            var habits = _context.Habits.Where(h => h.UserId == user.Id);
            _context.Habits.RemoveRange(habits);

            // 🔥 eliminar comentarios (si existen)
            var comments = _context.Set<HabitComment>()
                .Where(c => c.UserId == user.Id);
            _context.RemoveRange(comments);

            // 🔥 manejar mensajes (evitar error FK)
            var messages = _context.Messages
                .Where(m => m.SenderId == id || m.ReceiverId == id)
                .ToList();

            foreach (var msg in messages)
            {
                if (msg.SenderId == id) msg.SenderId = null;
                if (msg.ReceiverId == id) msg.ReceiverId = null;
            }

            // 🚨 avisar al usuario en tiempo real
            await _hubContext.Clients.User(id.ToString())
                .SendAsync("ForceLogout", "Tu cuenta fue eliminada por el SuperAdmin");

            // 🔥 eliminar usuario
            _context.Users.Remove(user);

            await _context.SaveChangesAsync();

            TempData["Success"] = "Usuario eliminado correctamente";

            return RedirectToAction("Users");
        }
    }
}