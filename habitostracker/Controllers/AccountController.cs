using BCrypt.Net;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using HabitTrackerApp.Data;
using HabitTrackerApp.Hubs;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;


namespace HabitTrackerApp.Controllers
{
    public class AccountController : Controller
    {

       
        private readonly HabitDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly EmailService _emailService;
        private readonly IWebHostEnvironment _environment;

        private readonly CloudinaryService _cloudinaryService;

        public AccountController(
           HabitDbContext context,
           EmailService emailService,
           IWebHostEnvironment environment,
           IHubContext<ChatHub> hubContext,
           CloudinaryService cloudinaryService)
        {
            _context = context;
            _emailService = emailService;
            _environment = environment;
            _hubContext = hubContext;
            _cloudinaryService = cloudinaryService;
        }



        // =====================================================
        // 🔐 LOGIN
        // =====================================================
        [HttpGet]
        public IActionResult Login()
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            var blockedIp = _context.BlockedIPs.FirstOrDefault(x => x.IpAddress == ip);

            // 🚫 Si la IP está bloqueada, NO redirigir aunque esté autenticado
            if (blockedIp == null && User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Habit");
            }

            return View();
        }

        // ===== SEGURIDAD =====
        [HttpGet]
        public async Task<IActionResult> Security()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");

            var userId = int.Parse(userIdClaim.Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login");

            bool hasQuestions = !string.IsNullOrEmpty(user.SecurityQuestion1) &&
                                !string.IsNullOrEmpty(user.SecurityQuestion2) &&
                                !string.IsNullOrEmpty(user.SecurityQuestion3);

            ViewBag.HasSecurityQuestions = hasQuestions;

            // 🔥 sesiones activas
            var todasSesiones = _context.UserSessions
       .Where(s => s.UserId == userId)
       .OrderByDescending(s => s.IsActive)
       .ThenByDescending(s => s.CreatedAt)
       .ToList();

            // 🔥 agrupar por device+browser, tomar la más reciente de cada combo
            var activeSessions = todasSesiones
                .GroupBy(s => $"{s.Device ?? "?"}-{s.Browser ?? "?"}")
                .Select(g => g.First())
                .OrderByDescending(s => s.IsActive)
                .ThenByDescending(s => s.CreatedAt)
                .ToList();

            // 🔥 si la sesión actual no está en el resultado, agregarla
            var currentToken = User.FindFirst("SessionToken")?.Value;
            if (!string.IsNullOrEmpty(currentToken))
            {
                bool currentIncluida = activeSessions.Any(s => s.SessionToken == currentToken);
                if (!currentIncluida)
                {
                    var currentSession = todasSesiones.FirstOrDefault(s => s.SessionToken == currentToken);
                    if (currentSession != null)
                        activeSessions.Insert(0, currentSession);
                    else
                    {
                        // crear sesión actual si no existe
                        var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
                        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                                 ?? HttpContext.Connection.RemoteIpAddress?.ToString();
                        var newSession = new UserSession
                        {
                            UserId = userId,
                            SessionToken = currentToken,
                            Device = user.Device,
                            Browser = user.Browser,
                            IpAddress = ip ?? "",
                            CreatedAt = DateTime.UtcNow,
                            IsActive = true
                        };
                        _context.UserSessions.Add(newSession);
                        await _context.SaveChangesAsync();
                        activeSessions.Insert(0, newSession);
                    }
                }
            }

            ViewBag.ActiveSessions = activeSessions;

            if (hasQuestions)
            {
                var rng = new Random();
                int qNum = rng.Next(1, 4);
                HttpContext.Session.SetInt32("SecurityQuestionNum", qNum);
                ViewBag.SecurityQuestionNum = qNum;
            }

            return View(user);
        }

        // 🔥 GUARDAR PREGUNTAS DE SEGURIDAD
        [HttpPost]
        public async Task<IActionResult> SaveSecurityQuestions(
       string q1, string a1,
       string q2, string a2,
       string q3, string a3,
       string verifyPassword)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");

            var userId = int.Parse(userIdClaim.Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(q1) || string.IsNullOrWhiteSpace(a1) ||
                string.IsNullOrWhiteSpace(q2) || string.IsNullOrWhiteSpace(a2) ||
                string.IsNullOrWhiteSpace(q3) || string.IsNullOrWhiteSpace(a3))
            {
                TempData["Error"] = "Debes completar todas las preguntas y respuestas.";
                return RedirectToAction("Security");
            }

            bool yaTienePreguntas = !string.IsNullOrEmpty(user.SecurityQuestion1);
            if (yaTienePreguntas)
            {
                if (!user.IsGoogleAccount)
                {
                    // 🔥 usuario normal → verificar contraseña
                    if (string.IsNullOrWhiteSpace(verifyPassword) ||
                        !BCrypt.Net.BCrypt.Verify(verifyPassword, user.PasswordHash))
                    {
                        TempData["Error"] = "Contraseña incorrecta. No se pudieron actualizar las preguntas.";
                        return RedirectToAction("Security");
                    }
                }
                else
                {
                    // 🔥 cuenta Google → verificar con una de sus preguntas actuales
                    var verifyAnswer = Request.Form["verifySecurityAnswer"].ToString();
                    var verifyQNum = int.TryParse(Request.Form["verifySecurityQNum"], out int vq) ? vq : 1;

                    string? storedHash = verifyQNum switch
                    {
                        1 => user.SecurityAnswer1,
                        2 => user.SecurityAnswer2,
                        3 => user.SecurityAnswer3,
                        _ => null
                    };

                    if (string.IsNullOrWhiteSpace(verifyAnswer) || string.IsNullOrEmpty(storedHash) ||
                        !BCrypt.Net.BCrypt.Verify(verifyAnswer.Trim().ToLower(), storedHash))
                    {
                        TempData["Error"] = "Respuesta de seguridad incorrecta. No se pudieron actualizar las preguntas.";
                        return RedirectToAction("Security");
                    }
                }
            }

            user.SecurityQuestion1 = q1.Trim();
            user.SecurityAnswer1 = BCrypt.Net.BCrypt.HashPassword(a1.Trim().ToLower());
            user.SecurityQuestion2 = q2.Trim();
            user.SecurityAnswer2 = BCrypt.Net.BCrypt.HashPassword(a2.Trim().ToLower());
            user.SecurityQuestion3 = q3.Trim();
            user.SecurityAnswer3 = BCrypt.Net.BCrypt.HashPassword(a3.Trim().ToLower());

            await _context.SaveChangesAsync();

