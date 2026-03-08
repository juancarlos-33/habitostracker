using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly HabitDbContext _context;

        public ProfileController(HabitDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst("UserId").Value);
        }

        // 📄 Ver perfil
        public IActionResult Index()
        {
            var userId = GetUserId();
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            return View(user);
        }

        // ✏ Actualizar perfil
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Update(User updatedUser)
        {
            var userId = GetUserId();
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null) return NotFound();

            user.FullName = updatedUser.FullName;
            user.Email = updatedUser.Email;

            _context.SaveChanges();

            TempData["Success"] = "Perfil actualizado correctamente.";

            return RedirectToAction("Index");
        }
    }
}