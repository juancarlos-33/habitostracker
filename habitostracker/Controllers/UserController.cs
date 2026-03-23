using HabitTrackerApp.Data;
using HabitTrackerApp.Hubs;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace HabitTrackerApp.Controllers
{

    [Authorize]
    public class UserController : Controller
    {

        private readonly IHubContext<ChatHub> _hubContext;
        private readonly HabitDbContext _context;
        private readonly OnlineUsersService _onlineUsers;


        public UserController(
    HabitDbContext context,
    IHubContext<ChatHub> hubContext,
    OnlineUsersService onlineUsers)
        {
            _context = context;
            _hubContext = hubContext;
            _onlineUsers = onlineUsers;
        }

        public IActionResult Index()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var users = _context.Users
                .Where(u => u.Role != "SuperAdmin")
                .OrderByDescending(u => u.Role == "Admin")
                .ThenBy(u => u.Username)
                .ToList();

            // 🔥 solicitudes ya enviadas por mí
            var sentRequests = _context.FriendRequests
                .Where(f => f.SenderId == myId && f.Status == "Pending")
                .Select(f => f.ReceiverId)
                .ToList();

            // 🔥 amigos ya aceptados
            var friends = _context.FriendRequests
                .Where(f => (f.SenderId == myId || f.ReceiverId == myId) && f.Status == "Accepted")
                .Select(f => f.SenderId == myId ? f.ReceiverId : f.SenderId)
                .ToList();

            ViewBag.SentRequests = sentRequests;
            ViewBag.Friends = friends;

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

            // contar seguidores
            ViewBag.Followers = _context.Follows
                .Count(f => f.FollowingId == id);

            // contar a quién sigue
            ViewBag.Following = _context.Follows
                .Count(f => f.FollowerId == id);


         

            return View(user);
        }


        [HttpPost]
        public IActionResult UploadPayment(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Debes subir un comprobante";
                return RedirectToAction("Pay");
            }

            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return NotFound();

            // 📁 carpeta donde se guardan imágenes
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/payments");

            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            // 📸 nombre único
            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);

            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            // guardar en BD
            user.PaymentProofImage = "/uploads/payments/" + fileName;
            user.PaymentApproved = false;

            _context.SaveChanges();

            TempData["Success"] = "Comprobante enviado. Espera aprobación del admin 😎";

            return RedirectToAction("Index", "Habit");
        }

        [HttpPost]
        public IActionResult MakePremium(IFormFile screenshot)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            if (screenshot == null || screenshot.Length == 0)
            {
                TempData["Error"] = "Debes subir un comprobante";
                return RedirectToAction("Pay");
            }

            // 📁 guardar imagen
            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(screenshot.FileName);
            var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/payments", fileName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                screenshot.CopyTo(stream);
            }

            // 💾 guardar en BD
            var payment = new Payment
            {
                UserId = userId,
                Screenshot = "/payments/" + fileName,
                CreatedAt = DateTime.Now
            };

            _context.Payments.Add(payment);
            _context.SaveChanges();

            TempData["Success"] = "Comprobante enviado. Espera aprobación 😈";

            return RedirectToAction("Index", "Habit");
        }
        public IActionResult Pay()
        {
            return View();
        }


        // =====================================
        // 👥 ENVIAR SOLICITUD DE AMISTAD
        [HttpPost]
        public IActionResult SendFriendRequest(int receiverId)
        {
            var senderId = int.Parse(User.FindFirst("UserId").Value);

            var receiver = _context.Users.FirstOrDefault(u => u.Id == receiverId);

            if (receiver != null && receiver.Role == "SuperAdmin")
            {
                return RedirectToAction("Index");
            }

            var username = User.Identity.Name;

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

            // 🔎 obtener info del usuario que envía la solicitud
            var sender = _context.Users.FirstOrDefault(u => u.Id == senderId);

            // 🔔 crear notificación
            var notification = new Notification
            {
                UserId = receiverId,
                FromUserId = senderId,
                FromUsername = username,
                FromUserImage = sender?.ProfileImage ?? "",
                Message = username + " te envió una solicitud de amistad",
                Link = "/User/FriendRequests",
                IsRead = false,
                CreatedAt = DateTime.Now
            };

            _context.Notifications.Add(notification);

            // 🔔 notificación en tiempo real
            _hubContext.Clients.User(receiverId.ToString())
                .SendAsync("ReceiveNotification", username + " te envió una solicitud de amistad");

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

        //PARA SEGUIR
        [HttpPost]
        public IActionResult Follow(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var targetUser = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (targetUser != null && targetUser.Role == "SuperAdmin")
            {
                return RedirectToAction("Index");
            }

            if (myId == userId)
                return RedirectToAction("Index");

            var alreadyFollowing = _context.Follows
                .FirstOrDefault(f => f.FollowerId == myId && f.FollowingId == userId);

            if (alreadyFollowing == null)
            {
                var follow = new Follow
                {
                    FollowerId = myId,
                    FollowingId = userId,
                    CreatedAt = DateTime.Now
                };

                _context.Follows.Add(follow);
                _context.SaveChanges();
            }

            return RedirectToAction("Index");
        }

        //DEJAR DE SEGUIR 
        [HttpPost]
        public IActionResult Unfollow(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var follow = _context.Follows
                .FirstOrDefault(f => f.FollowerId == myId && f.FollowingId == userId);

            if (follow != null)
            {
                _context.Follows.Remove(follow);
                _context.SaveChanges();
            }

            return RedirectToAction("Index");
        }


        [HttpGet]
        public IActionResult GetFollowers(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var followers = _context.Follows
                .Where(f => f.FollowingId == userId && f.FollowerId != myId) // 🔥 NO TE INCLUYE
                .Select(f => f.Follower)
                .ToList();

            if (!followers.Any())
                return Content("<p style='text-align:center;'>Sin seguidores</p>");

            var html = "";

            foreach (var user in followers)
            {
                var img = !string.IsNullOrEmpty(user.ProfileImage)
                    ? $"<img src='{user.ProfileImage}' style='width:35px;height:35px;border-radius:50%;object-fit:cover;margin-right:8px;' />"
                    : $"<div style='width:35px;height:35px;border-radius:50%;background:#2563eb;color:white;display:flex;align-items:center;justify-content:center;margin-right:8px;'>{user.Username[0]}</div>";

                html += $@"
        <div style='display:flex;align-items:center;gap:8px;padding:8px;border-radius:8px;cursor:pointer;'
             onclick=""window.location='/User/Profile/{user.Id}'"">

            {img}

            <div>
                <div style='font-weight:600'>{user.Username}</div>
                <div style='font-size:12px;color:gray'>{user.FullName ?? ""}</div>
            </div>

        </div>";
            }

            return Content(html, "text/html");
        }

        [HttpGet]
        public IActionResult GetFollowing(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var following = _context.Follows
                .Where(f => f.FollowerId == userId && f.FollowingId != myId) // 🔥 NO TE INCLUYE
                .Select(f => f.Following)
                .ToList();

            if (!following.Any())
                return Content("<p style='text-align:center;'>No sigue a nadie</p>");

            var html = "";

            foreach (var user in following)
            {
                var img = !string.IsNullOrEmpty(user.ProfileImage)
                    ? $"<img src='{user.ProfileImage}' style='width:35px;height:35px;border-radius:50%;object-fit:cover;margin-right:8px;' />"
                    : $"<div style='width:35px;height:35px;border-radius:50%;background:#2563eb;color:white;display:flex;align-items:center;justify-content:center;margin-right:8px;'>{user.Username[0]}</div>";

                html += $@"
        <div style='display:flex;align-items:center;gap:8px;padding:8px;border-radius:8px;cursor:pointer;'
             onclick=""window.location='/User/Profile/{user.Id}'"">

            {img}

            <div>
                <div style='font-weight:600'>{user.Username}</div>
                <div style='font-size:12px;color:gray'>{user.FullName ?? ""}</div>
            </div>

        </div>";
            }

            return Content(html, "text/html");
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
     .Where(u => friendIds.Contains(u.Id) && u.Role != "SuperAdmin")
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




        [HttpGet]
public IActionResult GetOnlineUsers()
{
    var onlineUsers = _onlineUsers.GetOnlineUsers();
    return Json(onlineUsers);
}
    }
}