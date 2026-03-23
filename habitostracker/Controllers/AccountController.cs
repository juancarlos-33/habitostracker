using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.IO;
using System.Security.Claims;
using HabitTrackerApp.Hubs;
using BCrypt.Net;

namespace HabitTrackerApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly EmailService _emailService;
        private readonly IWebHostEnvironment _environment;

        public AccountController(
           HabitDbContext context,
           EmailService emailService,
           IWebHostEnvironment environment,
           IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _emailService = emailService;
            _environment = environment;
            _hubContext = hubContext;
        }

        // =====================================================
        // 🔐 LOGIN
        // =====================================================
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Habit");
            }

            return View();
        }

        // ===== SEGURIDAD =====
        [HttpGet]
        public IActionResult Security()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login");

            var userId = int.Parse(userIdClaim.Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login");

            return View(user);
        }

        // ===== ELIMINAR CUENTA =====
        [HttpPost]
        public async Task<IActionResult> DeleteAccount(string password)
        {
            // 🔥 validar que ingresó contraseña
            if (string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "Por favor ingresa tu contraseña para confirmar.";
                return RedirectToAction("Security");
            }

            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null) return NotFound();

            // 🔥 contraseña incorrecta
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                TempData["Error"] = "Contraseña incorrecta. Intenta de nuevo.";
                return RedirectToAction("Security");
            }

            // 🔥 eliminar datos del usuario
            var habits = _context.Habits.Where(h => h.UserId == userId);
            _context.Habits.RemoveRange(habits);

            var messages = _context.Messages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .ToList();

            foreach (var msg in messages)
            {
                if (msg.SenderId == userId) msg.SenderId = null;
                if (msg.ReceiverId == userId) msg.ReceiverId = null;
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Login");
        }

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
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok();
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
        public async Task<IActionResult> Login(LoginViewModel login)
        {
            if (!ModelState.IsValid)
                return View(login);

            var user = _context.Users.FirstOrDefault(u => u.Username == login.Username);

            if (user == null)
            {
                ModelState.AddModelError("", "El usuario no existe.");
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
                ModelState.AddModelError("", $"Cuenta bloqueada. Intenta nuevamente en {minutes} minuto(s).");
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

                    ModelState.AddModelError("", "Demasiados intentos fallidos. Cuenta bloqueada por 10 minutos.");
                    return View(login);
                }

                _context.SaveChanges();

                ModelState.AddModelError("", $"La contraseña es incorrecta. Intento {user.FailedLoginAttempts}/5");
                return View(login);
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;

            // 🟢 ACTUALIZAR ÚLTIMA VEZ ONLINE
            user.LastOnline = DateTime.Now;

            // 🟢 GUARDAR IP DEL USUARIO
            var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

            user.Device = GetDevice(userAgent);
            user.OperatingSystem = GetOS(userAgent);
            user.Browser = GetBrowser(userAgent);

            if (string.IsNullOrEmpty(ip))
            {
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            }




            user.LastIp = ip;

            if (user.LastIp != ip)
            {
                var info = await GetIPInfo(ip);

                user.Country = info.country;
                user.City = info.city;
                user.ISP = info.isp;
            }
            _context.SaveChanges();

            if (!user.EmailConfirmed)
            {
                TempData["UnconfirmedEmail"] = user.Email;
                ModelState.AddModelError("", "Debes confirmar tu correo antes de iniciar sesión.");
                return View(login);
            }

            await SignInUser(user);

            var admins = _context.Users
                .Where(u => u.Role == "SuperAdmin" || u.Role == "Admin")
                .ToList();

            foreach (var admin in admins)
            {
                if (user.Role != "SuperAdmin") // evitar que se notifique a sí mismo
                {
                    await _hubContext.Clients.User(admin.Id.ToString())
                        .SendAsync("UserConnectedNotification", user.Username);
                }
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
            if (!ModelState.IsValid)
                return View(model);

            var user = _context.Users.FirstOrDefault(u => u.Email == model.Email);

            if (user == null)
            {
                TempData["Error"] = "Usuario no encontrado";
                return RedirectToAction("Login");
            }

            // 🔐 guardar nueva contraseña encriptada
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

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

            var newUser = new User
            {
                Username = model.Username,
                Email = model.Email,
                Gender = model.Gender,
                Bio = model.Bio,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                CreatedAt = DateTime.Now,
                Role = "User",
                EmailConfirmed = false
            };

            _context.Users.Add(newUser);
            _context.SaveChanges();

            await SendConfirmationCode(newUser);
            _context.SaveChanges();

            TempData["ResetEmail"] = newUser.Email;
            TempData["FromRegister"] = true;

            return RedirectToAction("ConfirmEmail");
        }

        // =====================================================
        // 📧 CONFIRM EMAIL
        // =====================================================
        [HttpGet]
        public IActionResult ConfirmEmail()
        {
            var model = new ConfirmEmailViewModel
            {
                Email = TempData.Peek("ResetEmail")?.ToString()
            };

            return View(model);
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
        [ValidateAntiForgeryToken]
        public IActionResult ConfirmEmail(ConfirmEmailViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = _context.Users
                .FirstOrDefault(u => u.Email == model.Email || u.PendingEmail == model.Email);

            if (user == null || user.ResetCode != model.Code || user.ResetCodeExpiry == null || user.ResetCodeExpiry < DateTime.Now)
            {
                ModelState.AddModelError("", "Código inválido o expirado.");
                model.Email = TempData.Peek("ResetEmail")?.ToString();
                return View(model);
            }

            if (user.PendingEmail != null)
            {
                if (_context.Users.Any(u => u.Email == user.PendingEmail && u.Id != user.Id))
                {
                    ModelState.AddModelError("", "Ese correo ya está siendo usado por otro usuario.");
                    return View(model);
                }

                user.Email = user.PendingEmail;
                user.PendingEmail = null;
            }

            user.EmailConfirmed = true;
            user.ResetCode = null;
            user.ResetCodeExpiry = null;

            _context.SaveChanges();

            // 🔥 DIFERENCIAR FLUJO

            // 👉 SI VIENE DE REGISTRO
            if (TempData["FromRegister"] != null)
            {
                TempData["Success"] = "Cuenta verificada correctamente 🎉";
                return RedirectToAction("Login");
            }

            // 👉 SI VIENE DE RECUPERAR CONTRASEÑA
            if (TempData["FromReset"] != null)
            {
                TempData["Success"] = "Código verificado. Ahora crea tu nueva contraseña 🔐";
                return RedirectToAction("ResetPassword", new { email = user.Email });
            }

            // 👉 fallback
            return RedirectToAction("Login");
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
                if (me == null) return RedirectToAction("Login", "Account");

                if (me.PendingEmail != null)
                    TempData["PendingEmail"] = me.PendingEmail;

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

            bool emailChanged = user.Email != updatedUser.Email;

            user.FullName = updatedUser.FullName;
            user.Bio = updatedUser.Bio;

            if (croppedImage == "REMOVE")
            {
                if (!string.IsNullOrEmpty(user.ProfileImage))
                {
                    var oldPath = Path.Combine(_environment.WebRootPath, user.ProfileImage.TrimStart('/'));

                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                user.ProfileImage = null;
            }

            else if (!string.IsNullOrEmpty(croppedImage))
            {
                var base64Data = croppedImage.Split(',')[1];
                byte[] imageBytes = Convert.FromBase64String(base64Data);

                string uploadsFolder = Path.Combine(_environment.WebRootPath, "profiles");

                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                string fileName = "user_" + userId + ".png";

                string filePath = Path.Combine(uploadsFolder, fileName);

                await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

                user.ProfileImage = "/profiles/" + fileName;
            }

            else if (profilePhoto != null && profilePhoto.Length > 0)
            {
                string uploadsFolder = Path.Combine(_environment.WebRootPath, "profiles");

                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                string fileName = "user_" + userId + Path.GetExtension(profilePhoto.FileName);

                string filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await profilePhoto.CopyToAsync(stream);
                }

                user.ProfileImage = "/profiles/" + fileName;
            }

          

            _context.SaveChanges();

            await SignInUser(user);

            TempData["Success"] = "Perfil actualizado correctamente.";

            return RedirectToAction("Profile", "Account");
        }

        // =====================================================
        // 🔐 CAMBIAR CONTRASEÑA
        // =====================================================
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
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

            return RedirectToAction("Login");
        }




        // =====================================================
        // 🔑 MÉTODO LOGIN
        // =====================================================
        private async Task SignInUser(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("UserId", user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role ?? "User"),
                new Claim("ProfileImage", user.ProfileImage ?? "")
            };

            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            //REFRESHUSERSESSION
        }
        public async Task RefreshUserSession(User user)
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim("UserId", user.Id.ToString()),
        new Claim(ClaimTypes.Role, user.Role ?? "User"),
        new Claim("ProfileImage", user.ProfileImage ?? "")
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
                    address["town"]?.ToString() ??
                    address["city"]?.ToString() ??
                    address["village"]?.ToString() ??
                    address["county"]?.ToString() ??
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