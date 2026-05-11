using HabitTrackerApp.Data;
using HabitTrackerApp.Hubs;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class UserController : Controller
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly HabitDbContext _context;
        private readonly OnlineUsersService _onlineUsers;

        private readonly CloudinaryService _cloudinaryService;

        public UserController(HabitDbContext context, IHubContext<ChatHub> hubContext, OnlineUsersService onlineUsers, CloudinaryService cloudinaryService)
        {
            _context = context;
            _hubContext = hubContext;
            _onlineUsers = onlineUsers;
            _cloudinaryService = cloudinaryService;
        }
        [HttpGet]
        public IActionResult GetFriendsForMention()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            // ✅ Ahora obtenemos TODOS los usuarios activos,
            //    excepto el propio usuario, y excluyendo roles especiales.
            var allUsers = _context.Users
                .Where(u => u.Id != myId
                            && u.IsActive == true
                            && u.Role != "System"
                            && u.Role != "Guest")
                .OrderBy(u => u.Username) // Orden alfabético para mejor experiencia
                .Select(u => new
                {
                    id = u.Id,
                    username = u.Username,
                    profileImage = u.ProfileImage ?? u.ProfilePicture
                })
                .ToList();

            return Json(allUsers);
        }
        public IActionResult Index()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var users = _context.Users
                .Where(u => u.Role != "SuperAdmin" && u.Role != "Guest" && u.Role != "System" && u.IsActive)
                .OrderByDescending(u => u.Role == "Admin")
                .ThenBy(u => u.Username)
                .ToList()
                .DistinctBy(u => u.Id)
                .ToList();

            var sentRequests = _context.FriendRequests
                .Where(f => f.SenderId == myId && f.Status == "Pending")
                .Select(f => f.ReceiverId)
                .ToList();

            var friendIds = _context.FriendRequests
                .Where(f => (f.SenderId == myId || f.ReceiverId == myId) && f.Status == "Accepted")
                .Select(f => f.SenderId == myId ? f.ReceiverId : f.SenderId)
                .ToList();

            var validUserIds = _context.Users.Select(u => u.Id).ToHashSet();
            var friends = friendIds.Where(id => validUserIds.Contains(id)).Distinct().ToList();

            ViewBag.SentRequests = sentRequests;
            ViewBag.Friends = friends;

            return View(users);
        }
        public IActionResult Profile(int id)
        {
            if (id == 0) return View("UserDeleted");
            var user = _context.Users.FirstOrDefault(u => u.Id == id);
            if (user == null) return View("UserDeleted");
            if (user.Role == "SuperAdmin") return RedirectToAction("Index");

            var myId = int.Parse(User.FindFirst("UserId").Value);

            ViewBag.Followers = _context.Follows.Count(f => f.FollowingId == id);
            ViewBag.Following = _context.Follows.Count(f => f.FollowerId == id);

            // ========== CARGAR PUBLICACIONES DEL USUARIO ==========
            var userPosts = _context.Posts
                .Include(p => p.User)
                .Where(p => p.UserId == user.Id)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();
            ViewBag.UserPosts = userPosts;

            var userPostIds = userPosts.Select(p => p.Id).ToList();

            // ========== ESTADÍSTICAS DE PUBLICACIONES ==========
            var likesCount = _context.PostLikes
                .Where(l => userPostIds.Contains(l.PostId))
                .GroupBy(l => l.PostId)
                .ToDictionary(g => g.Key, g => g.Count());

            var commentCountsQuery = _context.PostComments
                .Where(c => userPostIds.Contains(c.PostId))
                .Select(c => new { c.PostId, c.Id })
                .ToList();

            var replyCounts = _context.CommentReplies
                .Where(r => commentCountsQuery.Select(c => c.Id).Contains(r.CommentId))
                .GroupBy(r => r.CommentId)
                .ToDictionary(g => g.Key, g => g.Count());

            var commentCounts = commentCountsQuery
                .GroupBy(c => c.PostId)
                .ToDictionary(g => g.Key, g => g.Count() + g.Sum(c => replyCounts.ContainsKey(c.Id) ? replyCounts[c.Id] : 0));

            var myLikes = _context.PostLikes
                .Where(l => l.UserId == myId && userPostIds.Contains(l.PostId))
                .Select(l => l.PostId)
                .ToHashSet();

            // ========== REPOSTS ==========
            var reposts = _context.Reposts
                .Include(r => r.Post).ThenInclude(p => p.User)
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            ViewBag.Reposts = reposts;

            var repostPostIds = reposts.Where(r => r.Post != null).Select(r => r.PostId).ToList();

            // Likes de posts reposteados
            var repostLikes = _context.PostLikes
                .Where(l => repostPostIds.Contains(l.PostId))
                .GroupBy(l => l.PostId)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var kv in repostLikes)
                if (!likesCount.ContainsKey(kv.Key))
                    likesCount[kv.Key] = kv.Value;

            // Comentarios de posts reposteados
            var repostCommentQuery = _context.PostComments
                .Where(c => repostPostIds.Contains(c.PostId))
                .Select(c => new { c.PostId, c.Id })
                .ToList();

            var repostReplyCounts = _context.CommentReplies
                .Where(r => repostCommentQuery.Select(c => c.Id).Contains(r.CommentId))
                .GroupBy(r => r.CommentId)
                .ToDictionary(g => g.Key, g => g.Count());

            var repostCommentCounts = repostCommentQuery
                .GroupBy(c => c.PostId)
                .ToDictionary(g => g.Key, g => g.Count() + g.Sum(c => repostReplyCounts.ContainsKey(c.Id) ? repostReplyCounts[c.Id] : 0));

            foreach (var kv in repostCommentCounts)
                if (!commentCounts.ContainsKey(kv.Key))
                    commentCounts[kv.Key] = kv.Value;

            // Mis likes en posts reposteados
            var myLikesOnReposts = _context.PostLikes
                .Where(l => l.UserId == myId && repostPostIds.Contains(l.PostId))
                .Select(l => l.PostId)
                .ToHashSet();

            foreach (var repostId in myLikesOnReposts)
                myLikes.Add(repostId);

            ViewBag.PostLikes = likesCount;
            ViewBag.CommentCounts = commentCounts;
            ViewBag.MyLikes = myLikes;

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> UploadPayment(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Debes subir un comprobante";
                return RedirectToAction("Pay");
            }

            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return NotFound();

            // Subir a Cloudinary
            string imageUrl;
            try
            {
                imageUrl = await _cloudinaryService.UploadImageAsync(file, "payments");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al subir el comprobante: " + ex.Message;
                return RedirectToAction("Pay");
            }

            var payment = new Payment
            {
                UserId = userId,
                Screenshot = imageUrl,
                CreatedAt = DateTime.Now,
                IsApproved = false,
                IsRejected = false
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Comprobante enviado. Espera aprobación del admin 😎";
            return RedirectToAction("Index", "Habit");
        }
        [HttpPost]
        public async Task<IActionResult> MakePremium(IFormFile screenshot)
        {
            if (screenshot == null || screenshot.Length == 0)
            {
                TempData["Error"] = "Debes subir un comprobante";
                return RedirectToAction("Pay");
            }

            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return NotFound();

            // Subir a Cloudinary
            string imageUrl;
            try
            {
                imageUrl = await _cloudinaryService.UploadImageAsync(screenshot, "payments");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al subir el comprobante: " + ex.Message;
                return RedirectToAction("Pay");
            }

            var payment = new Payment
            {
                UserId = userId,
                Screenshot = imageUrl,
                CreatedAt = DateTime.Now,
                IsApproved = false,
                IsRejected = false
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Comprobante enviado. Espera aprobación 😈";
            return RedirectToAction("Index", "Habit");
        }
        public IActionResult Pay() => View();

        [HttpPost]
        public async Task<IActionResult> SendFriendRequest(int receiverId)
        {
            var senderId = int.Parse(User.FindFirst("UserId").Value);
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == senderId);

            if (currentUser.Role == "Guest")
            {
                TempData["Error"] = "✨ Debes crear una cuenta para enviar solicitudes de amistad.";
                return RedirectToAction("Profile", new { id = receiverId });
            }

            var receiver = _context.Users.FirstOrDefault(u => u.Id == receiverId);
            if (receiver != null && receiver.Role == "SuperAdmin") return RedirectToAction("Index");
            if (senderId == receiverId) return RedirectToAction("Profile", new { id = receiverId });

            var exists = _context.FriendRequests.Any(r => r.SenderId == senderId && r.ReceiverId == receiverId);
            if (exists) return RedirectToAction("Profile", new { id = receiverId });

            var username = User.Identity.Name;
            var request = new FriendRequest { SenderId = senderId, ReceiverId = receiverId, Status = "Pending" };
            _context.FriendRequests.Add(request);

            var sender = _context.Users.FirstOrDefault(u => u.Id == senderId);
            _context.Notifications.Add(new Notification
            {
                UserId = receiverId,
                FromUserId = senderId,
                FromUsername = username,
                FromUserImage = sender?.ProfileImage ?? "",
                Message = username + " te envió una solicitud de amistad",
                Link = "/User/FriendRequests",
                IsRead = false,
                CreatedAt = DateTime.Now
            });

            await _hubContext.Clients.Group(receiverId.ToString())
                .SendAsync("ReceiveNotification", senderId, username + " te envió una solicitud de amistad",
                    username, sender?.ProfileImage ?? "", "/User/FriendRequests");

            _context.SaveChanges();
            return RedirectToAction("Profile", new { id = receiverId });
        }

        public IActionResult FriendRequests()
        {
            var claim = User.FindFirst("UserId");
            if (claim == null) return RedirectToAction("Login", "Account");

            var userId = int.Parse(claim.Value);
            var requests = _context.FriendRequests
                .Where(r => r.ReceiverId == userId && r.Status == "Pending")
                .Select(r => new FriendRequestViewModel
                {
                    Id = r.Id,
                    SenderId = r.SenderId,
                    SenderUsername = _context.Users.Where(u => u.Id == r.SenderId).Select(u => u.Username).FirstOrDefault(),
                    ProfileImage = _context.Users.Where(u => u.Id == r.SenderId).Select(u => u.ProfileImage).FirstOrDefault()
                })
                .ToList();

            return View(requests);
        }

        [HttpPost]
        public IActionResult AcceptFriendRequest(int requestId)
        {
            var request = _context.FriendRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null) return NotFound();
            request.Status = "Accepted";
            _context.SaveChanges();
            return RedirectToAction("FriendRequests");
        }

        [HttpPost]
        public IActionResult RejectFriendRequest(int requestId)
        {
            var request = _context.FriendRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null) return NotFound();
            request.Status = "Rejected";
            _context.SaveChanges();
            return RedirectToAction("FriendRequests");
        }

        public IActionResult Friends()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var friends = _context.FriendRequests
                .Where(r => (r.SenderId == userId || r.ReceiverId == userId) && r.Status == "Accepted")
                .ToList();
            return View(friends);
        }

        [HttpPost]
        public async Task<IActionResult> Follow(int userId)
        {
            var currentUserId = int.Parse(User.FindFirst("UserId").Value);
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == currentUserId);

            if (currentUser.Role == "Guest")
            {
                TempData["Error"] = "✨ Debes crear una cuenta para seguir usuarios.";
                return RedirectToAction("Profile", new { id = userId });
            }

            var targetUser = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (targetUser != null && targetUser.Role == "SuperAdmin") return RedirectToAction("Index");
            if (currentUserId == userId) return RedirectToAction("Index");

            var alreadyFollowing = _context.Follows.FirstOrDefault(f => f.FollowerId == currentUserId && f.FollowingId == userId);

            if (alreadyFollowing == null)
            {
                _context.Follows.Add(new Follow { FollowerId = currentUserId, FollowingId = userId, CreatedAt = DateTime.Now });

                var sender = _context.Users.FirstOrDefault(u => u.Id == currentUserId);
                _context.Notifications.Add(new Notification
                {
                    UserId = userId,
                    FromUserId = currentUserId,
                    FromUsername = sender?.Username ?? "",
                    FromUserImage = sender?.ProfileImage ?? "",
                    Message = sender?.Username + " empezó a seguirte",
                    Link = "/User/Profile/" + currentUserId,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });

                _context.SaveChanges();

                await _hubContext.Clients.Group(userId.ToString())
                    .SendAsync("ReceiveNotification", currentUserId, sender?.Username + " empezó a seguirte",
                        sender?.Username ?? "", sender?.ProfileImage ?? "", "/User/Profile/" + currentUserId);
            }

            return RedirectToAction("Profile", new { id = userId });
        }

        [HttpPost]
        public IActionResult Unfollow(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var follow = _context.Follows.FirstOrDefault(f => f.FollowerId == myId && f.FollowingId == userId);
            if (follow != null) { _context.Follows.Remove(follow); _context.SaveChanges(); }
            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult GetFollowers(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var followers = _context.Follows.Where(f => f.FollowingId == userId && f.FollowerId != myId).Select(f => f.Follower).ToList();
            if (!followers.Any()) return Content("<p style='text-align:center;'>Sin seguidores</p>");

            var html = "";
            foreach (var user in followers)
            {
                var img = !string.IsNullOrEmpty(user.ProfileImage)
                    ? $"<img src='{user.ProfileImage}' style='width:35px;height:35px;border-radius:50%;object-fit:cover;margin-right:8px;' />"
                    : $"<div style='width:35px;height:35px;border-radius:50%;background:#2563eb;color:white;display:flex;align-items:center;justify-content:center;margin-right:8px;'>{user.Username[0]}</div>";
                html += $@"<div style='display:flex;align-items:center;gap:8px;padding:8px;border-radius:8px;cursor:pointer;' onclick=""window.location='/User/Profile/{user.Id}'"">
                    {img}<div><div style='font-weight:600'>{user.Username}</div><div style='font-size:12px;color:gray'>{user.FullName ?? ""}</div></div></div>";
            }
            return Content(html, "text/html");
        }

        [HttpGet]
        public IActionResult GetFollowing(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var following = _context.Follows.Where(f => f.FollowerId == userId && f.FollowingId != myId).Select(f => f.Following).ToList();
            if (!following.Any()) return Content("<p style='text-align:center;'>No sigue a nadie</p>");

            var html = "";
            foreach (var user in following)
            {
                var img = !string.IsNullOrEmpty(user.ProfileImage)
                    ? $"<img src='{user.ProfileImage}' style='width:35px;height:35px;border-radius:50%;object-fit:cover;margin-right:8px;' />"
                    : $"<div style='width:35px;height:35px;border-radius:50%;background:#2563eb;color:white;display:flex;align-items:center;justify-content:center;margin-right:8px;'>{user.Username[0]}</div>";
                html += $@"<div style='display:flex;align-items:center;gap:8px;padding:8px;border-radius:8px;cursor:pointer;' onclick=""window.location='/User/Profile/{user.Id}'"">
                    {img}<div><div style='font-weight:600'>{user.Username}</div><div style='font-size:12px;color:gray'>{user.FullName ?? ""}</div></div></div>";
            }
            return Content(html, "text/html");
        }

        public IActionResult Ranking()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var friendRequests = _context.FriendRequests
                .Where(r => (r.SenderId == userId || r.ReceiverId == userId) && r.Status == "Accepted").ToList();
            var friendIds = friendRequests.Select(r => r.SenderId == userId ? r.ReceiverId : r.SenderId).ToList();
            friendIds.Add(userId);

            var ranking = _context.Users
                .Where(u => friendIds.Contains(u.Id) && u.Role != "SuperAdmin")
                .Select(u => new
                {
                    u.Username,
                    Streak = _context.Habits.Where(h => h.UserId == u.Id).Select(h => h.StreakDays).DefaultIfEmpty(0).Max()
                })
                .OrderByDescending(x => x.Streak)
                .ToList();

            return View(ranking);
        }

        // =====================================
        // 🚫 BLOQUEAR USUARIO
        // =====================================
        [HttpPost]
        public async Task<IActionResult> BlockUser([FromBody] BlockDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            if (myId == dto.BlockedId) return Json(new { success = false });

            var already = _context.Blocks.Any(b => b.BlockerId == myId && b.BlockedId == dto.BlockedId);
            if (!already)
            {
                _context.Blocks.Add(new Block { BlockerId = myId, BlockedId = dto.BlockedId, CreatedAt = DateTime.UtcNow });
                await _context.SaveChangesAsync();
            }

            // 🔥 notificar al bloqueado en tiempo real para deshabilitar su chat
            await _hubContext.Clients.Group(dto.BlockedId.ToString())
                .SendAsync("UserBlocked", myId.ToString());

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UnblockUser([FromBody] BlockDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var block = _context.Blocks.FirstOrDefault(b => b.BlockerId == myId && b.BlockedId == dto.BlockedId);
            if (block != null)
            {
                _context.Blocks.Remove(block);
                await _context.SaveChangesAsync();
            }

            // 🔥 notificar al desbloqueado para recargar
            await _hubContext.Clients.Group(dto.BlockedId.ToString())
                .SendAsync("UserUnblocked", myId.ToString());

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ReportUser([FromBody] ReportDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            if (myId == dto.ReportedId) return Json(new { success = false });

            var count = _context.Reports.Count(r => r.ReporterId == myId && r.ReportedId == dto.ReportedId);
            if (count >= 3) return Json(new { success = false, error = "Ya reportaste a este usuario" });

            var reporter = _context.Users.FirstOrDefault(u => u.Id == myId);
            var reported = _context.Users.FirstOrDefault(u => u.Id == dto.ReportedId);

            _context.Reports.Add(new Report
            {
                ReporterId = myId,
                ReportedId = dto.ReportedId,
                Reason = dto.Reason ?? "Sin motivo",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // 🔥 notificar a SuperAdmin y Admins del nuevo reporte
            var admins = _context.Users
                .Where(u => u.Role == "SuperAdmin" || u.Role == "Admin")
                .ToList();

            var reportMsg = $"🚩 {reporter?.Username} reportó a {reported?.Username}: \"{dto.Reason}\"";

            foreach (var admin in admins)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = admin.Id,
                    FromUserId = myId,
                    FromUsername = reporter?.Username ?? "",
                    FromUserImage = reporter?.ProfileImage ?? "",
                    Message = reportMsg,
                    Link = "/Admin/Reports",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });

                await _hubContext.Clients.Group(admin.Id.ToString())
                    .SendAsync("ReceiveNotification", myId, reportMsg,
                        reporter?.Username ?? "", reporter?.ProfileImage ?? "", "/Admin/Reports");
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult IsBlocked(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var blocked = _context.Blocks.Any(b => b.BlockerId == myId && b.BlockedId == userId);
            return Json(new { blocked });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFriend([FromBody] BlockDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var friendship = _context.FriendRequests
                .FirstOrDefault(f => ((f.SenderId == myId && f.ReceiverId == dto.BlockedId) ||
                                       (f.SenderId == dto.BlockedId && f.ReceiverId == myId)) &&
                                      f.Status == "Accepted");

            if (friendship != null)
            {
                _context.FriendRequests.Remove(friendship);
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult GetOnlineUsers()
        {
            var onlineUsers = _onlineUsers.GetOnlineUsers();
            return Json(onlineUsers);
        }

        public class BlockDto { public int BlockedId { get; set; } }
        public class ReportDto { public int ReportedId { get; set; } public string Reason { get; set; } }
    }
}