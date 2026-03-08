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
        public async Task<IActionResult> Login()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return View();
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
            return RedirectToAction("Index", "Habit");
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
        [ValidateAntiForgeryToken]
        public IActionResult ConfirmEmail(ConfirmEmailViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = _context.Users
                .FirstOrDefault(u => u.Email == model.Email || u.PendingEmail == model.Email);

            if (user == null ||
    user.ResetCode != model.Code ||
    user.ResetCodeExpiry == null ||
    user.ResetCodeExpiry < DateTime.Now)
            {
                ModelState.AddModelError("", "Código inválido o expirado.");

                // mantener el correo en el formulario
                model.Email = TempData.Peek("ResetEmail")?.ToString();

                return View(model);
            }

            if (user.PendingEmail != null)
            {
                // 🔐 verificar si el correo ya existe
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

            TempData["Success"] = "Correo confirmado correctamente.";

            if (TempData["FromRegister"] != null)
                return RedirectToAction("Login");

            if (User.Identity != null && User.Identity.IsAuthenticated)
                return RedirectToAction("Profile", "Account");

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
        public IActionResult Profile()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }

            var claim = User.FindFirst("UserId");

            if (claim == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var userId = int.Parse(claim.Value);

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (user.PendingEmail != null)
                TempData["PendingEmail"] = user.PendingEmail;

            return View("~/Views/Account/Profile.cshtml", user);
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

            if (emailChanged)
            {
                user.PendingEmail = updatedUser.Email;

                await SendConfirmationCode(user);

                _context.SaveChanges();

                TempData["ResetEmail"] = user.PendingEmail;

                return RedirectToAction("ConfirmEmail");
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

            await _emailService.SendEmailAsync(
                user.PendingEmail ?? user.Email,
                "Confirma tu cuenta - HabitTracker",
                $"<h3>Tu código es:</h3><h1>{code}</h1>"
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

            return RedirectToAction("ConfirmEmail");
        }

        private async Task SendResetCode(User user)
        {
            var random = new Random();

            string code = random.Next(100000, 999999).ToString();

            user.ResetCode = code;
            user.ResetCodeExpiry = DateTime.Now.AddMinutes(10);

            _context.SaveChanges();

            await _emailService.SendEmailAsync(
                user.Email,
                "Código de recuperación",
                $"<h3>Tu código es:</h3><h1>{code}</h1>"
            );
        }
    }
}