            TempData["Success"] = "✅ Preguntas de seguridad guardadas correctamente.";
            return RedirectToAction("Security");
        }
        // 🔥 VERIFICAR RESPUESTA DE SEGURIDAD (para eliminar cuenta)
        [HttpPost]
        public IActionResult VerifySecurityAnswer([FromBody] SecurityAnswerDto dto)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return Json(new { valid = false });

            var userId = int.Parse(userIdClaim.Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return Json(new { valid = false });

            string? storedHash = dto.QuestionNumber switch
            {
                1 => user.SecurityAnswer1,
                2 => user.SecurityAnswer2,
                3 => user.SecurityAnswer3,
                _ => null
            };

            if (string.IsNullOrEmpty(storedHash))
                return Json(new { valid = false });

            var isValid = BCrypt.Net.BCrypt.Verify(dto.Answer.Trim().ToLower(), storedHash);
            return Json(new { valid = isValid });
        }


        [HttpPost]
        public async Task<IActionResult> UpdateCover(IFormFile coverPhoto)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return Json(new { success = false });

            if (!user.IsPremium)
                return Json(new { success = false, error = "Solo usuarios Premium pueden cambiar la foto de portada." });

            if (coverPhoto == null || coverPhoto.Length == 0)
                return Json(new { success = false });

            var coverUrl = await _cloudinaryService.UploadImageAsync(coverPhoto, "covers");
            user.CoverImage = coverUrl;
            await _context.SaveChangesAsync();

            return Json(new { success = true, coverUrl = user.CoverImage });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAccount(string password, string confirmText, string securityAnswer)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");

            var userId = int.Parse(userIdClaim.Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return NotFound();

            // 🔥 verificar pregunta de seguridad desde sesión
            bool hasQuestions = !string.IsNullOrEmpty(user.SecurityAnswer1) &&
                                !string.IsNullOrEmpty(user.SecurityAnswer2) &&
                                !string.IsNullOrEmpty(user.SecurityAnswer3);

            if (hasQuestions)
            {
                var securityQuestionNumber = HttpContext.Session.GetInt32("SecurityQuestionNum") ?? 1;

                string? storedHash = securityQuestionNumber switch
                {
                    1 => user.SecurityAnswer1,
                    2 => user.SecurityAnswer2,
                    3 => user.SecurityAnswer3,
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(securityAnswer) || string.IsNullOrEmpty(storedHash) ||
                    !BCrypt.Net.BCrypt.Verify(securityAnswer.Trim().ToLower(), storedHash))
                {
                    TempData["Error"] = "Respuesta de seguridad incorrecta.";
                    return RedirectToAction("Security");
                }
            }

            if (user.IsGoogleAccount)
            {
                if (string.IsNullOrWhiteSpace(confirmText) || confirmText.Trim().ToUpper() != "ELIMINAR")
                {
                    TempData["Error"] = "Debes escribir ELIMINAR para confirmar.";
                    return RedirectToAction("Security");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    TempData["Error"] = "Por favor ingresa tu contraseña para confirmar.";
                    return RedirectToAction("Security");
                }
                if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                {
                    TempData["Error"] = "Contraseña incorrecta. Intenta de nuevo.";
                    return RedirectToAction("Security");
                }
            }

            var habits = _context.Habits.Where(h => h.UserId == userId);
            _context.Habits.RemoveRange(habits);

            var messages = _context.Messages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId).ToList();

            foreach (var msg in messages)
            {
                if (msg.SenderId == userId) msg.SenderId = null;
                if (msg.ReceiverId == userId) msg.ReceiverId = null;
            }

            var follows = _context.Follows
                .Where(f => f.FollowerId == userId || f.FollowingId == userId);
            _context.Follows.RemoveRange(follows);

            await SendGoodbyeEmail(user);

            // 🔥 eliminar reacciones de mensajes de grupo
            var groupReactions = _context.GroupMessageReactions.Where(r => r.UserId == userId);
            _context.GroupMessageReactions.RemoveRange(groupReactions);

            // 🔥 eliminar lecturas de mensajes de grupo
            var groupReads = _context.GroupMessageReads.Where(r => r.UserId == userId);
            _context.GroupMessageReads.RemoveRange(groupReads);

            // 🔥 eliminar membresías de grupos
            var groupMembers = _context.GroupMembers.Where(m => m.UserId == userId);
            _context.GroupMembers.RemoveRange(groupMembers);
            var groupMessages = _context.GroupMessages
    .Where(m => m.SenderId == userId)
    .ToList();
            foreach (var gm in groupMessages)
                gm.SenderId = null;
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        public class SecurityAnswerDto
        {
            public int QuestionNumber { get; set; }
            public string Answer { get; set; } = "";
        }
        // ===== ELIMINAR CUENTA =====
      

        [HttpPost]
        public IActionResult VerifyPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return Json(new { valid = false });

            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null) return Json(new { valid = false });

            var isValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            return Json(new { valid = isValid });
        }

        [HttpPost]
        public async Task<IActionResult> LogoutAll()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return Ok();

            var userId = int.Parse(userIdClaim.Value);

            // 🔥 marcar todas las sesiones como inactivas
            var sessions = _context.UserSessions
                .Where(s => s.UserId == userId && s.IsActive)
                .ToList();

            foreach (var s in sessions)
                s.IsActive = false;

            await _context.SaveChangesAsync();

            // 🔥 forzar logout en todos los dispositivos via SignalR
            await _hubContext.Clients.Group(userId.ToString())
                .SendAsync("ForceLogout", "Tu sesión fue cerrada desde otro dispositivo.");

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok();
        }

        public IActionResult AccessDenied()
        {
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult CheckIfUserExists()
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Json(false);

            var userId = int.Parse(userIdClaim.Value);

            var exists = _context.Users.Any(u => u.Id == userId);

            return Json(exists);
        }

        [HttpGet]
        public IActionResult CheckAccount()
        {
            // 🔥 detectar invitado
            var isGuest = User.FindFirst("IsGuest")?.Value == "true";

            if (isGuest)
            {
                return Json(new { deleted = false }); // 🚀 invitado NUNCA está eliminado
            }

            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return Json(new { deleted = true });

            var userId = int.Parse(userIdClaim.Value);

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return Json(new { deleted = true });

            return Json(new { deleted = false });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel login, double? Latitude, double? Longitude)
        {
            if (!ModelState.IsValid) return View(login);

            var user = _context.Users.FirstOrDefault(u => u.Username == login.Username);

            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrEmpty(ip))
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            var blockedIp = _context.BlockedIPs.FirstOrDefault(b => b.IpAddress == ip);
            if (blockedIp != null)
                return RedirectToAction("Login", new { ipblocked = true });

            if (user == null)
            {
                ModelState.AddModelError("", "El usuario no existe.");
                return View(login);
            }

            // 🔌 CONEXIÓN BLOQUEADA — actualiza IP para detectar en otros dispositivos
            if (user.IsIpBlocked)
            {
                user.LastIp = ip;
                _context.SaveChanges();
                return RedirectToAction("Login", new { blocked = true });
            }

            if (user.IsGoogleAccount)
            {
                ModelState.AddModelError("", "Esta cuenta fue creada con Google. Usa 'Continuar con Google' 🔴");
                return View(login);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError("", "Un administrador desactivó tu cuenta.");
                return View(login);
            }

            if (user.IsBanned)
            {
                ModelState.AddModelError("", "Tu cuenta ha sido suspendida por un administrador.");
                return View(login);
            }

            if (user.LockoutEnd != null && user.LockoutEnd > DateTime.Now)
            {
                var minutes = (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.Now).TotalMinutes);
                ModelState.AddModelError("", $"Cuenta bloqueada. Intenta en {minutes} minuto(s).");
                return View(login);
            }

            if (!BCrypt.Net.BCrypt.Verify(login.Password, user.PasswordHash))
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= 5)
                {
                    user.LockoutEnd = DateTime.Now.AddMinutes(10);
                    user.FailedLoginAttempts = 0;
                    _context.SaveChanges();
                    ModelState.AddModelError("", "Demasiados intentos. Cuenta bloqueada 10 minutos.");
                    return View(login);
                }
                _context.SaveChanges();
                ModelState.AddModelError("", $"Contraseña incorrecta. Intento {user.FailedLoginAttempts}/5");
                return View(login);
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            user.LastOnline = DateTime.Now;

            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            user.Device = GetDevice(userAgent);
            user.OperatingSystem = GetOS(userAgent);
            user.Browser = GetBrowser(userAgent);
            user.LastIp = ip;

            if (Latitude != null && Longitude != null)
            {
                user.Latitude = Latitude;
                user.Longitude = Longitude;
            }

            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                var geoJson = await httpClient.GetStringAsync($"https://ipwho.is/{ip}");
                var geoDoc = System.Text.Json.JsonDocument.Parse(geoJson);
                var geoRoot = geoDoc.RootElement;
                if (geoRoot.GetProperty("success").GetBoolean())
                {
                    user.Country = geoRoot.GetProperty("country").GetString();
                    user.City = geoRoot.GetProperty("city").GetString();
                    if (Latitude == null || Longitude == null)
                    {
                        user.Latitude = geoRoot.GetProperty("latitude").GetDouble();
                        user.Longitude = geoRoot.GetProperty("longitude").GetDouble();
                    }
                }
            }
            catch { }

            _context.SaveChanges();

            if (!user.EmailConfirmed)
            {
                TempData["UnconfirmedEmail"] = user.Email;
                ModelState.AddModelError("", "Debes confirmar tu correo antes de iniciar sesión.");
                return View(login);
            }

            await SignInUser(user);

            var admins = _context.Users.Where(u => u.Role == "SuperAdmin" || u.Role == "Admin").ToList();
            foreach (var admin in admins)
            {
                if (user.Role != "SuperAdmin")
                    await _hubContext.Clients.User(admin.Id.ToString())
                        .SendAsync("UserConnectedNotification", user.Username);
            }

            return RedirectToAction("Index", "Habit", null, "https");
        }
        // =====================================================
        // 🔁 REENVIAR CONFIRMACIÓN
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmation(string email)
        {
            var user = _context.Users.FirstOrDefault(u => u.Email == email);

            if (user == null)
                return RedirectToAction("Login");

            await SendConfirmationCode(user);

            _context.SaveChanges();

            TempData["ResetEmail"] = user.Email;

            return RedirectToAction("ConfirmEmail");
        }

       
        private async Task SendEmail(string toEmail, string subject, string htmlMessage)
        {
            var smtp = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential("noreplyhabittrackert@gmail.com", "iejtakfbikbxwzuk"),
                EnableSsl = true
            };

            var mail = new MailMessage
            {
                From = new MailAddress("noreplyhabittrackert@gmail.com", "HabitTracker"),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };

            mail.To.Add(toEmail);

            await smtp.SendMailAsync(mail);
        }


        [HttpGet]
        public IActionResult ResetPassword(string email)
        {
            var model = new ResetPasswordViewModel
            {
                Email = email
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = _context.Users.FirstOrDefault(u => u.Email == model.Email);

            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado";
                return RedirectToAction("Login");
            }

            // 🔐 guardar nueva contraseña encriptada
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

            // 🔎 obtener admin actual
            var currentUserId = int.Parse(User.FindFirst("UserId").Value);
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == currentUserId);

            // 📝 registrar acción en historial admin
            var log = new AdminLog
            {
                AdminId = currentUser.Id,
                AdminName = currentUser.Username,
                TargetUserId = user.Id,
                TargetUsername = user.Username,
                Action = "Restablecer contraseña",
                CreatedAt = DateTime.Now
            };

            _context.AdminLogs.Add(log);

            return Content("LOG AGREGADO");

            _context.SaveChanges();

            TempData["Success"] = "Contraseña actualizada correctamente 🔥";
            return RedirectToAction("Login");
        }





        // =====================================================
        // 📝 REGISTER
        // =====================================================
        [HttpGet]
        public IActionResult Register()
        {
           

            return View();
        }



       
        // =====================================================
        // 📧 CONFIRM EMAIL
        [HttpGet]
        public IActionResult ConfirmEmail()
        {
            var email = TempData.Peek("ResetEmail")?.ToString();

            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("Login");
            }

            var model = new ConfirmEmailViewModel
            {
                Email = email
            };

            return View(model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmEmail(ConfirmEmailViewModel model)
        {
            var email = TempData.Peek("ResetEmail")?.ToString();
            var fromRegister = TempData.Peek("FromRegister") as bool? ?? false;
            var fromReset = TempData.Peek("FromReset") as bool? ?? false;

            var user = _context.Users.FirstOrDefault(u => u.Email == email || u.PendingEmail == email);

            if (user == null)
            {
                ModelState.AddModelError("", "Usuario no encontrado.");
                return View(model);
            }

            if (user.ResetCode != model.Code || user.ResetCodeExpiry < DateTime.Now)
            {
                ModelState.AddModelError("", "Código inválido o expirado.");
                return View(model);
            }

            if (fromRegister)
            {
                user.EmailConfirmed = true;
                user.IsActive = true;
                user.ResetCode = null;
                _context.SaveChanges();
                _ = SendWelcomeEmail(user);
                // ✅ login automático y redirigir a CompleteProfile
                await RefreshUserSession(user);
                return RedirectToAction("CompleteProfile", "Account");
            }
            else if (fromReset)
            {
                TempData["VerifiedEmail"] = email;
                return RedirectToAction("NewPassword");
            }
            else
            {
                user.EmailConfirmed = true;
                user.Email = user.PendingEmail ?? user.Email;
                user.PendingEmail = null;
                user.ResetCode = null;
                _context.SaveChanges();
            }

            TempData["Success"] = "Cuenta confirmada correctamente.";
            return RedirectToAction("Login");
        }

        [HttpPost]
        public IActionResult SaveBio(string bio)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null) return NotFound();

            user.Bio = bio;
            _context.SaveChanges();

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model, IFormFile profilePhoto)
        {
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrEmpty(ip))
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            var blockedIp = _context.BlockedIPs.FirstOrDefault(b => b.IpAddress == ip);
            if (blockedIp != null)
                return RedirectToAction("Login", new { ipblocked = true });

            var blockedUser = _context.Users.FirstOrDefault(u => u.LastIp == ip && u.IsIpBlocked);
            if (blockedUser != null)
                return RedirectToAction("Login", new { blocked = true });

            ModelState.Remove("profilePhoto");
            if (!ModelState.IsValid) return View(model);

            if (_context.Users.Any(u => u.Username == model.Username))
            {
                ModelState.AddModelError("", "El usuario ya existe.");
                return View(model);
            }

            if (_context.Users.Any(u => u.Email == model.Email))
            {
                ModelState.AddModelError("", "Este correo ya está registrado.");
                return View(model);
            }

            string imagePath = null;
            if (profilePhoto != null && profilePhoto.Length > 0)
            {
                var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/profiles");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(profilePhoto.FileName);
                var filePath = Path.Combine(folder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                    await profilePhoto.CopyToAsync(stream);
                imagePath = "/images/profiles/" + fileName;
            }

            var newUser = new User
            {
                Username = model.Username,
                Email = model.Email,
                Gender = model.Gender ?? "",
                Bio = model.Bio ?? "",
                FullName = null,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                CreatedAt = DateTime.Now,
                Role = "User",
                EmailConfirmed = false,
                ProfileImage = imagePath,
                IsActive = false
            };

            _context.Users.Add(newUser);
            _context.SaveChanges();

            await SendConfirmationCode(newUser);
            _context.SaveChanges();

            TempData["RegisterData"] = System.Text.Json.JsonSerializer.Serialize(newUser);
            TempData["ResetEmail"] = newUser.Email;
            TempData["FromRegister"] = true;

            return RedirectToAction("ConfirmEmail");
        }
        [HttpGet]
        [Authorize]
        public IActionResult CheckBlockStatus()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return Forbid();
            var user = _context.Users.FirstOrDefault(u => u.Id == int.Parse(userIdClaim.Value));
            if (user == null || user.IsIpBlocked) return StatusCode(403);
            return Ok();
        }
        private async Task SendGoodbyeEmail(User user)
        {
            var subject = "💔 Tu cuenta ha sido eliminada - HabitTracker";

            var message = $@"
<div style='font-family:Arial,sans-serif;background:#0f172a;padding:40px 20px;margin:0;'>
    <div style='max-width:560px;margin:0 auto;'>

        <!-- HEADER -->
        <div style='text-align:center;margin-bottom:30px;'>
            <div style='background:linear-gradient(135deg,#6366f1,#2563eb);border-radius:16px;padding:14px 24px;display:inline-block;'>
                <span style='font-size:22px;font-weight:800;color:white;'>🚀 HabitTracker</span>
            </div>
        </div>

        <!-- CARD PRINCIPAL -->
        <div style='background:#1e293b;border-radius:24px;padding:36px;border:1px solid rgba(255,255,255,0.06);text-align:center;margin-bottom:16px;'>

            <!-- Mascota -->
            <img src='https://res.cloudinary.com/dzrjag7ia/image/upload/v1776560628/NE_lmertz.jpg'
                 style='width:100px;height:100px;border-radius:50%;object-fit:cover;border:3px solid #6366f1;box-shadow:0 0 0 6px rgba(99,102,241,0.15);margin-bottom:20px;display:block;margin-left:auto;margin-right:auto;' />

            <h2 style='color:white;font-size:22px;margin:0 0 6px;'>Hasta pronto, {user.Username} 💔</h2>
            <p style='color:rgba(255,255,255,0.4);font-size:13px;margin:0 0 24px;'>Tu cuenta ha sido eliminada correctamente</p>

            <div style='height:1px;background:rgba(255,255,255,0.06);margin:0 0 24px;'></div>

            <p style='color:rgba(255,255,255,0.7);font-size:14px;line-height:1.8;margin:0 0 20px;text-align:left;'>
                Hola <strong style='color:white;'>{user.Username}</strong>, queremos agradecerte por el tiempo que dedicaste a 
                construir hábitos, mejorar tu disciplina y apostar por tu crecimiento personal.
            </p>

            <!-- Quote -->
            <div style='background:rgba(99,102,241,0.08);border-left:3px solid #6366f1;border-radius:0 12px 12px 0;padding:14px 18px;margin-bottom:24px;text-align:left;'>
                <p style='color:#818cf8;font-size:13px;font-style:italic;margin:0;line-height:1.7;'>
                    &ldquo;Cada pequeño hábito que construiste fue una victoria. Los cambios reales toman tiempo, y tú diste el primer paso. Eso nunca desaparece.&rdquo; 🔥
                </p>
            </div>

            <!-- Botón volver -->
            <a href='https://habitostracker-production-4cf5.up.railway.app/Account/Register'
               style='display:inline-block;background:linear-gradient(135deg,#6366f1,#2563eb);color:white;text-decoration:none;padding:13px 28px;border-radius:12px;font-weight:700;font-size:14px;box-shadow:0 6px 20px rgba(99,102,241,0.35);'>
                🚀 Volver a HabitTracker
            </a>

            <p style='color:rgba(255,255,255,0.25);font-size:12px;margin:20px 0 0;'>
                Si decides volver, aquí estaremos para ayudarte a seguir creciendo.
            </p>
        </div>

        <!-- FOOTER -->
        <div style='text-align:center;padding:16px;'>
            <p style='color:rgba(255,255,255,0.2);font-size:11px;margin:0;line-height:1.7;'>
                Este correo fue enviado porque eliminaste tu cuenta en HabitTracker.<br/>
                <strong style='color:rgba(255,255,255,0.3);'>— Equipo HabitTracker 🚀</strong>
            </p>
        </div>

    </div>
</div>";

            await _emailService.SendEmailAsync(user.Email, subject, message);
        }

        private async Task SendWelcomeEmail(User user)
{
    var subject = "🎉 Bienvenido a HabitTracker";
    var saludo = user.Gender?.ToLower() == "femenino" ? "Bienvenida" : "Bienvenido";

    var message = $@"<!DOCTYPE html>
<html>
<body style='margin:0;padding:0;background-color:#0f172a;font-family:""Segoe UI"",Arial,sans-serif;'>
  <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#0f172a;padding:40px 0;'>
    <tr>
      <td align='center'>
        <table width='600' cellpadding='0' cellspacing='0' style='background-color:#ffffff;border-radius:24px;overflow:hidden;box-shadow:0 20px 60px rgba(0,0,0,0.4);'>

          <!-- HEADER -->
          <tr>
            <td style='background:linear-gradient(135deg,#1e293b 0%,#1e1b4b 50%,#0f172a 100%);padding:0;text-align:center;'>
              <div style='padding:40px 35px 28px;'>
                <div style='display:inline-block;background:linear-gradient(135deg,#6366f1,#2563eb);border-radius:20px;width:64px;height:64px;line-height:64px;text-align:center;font-size:32px;box-shadow:0 8px 24px rgba(99,102,241,0.5);margin-bottom:16px;'>🎯</div>
                <h1 style='color:white;margin:0 0 8px;font-size:30px;font-weight:800;letter-spacing:-0.5px;'>HabitTracker</h1>
                <p style='color:rgba(255,255,255,0.5);margin:0;font-size:13px;font-weight:500;letter-spacing:1.5px;text-transform:uppercase;'>Construye hábitos · Transforma tu vida</p>
              </div>
              <img src='https://res.cloudinary.com/dzrjag7ia/image/upload/v1776560628/NE_lmertz.jpg'
                   width='600'
                   style='width:100%;max-width:600px;display:block;height:200px;object-fit:cover;opacity:0.45;'/>
            </td>
          </tr>

          <!-- DIVIDER -->
          <tr><td style='height:4px;background:linear-gradient(90deg,#6366f1,#2563eb,#22c55e);'></td></tr>

          <!-- BODY -->
          <tr>
            <td style='padding:40px 36px 32px;background:#ffffff;'>

              <h2 style='color:#111827;margin:0 0 10px;font-size:22px;font-weight:800;'>¡{saludo}, {user.Username}! 🎉</h2>
              <p style='color:#6b7280;font-size:15px;line-height:1.7;margin:0 0 28px;'>Tu cuenta ha sido creada exitosamente. Bienvenido a la comunidad de personas que están transformando su vida un hábito a la vez.</p>

              <!-- STATS CARDS -->
              <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:28px;'>
                <tr>
                  <td width='33%' style='padding:0 5px 0 0;'>
                    <div style='background:#f0fdf4;border-radius:16px;padding:18px 10px;text-align:center;border:1px solid #dcfce7;'>
                      <div style='font-size:26px;margin-bottom:6px;'>🔥</div>
                      <div style='font-size:11px;font-weight:700;color:#16a34a;text-transform:uppercase;letter-spacing:0.5px;'>Rachas</div>
                      <div style='font-size:10px;color:#4ade80;margin-top:2px;'>Mantén el ritmo</div>
                    </div>
                  </td>
                  <td width='33%' style='padding:0 2px;'>
                    <div style='background:#eff6ff;border-radius:16px;padding:18px 10px;text-align:center;border:1px solid #dbeafe;'>
                      <div style='font-size:26px;margin-bottom:6px;'>📈</div>
                      <div style='font-size:11px;font-weight:700;color:#2563eb;text-transform:uppercase;letter-spacing:0.5px;'>Progreso</div>
                      <div style='font-size:10px;color:#60a5fa;margin-top:2px;'>Mejora cada día</div>
                    </div>
                  </td>
                  <td width='33%' style='padding:0 0 0 5px;'>
                    <div style='background:#fdf4ff;border-radius:16px;padding:18px 10px;text-align:center;border:1px solid #f3e8ff;'>
                      <div style='font-size:26px;margin-bottom:6px;'>🤝</div>
                      <div style='font-size:11px;font-weight:700;color:#7c3aed;text-transform:uppercase;letter-spacing:0.5px;'>Comunidad</div>
                      <div style='font-size:10px;color:#a78bfa;margin-top:2px;'>Conecta y crece</div>
                    </div>
                  </td>
                </tr>
              </table>

              <!-- QUOTE -->
              <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:28px;'>
                <tr>
                  <td style='background:linear-gradient(135deg,#1e293b,#1e1b4b);border-radius:16px;padding:22px 24px;text-align:center;'>
                    <p style='color:rgba(255,255,255,0.45);font-size:11px;margin:0 0 8px;text-transform:uppercase;letter-spacing:1px;font-weight:600;'>💡 Recuerda siempre</p>
                    <p style='color:white;font-size:18px;font-weight:800;margin:0;line-height:1.4;'>&quot;Pequeños hábitos,<br/>grandes resultados.&quot;</p>
                  </td>
                </tr>
              </table>

              <!-- PASOS -->
              <p style='color:#111827;font-size:13px;font-weight:700;margin:0 0 14px;text-transform:uppercase;letter-spacing:0.5px;'>🚀 Empieza ahora</p>

              <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:10px;'>
                <tr>
                  <td style='background:#f8fafc;border-radius:12px;padding:14px 16px;border:1px solid #e5e7eb;'>
                    <table width='100%' cellpadding='0' cellspacing='0'><tr>
                      <td width='36' style='vertical-align:middle;'>
                        <div style='width:32px;height:32px;background:linear-gradient(135deg,#6366f1,#4f46e5);border-radius:10px;text-align:center;line-height:32px;font-size:15px;'>1️⃣</div>
                      </td>
                      <td style='padding-left:12px;vertical-align:middle;'>
                        <span style='color:#111827;font-size:14px;font-weight:600;'>Crea tu primer hábito</span>
                        <p style='color:#9ca3af;font-size:12px;margin:2px 0 0;'>Elige algo pequeño y alcanzable</p>
                      </td>
                    </tr></table>
                  </td>
                </tr>
              </table>

              <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:10px;'>
                <tr>
                  <td style='background:#f8fafc;border-radius:12px;padding:14px 16px;border:1px solid #e5e7eb;'>
                    <table width='100%' cellpadding='0' cellspacing='0'><tr>
                      <td width='36' style='vertical-align:middle;'>
                        <div style='width:32px;height:32px;background:linear-gradient(135deg,#22c55e,#16a34a);border-radius:10px;text-align:center;line-height:32px;font-size:15px;'>2️⃣</div>
                      </td>
                      <td style='padding-left:12px;vertical-align:middle;'>
                        <span style='color:#111827;font-size:14px;font-weight:600;'>Complétalo cada día</span>
                        <p style='color:#9ca3af;font-size:12px;margin:2px 0 0;'>La constancia es la clave del éxito</p>
                      </td>
                    </tr></table>
                  </td>
                </tr>
              </table>

              <table width='100%' cellpadding='0' cellspacing='0' style='margin-bottom:0;'>
                <tr>
                  <td style='background:#f8fafc;border-radius:12px;padding:14px 16px;border:1px solid #e5e7eb;'>
                    <table width='100%' cellpadding='0' cellspacing='0'><tr>
                      <td width='36' style='vertical-align:middle;'>
                        <div style='width:32px;height:32px;background:linear-gradient(135deg,#f59e0b,#d97706);border-radius:10px;text-align:center;line-height:32px;font-size:15px;'>3️⃣</div>
                      </td>
                      <td style='padding-left:12px;vertical-align:middle;'>
                        <span style='color:#111827;font-size:14px;font-weight:600;'>Comparte tu progreso</span>
                        <p style='color:#9ca3af;font-size:12px;margin:2px 0 0;'>Motiva a otros en la comunidad</p>
                      </td>
                    </tr></table>
                  </td>
                </tr>
              </table>

            </td>
          </tr>

          <!-- FOOTER -->
          <tr>
            <td style='background:#f8fafc;padding:24px 36px;border-top:1px solid #e5e7eb;text-align:center;'>
              <p style='color:#6b7280;font-size:13px;margin:0 0 4px;'>
                Estamos en desarrollo 🚧 — aún nos falta mucho por mejorar
              </p>
              <p style='color:#9ca3af;font-size:12px;margin:0;'>
                Con 💙 del <strong style='color:#6366f1;'>Equipo HabitTracker</strong>
              </p>
              <div style='margin-top:14px;'>
                <span style='display:inline-block;width:8px;height:8px;border-radius:50%;background:#6366f1;margin:0 3px;'></span>
                <span style='display:inline-block;width:8px;height:8px;border-radius:50%;background:#2563eb;margin:0 3px;'></span>
                <span style='display:inline-block;width:8px;height:8px;border-radius:50%;background:#22c55e;margin:0 3px;'></span>
              </div>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>";

    await _emailService.SendEmailAsync(user.Email, subject, message);
}

     

        // =====================================================
        // ✏️ EDITAR CORREO
        // =====================================================
        [HttpGet]
        public IActionResult EditEmail(string email)
        {
            var model = new EditEmailViewModel
            {


                Email = email
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEmail(EditEmailViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // obtenemos el correo actual guardado cuando entró a confirmar
            var emailActual = TempData.Peek("ResetEmail")?.ToString();

            var user = _context.Users
                .FirstOrDefault(u => u.Email == emailActual || u.PendingEmail == emailActual);

            if (user == null)
            {
                ModelState.AddModelError("", "Usuario no encontrado.");
                return View(model);
            }

            // guardamos el nuevo correo
            user.PendingEmail = model.Email;

            // enviamos nuevo código
            await SendConfirmationCode(user);

            _context.SaveChanges();

            // guardamos el nuevo correo para la pantalla de confirmación
            TempData["ResetEmail"] = model.Email;

            return RedirectToAction("ConfirmEmail");
        }



        // =====================================================
        // 👤 PROFILE
        // =====================================================
        [HttpGet]
        public IActionResult Profile(int? id)
        {
            if (!User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            var myId = int.Parse(User.FindFirst("UserId").Value);

            // 🔥 SI ES MI PERFIL
            if (id == null || id == myId)
            {
                var me = _context.Users.FirstOrDefault(u => u.Id == myId);
                if (me != null && me.PendingEmail != null)
                {
                    me.PendingEmail = null;
                    _context.SaveChanges();
                }



                // 🔥 AGREGA ESTO (SEGUIDORES / SIGUIENDO)
                ViewBag.Followers = _context.Follows.Count(f => f.FollowingId == myId);
                ViewBag.Following = _context.Follows.Count(f => f.FollowerId == myId);

                return View("~/Views/Account/Profile.cshtml", me); // 🟢 editable
            }

            // 🔥 SI ES OTRO USUARIO
            var user = _context.Users.FirstOrDefault(u => u.Id == id);
            if (user == null) return NotFound();

            // 🔥 seguidores
            ViewBag.FollowersCount = _context.Follows.Count(f => f.FollowingId == user.Id);

            // 🔥 siguiendo
            ViewBag.FollowingCount = _context.Follows.Count(f => f.FollowerId == user.Id);

            return View("~/Views/User/Profile.cshtml", user); // 🔵 SOLO VISUAL
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(User updatedUser, IFormFile profilePhoto, string croppedImage)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return RedirectToAction("Login");

            if (_context.Users.Any(u => u.Email == updatedUser.Email && u.Id != user.Id))
            {
                ModelState.AddModelError("", "Ese correo ya está en uso.");
                return View("~/Views/Account/Profile.cshtml", user);
            }

            user.FullName = updatedUser.FullName;
            user.Bio = updatedUser.Bio;

            if (croppedImage == "REMOVE")
            {
                user.ProfileImage = null;
            }
            else if (!string.IsNullOrEmpty(croppedImage))
            {
                var base64Data = croppedImage.Contains(',') ? croppedImage.Split(',')[1] : croppedImage;
                byte[] imageBytes = Convert.FromBase64String(base64Data);

                using var ms = new MemoryStream(imageBytes);
                var formFile = new FormFile(ms, 0, imageBytes.Length, "croppedImage", "profile.png")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "image/png"
                };

                var imageUrl = await _cloudinaryService.UploadImageAsync(formFile, "habitostracker/profiles");
                user.ProfileImage = imageUrl;
            }
            else if (profilePhoto != null && profilePhoto.Length > 0)
            {
                var imageUrl = await _cloudinaryService.UploadImageAsync(profilePhoto, "habitostracker/profiles");
                user.ProfileImage = imageUrl;
            }

            _context.SaveChanges();
            await SignInUser(user);

            TempData["Success"] = "Perfil actualizado correctamente.";
            return RedirectToAction("Profile", "Account");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var userId = int.Parse(User.FindFirst("UserId").Value);

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return RedirectToAction("Login");

            if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash))
            {
                ModelState.AddModelError("", "La contraseña actual es incorrecta.");
                return View(model);
            }

            if (BCrypt.Net.BCrypt.Verify(model.NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError("", "La nueva contraseña no puede ser igual a la actual.");
                return View(model);
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);

            _context.SaveChanges();

            TempData["Success"] = "Contraseña actualizada correctamente.";

            return RedirectToAction("Profile");
        }

        // =====================================================
        // 🔓 LOGOUT
        // =====================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> LogoutGet()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Login");
        }




        // =====================================================
        // 🔑 MÉTODO LOGIN
        // =====================================================
        private async Task SignInUser(User user)
        {
            // 🔥 generar token único para esta sesión
            var sessionToken = Guid.NewGuid().ToString();

            var claims = new List<Claim>
    {
        new Claim("UserId", user.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username ?? "Usuario"),
        new Claim(ClaimTypes.Role, user.Role ?? "User"),
        new Claim("ProfileImage", user.ProfileImage ?? user.ProfilePicture ?? ""),
        new Claim("SessionToken", sessionToken)
    };

            var claimsIdentity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            // 🔥 registrar sesión en BD
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                     ?? HttpContext.Connection.RemoteIpAddress?.ToString();

            try
            {
                _context.UserSessions.Add(new UserSession
                {
                    UserId = user.Id,
                    SessionToken = sessionToken,
                    Device = GetDevice(userAgent),
                    Browser = GetBrowser(userAgent),
                    IpAddress = ip ?? "",
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await _context.SaveChangesAsync();
                Console.WriteLine($"✅ Sesión guardada para userId={user.Id} token={sessionToken}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error guardando sesión: {ex.Message}");
            }
        }
        public async Task RefreshUserSession(User user)
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim("UserId", user.Id.ToString()),
        new Claim(ClaimTypes.Role, user.Role ?? "User"),
       new Claim("ProfileImage", user.ProfileImage ?? user.ProfilePicture ?? "")
    };

            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));
        }

        //ENDPOINT/REFRESH ROLE

        [HttpPost]
        public async Task<IActionResult> RefreshRole()
        {
            var claim = User.FindFirst("UserId");

            if (claim == null)
                return Unauthorized();

            var userId = int.Parse(claim.Value);

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return Unauthorized();

            await RefreshUserSession(user);

            return Ok();
        }

        // =====================================================
        // 🔧 MÉTODOS PRIVADOS
        // =====================================================
        private async Task SendConfirmationCode(User user)
        {
            var random = new Random();
            string code = random.Next(100000, 999999).ToString();

            user.ResetCode = code;
            user.ResetCodeExpiry = DateTime.Now.AddMinutes(15);

            var html = $@"
    <div style='font-family:Segoe UI,Arial,sans-serif;background:#f9fafb;padding:30px'>
        
        <div style='max-width:500px;margin:auto;background:white;padding:30px;border-radius:12px;
                    box-shadow:0 5px 20px rgba(0,0,0,0.08)'>

            <h2 style='margin-bottom:10px;color:#111827'>
                🔐 Confirmación de cuenta
            </h2>

            <p style='color:#6b7280;font-size:14px'>
                Hola <b>{user.Username}</b>, usa este código para confirmar tu cuenta:
            </p>

            <div style='margin:25px 0;text-align:center'>
                <span style='font-size:30px;font-weight:bold;
                             letter-spacing:6px;
                             background:#111827;
                             color:white;
                             padding:12px 24px;
                             border-radius:10px;
                             display:inline-block'>
                    {code}
                </span>
            </div>

            <p style='font-size:13px;color:#9ca3af'>
                Este código expira en 15 minutos. No lo compartas con nadie.
            </p>

            <hr style='margin:25px 0;border:none;border-top:1px solid #eee'>

            <p style='font-size:12px;color:#9ca3af;text-align:center'>
                HabitTracker Pro 🚀
            </p>

        </div>
    </div>";

            await _emailService.SendEmailAsync(
                user.PendingEmail ?? user.Email,
                "🔐 Confirma tu cuenta - HabitTracker",
                html
            );
        }

        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
    ?? HttpContext.Connection.RemoteIpAddress?.ToString();
            var blockedUser = _context.Users.FirstOrDefault(u => u.LastIp == ip && u.IsIpBlocked);
            if (blockedUser != null)
                return RedirectToAction("Login", new { blocked = true });
            if (!ModelState.IsValid)
                return View(model);

            var user = _context.Users.FirstOrDefault(u => u.Email == model.Email);

            if (user == null)
            {
                ModelState.AddModelError("", "No existe una cuenta con ese correo.");
                return View(model);
            }

            await SendResetCode(user);

            TempData["ResetEmail"] = user.Email;

            TempData["FromReset"] = true;

            return RedirectToAction("ConfirmEmail");
        }

        private async Task SendResetCode(User user)
        {
            var random = new Random();
            string code = random.Next(100000, 999999).ToString();

            user.ResetCode = code;
            user.ResetCodeExpiry = DateTime.Now.AddMinutes(10);

            _context.SaveChanges();

            var html = $@"
    <div style='font-family:Segoe UI,Arial,sans-serif;background:#f9fafb;padding:30px'>
        
        <div style='max-width:500px;margin:auto;background:white;padding:30px;border-radius:12px;
                    box-shadow:0 5px 20px rgba(0,0,0,0.08)'>

            <h2 style='margin-bottom:10px;color:#111827'>
                🔑 Recuperación de contraseña
            </h2>

            <p style='color:#6b7280;font-size:14px'>
                Hola <b>{user.Username}</b>, usa este código para restablecer tu contraseña:
            </p>

            <div style='margin:25px 0;text-align:center'>
                <span style='font-size:30px;font-weight:bold;
                             letter-spacing:6px;
                             background:#2563eb;
                             color:white;
                             padding:12px 24px;
                             border-radius:10px;
                             display:inline-block'>
                    {code}
                </span>
            </div>

            <p style='font-size:13px;color:#9ca3af'>
                Este código expira en 10 minutos. No lo compartas con nadie.
            </p>

            <hr style='margin:25px 0;border:none;border-top:1px solid #eee'>

            <p style='font-size:12px;color:#9ca3af;text-align:center'>
                HabitTracker Pro 🚀
            </p>

        </div>
    </div>";

            await _emailService.SendEmailAsync(
                user.Email,
                "🔑 Recupera tu contraseña - HabitTracker :)",
                html
            );
        }

        [HttpGet]
        public IActionResult ExternalLogin(string provider)
        {
            var redirectUrl = Url.Action("ExternalLoginCallback", "Account");

            var properties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            };

            return Challenge(properties, provider);
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback()
        {
            try
            {
                string ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ip))
                    ip = ip.Split(',').First().Trim();
                else
                    ip = HttpContext.Connection.RemoteIpAddress?.ToString();

                var blocked = _context.BlockedIPs.FirstOrDefault(x => x.IpAddress == ip);
                if (blocked != null)
                    return RedirectToAction("Login", new { ipblocked = true });

                var result = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
                if (!result.Succeeded)
                {
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return RedirectToAction("Login");
                }

                var email = result.Principal.FindFirst(ClaimTypes.Email)?.Value;
                if (email == null) return RedirectToAction("Login");

                // 🔌 Verificar bloqueo de conexión — mostrar página de bloqueo
                var userCheck = _context.Users.FirstOrDefault(u => u.Email == email);
                if (userCheck != null && userCheck.IsIpBlocked)
                {
                    foreach (var cookie in HttpContext.Request.Cookies.Keys)
                        HttpContext.Response.Cookies.Delete(cookie);
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return RedirectToAction("Login", new { blocked = true });
                }

                var name = result.Principal.FindFirst(ClaimTypes.Name)?.Value;
                var picture = result.Principal.FindFirst("picture")?.Value;

                var user = _context.Users.FirstOrDefault(u => u.Email == email);

                if (user == null)
                {
                    user = new User
                    {
                        Email = email,
                        Username = name ?? email,
                        ProfilePicture = picture,
                        EmailConfirmed = true,
                        IsActive = true,
                        Gender = "No especificado",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                        IsGoogleAccount = true,
                        Role = "User"
                    };
                    _context.Users.Add(user);
                    _context.SaveChanges();
                }
                else
                {
                    if (!user.IsGoogleAccount)
                    {
                        user.IsGoogleAccount = true;
                        _context.SaveChanges();
                    }
                }

                user.LastOnline = DateTime.Now;
                var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
                user.Device = GetDevice(userAgent);
                user.OperatingSystem = GetOS(userAgent);
                user.Browser = GetBrowser(userAgent);
                user.LastIp = ip;

                try
                {
                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    var geoJson = await httpClient.GetStringAsync($"https://ipwho.is/{ip}");
                    var geoDoc = System.Text.Json.JsonDocument.Parse(geoJson);
                    var geoRoot = geoDoc.RootElement;
                    if (geoRoot.GetProperty("success").GetBoolean())
                    {
                        user.Country = geoRoot.GetProperty("country").GetString();
                        user.City = geoRoot.GetProperty("city").GetString();
                        user.Latitude = geoRoot.GetProperty("latitude").GetDouble();
                        user.Longitude = geoRoot.GetProperty("longitude").GetDouble();
                    }
                }
                catch { }

                _context.SaveChanges();

                var sessionToken = Guid.NewGuid().ToString();
                var claims = new List<Claim>
        {
            new Claim("UserId", user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role ?? "User"),
            new Claim("ProfileImage", user.ProfileImage ?? user.ProfilePicture ?? ""),
            new Claim("SessionToken", sessionToken)
        };
                var identity = new ClaimsIdentity(claims, "Cookies");
                var principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync("Cookies", principal);

                try
                {
                    _context.UserSessions.Add(new UserSession
                    {
                        UserId = user.Id,
                        SessionToken = sessionToken,
                        Device = GetDevice(userAgent),
                        Browser = GetBrowser(userAgent),
                        IpAddress = ip ?? "",
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    });
                    await _context.SaveChangesAsync();
                    await _hubContext.Clients.Group(user.Id.ToString()).SendAsync("NewSessionDetected");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error guardando sesión: {ex.Message}");
                }

                bool perfilIncompleto = string.IsNullOrEmpty(user.Gender)
                    || user.Gender == "No especificado"
                    || string.IsNullOrEmpty(user.Bio)
                    || user.Bio == "Registrado con Google";

                if (perfilIncompleto)
                    return RedirectToAction("CompleteProfile", "Account");

                return RedirectToAction("Index", "Habit");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ExternalLoginCallback error: {ex.Message}");
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction("Login");
            }
        }
        [HttpGet]
        public IActionResult GuestRegister()
        {
            if (User.Identity.IsAuthenticated && User.IsInRole("Guest"))
                return RedirectToAction("Index", "Habit");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuestRegister(string username)
        {
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrEmpty(ip))
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            var blockedIp = _context.BlockedIPs.FirstOrDefault(b => b.IpAddress == ip);
            if (blockedIp != null)
                return RedirectToAction("Login", new { ipblocked = true });

            var blockedUser = _context.Users.FirstOrDefault(u => u.LastIp == ip && u.IsIpBlocked);
            if (blockedUser != null)
                return RedirectToAction("Login", new { blocked = true });

            if (string.IsNullOrWhiteSpace(username))
            {
                ModelState.AddModelError("", "El nombre de usuario es obligatorio.");
                return View();
            }

            if (_context.Users.Any(u => u.Username == username))
            {
                ModelState.AddModelError("", "Ese usuario ya existe.");
                return View();
            }

            var user = new User
            {
                Username = username,
                Email = $"guest_{Guid.NewGuid()}@guest.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                EmailConfirmed = true,
                Role = "Guest",
                CreatedAt = DateTime.Now,
                IsActive = true,
                IsGoogleAccount = false,
                Gender = "No especificado",
                FullName = "Invitado",
                Bio = "Usuario invitado"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await SignInUser(user);
            HttpContext.Session.SetString("Guest", "true");

            return RedirectToAction("Index", "Habit");
        }
        [HttpGet]
        public IActionResult UpgradeAccount()
        {
            return View();
        }

        private async Task RegistrarSesion(int userId)
        {
            try
            {
                var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
                var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                         ?? HttpContext.Connection.RemoteIpAddress?.ToString();
                var sessionToken = User.FindFirst("SessionToken")?.Value;

                if (!string.IsNullOrEmpty(sessionToken))
                {
                    var exists = _context.UserSessions.Any(s => s.SessionToken == sessionToken);
                    if (!exists)
                    {
                        _context.UserSessions.Add(new UserSession
                        {
                            UserId = userId,
                            SessionToken = sessionToken,
                            Device = GetDevice(userAgent),
                            Browser = GetBrowser(userAgent),
                            IpAddress = ip ?? "",
                            CreatedAt = DateTime.UtcNow,
                            IsActive = true
                        });
                        await _context.SaveChangesAsync();
                        Console.WriteLine($"✅ Sesión Google guardada userId={userId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sesión Google: {ex.Message}");
            }
        }

        public IActionResult GoogleLogin()
        {
            // 🔥 OBTENER IP
            string ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrEmpty(ip))
            {
                ip = ip.Split(',').First().Trim();
            }
            else
            {
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            // 🔥 VALIDAR BLOQUEO ANTES DE GOOGLE
            var blocked = _context.BlockedIPs.FirstOrDefault(x => x.IpAddress == ip);

            if (blocked != null)
            {
                return RedirectToAction("Login", new { ipblocked = true });
            }

            var redirectUrl = Url.Action("GoogleResponse", "Account");

            var properties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            };

            return Challenge(properties, "Google");
        }

        public async Task<IActionResult> GoogleResponse()
        {
            string ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ip))
                ip = ip.Split(',').First().Trim();
            else
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var blocked = _context.BlockedIPs.FirstOrDefault(x => x.IpAddress == ip);
            if (blocked != null)
                return RedirectToAction("Login", new { ipblocked = true });

            var result = await HttpContext.AuthenticateAsync("Cookies");
            if (!result.Succeeded)
            {
                foreach (var cookie in HttpContext.Request.Cookies.Keys)
                    HttpContext.Response.Cookies.Delete(cookie);
                return RedirectToAction("Login");
            }

            var claims = result.Principal.Identities.FirstOrDefault()?.Claims;
            var email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            var name = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            var picture = claims?.FirstOrDefault(c => c.Type == "picture")?.Value;

            if (email == null) return RedirectToAction("Login");

            // Verificar bloqueo de conexión por email
            var userCheck = _context.Users.FirstOrDefault(u => u.Email == email);
            if (userCheck != null && userCheck.IsIpBlocked)
            {
                foreach (var cookie in HttpContext.Request.Cookies.Keys)
                    HttpContext.Response.Cookies.Delete(cookie);
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction("Login", new { blocked = true });
            }

            // 🔥 Invitado → convertir
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim != null)
            {
                int currentUserId = int.Parse(userIdClaim.Value);
                var currentUser = _context.Users.FirstOrDefault(u => u.Id == currentUserId);

                if (currentUser != null && currentUser.Role == "Guest")
                {
                    currentUser.Email = email;
                    currentUser.Username = name ?? email;
                    currentUser.Role = "User";
                    currentUser.EmailConfirmed = true;
                    currentUser.IsGoogleAccount = true;
                    currentUser.Gender = "No especificado";
                    currentUser.FullName = name ?? "Usuario";
                    currentUser.Bio = "Registrado con Google";
                    currentUser.IsActive = true;
                    _context.SaveChanges();

                    await SignInUser(currentUser);
                    return RedirectToAction("CompleteProfile", "Account");
                }
            }

            // 🔥 Flujo normal
            var user = _context.Users.FirstOrDefault(u => u.Email == email);
            bool isNewUser = false;

            if (user == null)
            {
                isNewUser = true;
                user = new User
                {
                    Username = name ?? email,
                    Email = email,
                    EmailConfirmed = true,
                    Role = "User",
                    CreatedAt = DateTime.Now,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    Gender = "No especificado",
                    FullName = name ?? "Usuario",
                    Bio = "Registrado con Google",
                    IsActive = true,
                    IsGoogleAccount = true
                };
                _context.Users.Add(user);
                _context.SaveChanges();
            }

            // 🔥 Foto de Google
            if (string.IsNullOrEmpty(user.ProfileImage) && !string.IsNullOrEmpty(picture))
            {
                user.ProfilePicture = picture;
                _context.SaveChanges();
            }

            // 🔥 generar token de sesión
            var sessionToken = Guid.NewGuid().ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

            // 🔥 SignIn con token incluido
            var sessionClaims = new List<Claim>
    {
        new Claim("UserId", user.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username ?? "Usuario"),
        new Claim(ClaimTypes.Role, user.Role ?? "User"),
        new Claim("ProfileImage", user.ProfileImage ?? user.ProfilePicture ?? ""),
        new Claim("SessionToken", sessionToken)
    };
            var sessionIdentity = new ClaimsIdentity(sessionClaims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(sessionIdentity));

            // 🔥 guardar sesión en BD
            try
            {
                _context.UserSessions.Add(new UserSession
                {
                    UserId = user.Id,
                    SessionToken = sessionToken,
                    Device = GetDevice(userAgent),
                    Browser = GetBrowser(userAgent),
                    IpAddress = ip ?? "",
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await _context.SaveChangesAsync();
                Console.WriteLine($"✅ Sesión GoogleResponse guardada userId={user.Id}");

                // 🔥 notificar en tiempo real a otras sesiones
                await _hubContext.Clients.Group(user.Id.ToString())
                    .SendAsync("NewSessionDetected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error sesión GoogleResponse: {ex.Message}");
            }

            bool perfilIncompleto = string.IsNullOrEmpty(user.Gender)
                || user.Gender == "No especificado"
                || string.IsNullOrEmpty(user.Bio)
                || user.Bio == "Registrado con Google";

            if (isNewUser || perfilIncompleto)
                return RedirectToAction("CompleteProfile", "Account");

            return RedirectToAction("Index", "Habit");
        }

        [HttpPost]
        public async Task<IActionResult> CloseSession([FromBody] CloseSessionDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var session = _context.UserSessions
                .FirstOrDefault(s => s.Id == dto.SessionId && s.UserId == myId);

            if (session == null) return Json(new { success = false });

            session.IsActive = false;
            await _context.SaveChangesAsync();

            // 🔥 forzar logout solo en esa sesión via SignalR
            await _hubContext.Clients.Group(myId.ToString())
                .SendAsync("ForceLogoutSession", session.SessionToken);

            return Json(new { success = true });
        }

        public class CloseSessionDto { public int SessionId { get; set; } }

        [HttpPost]
        public async Task<IActionResult> GuestLoginExisting(string username)
        {
            var user = _context.Users
                .FirstOrDefault(u => u.Username == username && u.Role == "Guest");

            if (user == null)
            {
                ModelState.AddModelError("", "Ese usuario invitado no existe.");
                return View("GuestRegister");
            }

            await SignInUser(user);

            return RedirectToAction("Index", "Habit");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpgradeAccount(string email, string password)
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
                return RedirectToAction("Login");

            int userId = int.Parse(userIdClaim.Value);

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return RedirectToAction("Login");

            // 🔥 VALIDAR QUE SEA INVITADO
            if (user.Role != "Guest")
            {
                return RedirectToAction("Index", "Habit");
            }

            // 🔥 VALIDACIONES
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Todos los campos son obligatorios.");
                return View();
            }

            if (_context.Users.Any(u => u.Email == email))
            {
                ModelState.AddModelError("", "Ese correo ya está en uso.");
                return View();
            }

            // 🔥 CONVERTIR A USUARIO REAL
            user.Email = email;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.Role = "User";
            user.EmailConfirmed = false;

            _context.SaveChanges();

            // 🔥 opcional: enviar código de confirmación
            await SendConfirmationCode(user);

            TempData["ResetEmail"] = user.Email;

            return RedirectToAction("ConfirmEmail");
        }

        public async Task<IActionResult> GuestLogin()
        {
            var claims = new List<Claim>
    {
        new Claim("UserId", "0"),
        new Claim(ClaimTypes.Name, "Invitado"),
        new Claim("IsGuest", "true")
    };

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync("Cookies", principal);

            return RedirectToAction("Index", "Habit");
        }

        [HttpGet]
        public IActionResult CompleteProfile()
        {
            return View();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken] // 🔥 Google OAuth rompe el token
        public async Task<IActionResult> CompleteProfile(string gender, string bio)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");

            var userId = int.Parse(userIdClaim.Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login");

            user.Gender = gender;
            user.Bio = bio;
            _context.SaveChanges();

            _ = SendWelcomeEmail(user);

            await RefreshUserSession(user);

            return RedirectToAction("CreandoCuenta", "Account");
        }


        public IActionResult CreandoCuenta()
        {
            return View();
        }
        private string GetOS(string userAgent)
        {
            if (userAgent.Contains("Android")) return "Android";
            if (userAgent.Contains("iPhone")) return "iOS";
            if (userAgent.Contains("Mac")) return "MacOS";
            if (userAgent.Contains("Windows")) return "Windows";
            if (userAgent.Contains("Linux")) return "Linux";

            return "Desconocido";
        }

        private string GetBrowser(string userAgent)
        {
            if (userAgent.Contains("Chrome")) return "Chrome";
            if (userAgent.Contains("Firefox")) return "Firefox";
            if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome")) return "Safari";
            if (userAgent.Contains("Edg")) return "Edge";

            return "Desconocido";
        }

        private string GetDevice(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
                return "Desconocido";

            if (userAgent.Contains("Android"))
            {
                try
                {
                    var start = userAgent.IndexOf("(");
                    var end = userAgent.IndexOf(")");

                    if (start != -1 && end != -1)
                    {
                        var info = userAgent.Substring(start + 1, end - start - 1);
                        var parts = info.Split(';');

                        foreach (var part in parts)
                        {
                            var text = part.Trim();

                            if (string.IsNullOrWhiteSpace(text))
                                continue;

                            // ignorar cosas inútiles
                            if (text.Contains("Android") || text.Contains("Linux"))
                                continue;

                            if (text.Length <= 2)
                                continue;

                            return text;
                        }
                    }
                }
                catch { }

                return "Android";
            }

            if (userAgent.Contains("iPhone"))
                return "iPhone";

            if (userAgent.Contains("iPad"))
                return "iPad";

            if (userAgent.Contains("Windows"))
                return "PC";

            if (userAgent.Contains("Mac"))
                return "Mac";

            return "Desconocido";
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

        [HttpPost]
        public async Task<IActionResult> SaveLocation([FromBody] LocationDto data)
        {
            var claim = User.FindFirst("UserId");

            if (claim == null)
            {
                return Ok(); // usuario no logueado
            }

            var userId = int.Parse(claim.Value);

            var user = _context.Users.FirstOrDefault(x => x.Id == userId);

            if (user == null)
                return Ok();

            user.Latitude = data.latitude;
            user.Longitude = data.longitude;

            // recalcular municipio SIEMPRE
            var municipality = await GetMunicipality(data.latitude, data.longitude);

            user.Municipality = municipality;

            await _context.SaveChangesAsync();

            return Ok();
        }

        public class LocationDto
        {
            public double latitude { get; set; }

            public double longitude { get; set; }
        }


        private async Task<string> GetMunicipality(double lat, double lon)
        {
            try
            {
                using var client = new HttpClient();

                client.DefaultRequestHeaders.UserAgent.ParseAdd("HabitTracker");

                var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={lat}&lon={lon}";

                var response = await client.GetStringAsync(url);

                var json = Newtonsoft.Json.Linq.JObject.Parse(response);

                var address = json["address"];

                if (address == null)
                    return "Desconocido";

                var municipality =
    address["municipality"]?.ToString() ??
    address["town"]?.ToString() ??
    address["village"]?.ToString() ??
    address["suburb"]?.ToString() ??
    address["city_district"]?.ToString() ??
    address["city"]?.ToString() ??
    address["county"]?.ToString() ??
    address["state_district"]?.ToString() ??
    "Desconocido";

                return municipality;
            }
            catch
            {
                return "Desconocido";
            }
        }
    }
}