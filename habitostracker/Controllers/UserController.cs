using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HabitTrackerApp.Controllers
{

    [Authorize]
    public class UserController : Controller
    {
        private readonly HabitDbContext _context;

        public UserController(HabitDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var users = _context.Users
                .OrderByDescending(u => u.Role == "SuperAdmin") // 👑 primero propietario
                .ThenByDescending(u => u.Role == "Admin")       // luego admins
                .ThenBy(u => u.Username)                        // luego usuarios
                .ToList();

            return View(users);
        }

        // =====================================
        // 👤 VER PERFIL DE OTRO USUARIO
        // =====================================
        public IActionResult Profile(int id)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
                return NotFound();

            // 🔒 impedir ver el perfil del propietario
            if (user.Role == "SuperAdmin")
            {
                return RedirectToAction("Index");
            }

            return View(user);
        }

        // =====================================
        // 👥 ENVIAR SOLICITUD DE AMISTAD
        // =====================================
        [HttpPost]
        public IActionResult SendFriendRequest(int receiverId)
        {
            var senderId = int.Parse(User.FindFirst("UserId").Value);

            // ❌ evitar enviarse solicitud a sí mismo
            if (senderId == receiverId)
                return RedirectToAction("Profile", new { id = receiverId });

            // ❌ evitar solicitudes duplicadas
            var exists = _context.FriendRequests
                .Any(r => r.SenderId == senderId && r.ReceiverId == receiverId);

            if (exists)
                return RedirectToAction("Profile", new { id = receiverId });

            var request = new FriendRequest
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Status = "Pending"
            };

            _context.FriendRequests.Add(request);
            _context.SaveChanges();

            return RedirectToAction("Profile", new { id = receiverId });
        }

        // =====================================
        // 📩 VER SOLICITUDES RECIBIDAS
        // =====================================
        public IActionResult FriendRequests()
        {
            var claim = User.FindFirst("UserId");

            if (claim == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var userId = int.Parse(claim.Value);

            var requests = _context.FriendRequests
                .Where(r => r.ReceiverId == userId && r.Status == "Pending")
                .Select(r => new FriendRequestViewModel
                {
                    Id = r.Id,
                    SenderId = r.SenderId,
                    SenderUsername = _context.Users
                        .Where(u => u.Id == r.SenderId)
                        .Select(u => u.Username)
                        .FirstOrDefault(),

                    ProfileImage = _context.Users
                        .Where(u => u.Id == r.SenderId)
                        .Select(u => u.ProfileImage)
                        .FirstOrDefault()
                })
                .ToList();

            return View(requests);
        }

        // =====================================
        // ✅ ACEPTAR SOLICITUD
        // =====================================
        [HttpPost]
        public IActionResult AcceptFriendRequest(int requestId)
        {
            var request = _context.FriendRequests.FirstOrDefault(r => r.Id == requestId);

            if (request == null)
                return NotFound();

            request.Status = "Accepted";

            _context.SaveChanges();

            return RedirectToAction("FriendRequests");
        }

        // =====================================
        // ❌ RECHAZAR SOLICITUD
        // =====================================
        [HttpPost]
        public IActionResult RejectFriendRequest(int requestId)
        {
            var request = _context.FriendRequests.FirstOrDefault(r => r.Id == requestId);

            if (request == null)
                return NotFound();

            request.Status = "Rejected";

            _context.SaveChanges();

            return RedirectToAction("FriendRequests");
        }

        // =====================================
        // 👥 LISTA DE AMIGOS
        // =====================================
        public IActionResult Friends()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var friends = _context.FriendRequests
                .Where(r => (r.SenderId == userId || r.ReceiverId == userId) && r.Status == "Accepted")
                .ToList();

            return View(friends);
        }

        // =====================================
        // 🏆 RANKING DE AMIGOS
        // =====================================
        public IActionResult Ranking()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            // obtener amigos aceptados
            var friendRequests = _context.FriendRequests
                .Where(r => (r.SenderId == userId || r.ReceiverId == userId) && r.Status == "Accepted")
                .ToList();

            var friendIds = friendRequests
                .Select(r => r.SenderId == userId ? r.ReceiverId : r.SenderId)
                .ToList();

            // incluir también al usuario actual
            friendIds.Add(userId);

            var ranking = _context.Users
                .Where(u => friendIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Username,
                    Streak = _context.Habits
                        .Where(h => h.UserId == u.Id)
                        .Select(h => h.StreakDays)
                        .DefaultIfEmpty(0)
                        .Max()
                })
                .OrderByDescending(x => x.Streak)
                .ToList();

            return View(ranking);
        }
    }
}