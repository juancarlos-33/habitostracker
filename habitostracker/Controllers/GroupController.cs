using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class GroupController : Controller
    {
        private readonly HabitDbContext _context;

        public GroupController(HabitDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var groups = _context.GroupMembers
                .Where(m => m.UserId == userId && m.IsActive)
                .Include(m => m.Group)
                    .ThenInclude(g => g.Members)
                .Include(m => m.Group)
                    .ThenInclude(g => g.Creator)
                .Select(m => m.Group)
                .Where(g => g.IsActive)
                .OrderByDescending(g => g.CreatedAt)
                .ToList();

            return View(groups);
        }

        [HttpGet]
        public IActionResult Create(string? nombre, int? preselect)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var friendIds = _context.FriendRequests
                .Where(f => (f.SenderId == userId || f.ReceiverId == userId) && f.Status == "Accepted")
                .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
                .ToList();

            var amigos = _context.Users
                .Where(u => friendIds.Contains(u.Id))
                .ToList();

            ViewBag.Amigos = amigos;
            ViewBag.NombreInicial = nombre ?? "";
            ViewBag.Preselect = preselect ?? 0;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(string name, string? description, List<int> memberIds)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "El nombre del grupo es requerido.";
                return RedirectToAction("Create");
            }

            var group = new Group
            {
                Name = name.Trim(),
                Description = description?.Trim(),
                CreatorId = userId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            // 🔥 agregar creador como Admin
            _context.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                UserId = userId,
                Role = "Admin",
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            });

            // 🔥 agregar miembros seleccionados
            foreach (var memberId in memberIds.Distinct())
            {
                if (memberId == userId) continue;
                _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = group.Id,
                    UserId = memberId,
                    Role = "Member",
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Chat", new { id = group.Id });
        }

        [HttpGet]
        public IActionResult Chat(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == id && m.UserId == userId && m.IsActive);

            if (member == null)
                return RedirectToAction("Index");

            var group = _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members.Where(m => m.IsActive))
                    .ThenInclude(m => m.User)
                .FirstOrDefault(g => g.Id == id && g.IsActive);

            if (group == null)
                return RedirectToAction("Index");

            var messages = _context.GroupMessages
                .Where(m => m.GroupId == id && !m.IsDeleted)
                .Include(m => m.Sender)
                .OrderBy(m => m.SentAt)
                .ToList();

            ViewBag.Messages = messages;
            ViewBag.CurrentUserId = userId;
            ViewBag.IsAdmin = member.Role == "Admin";

            return View(group);
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(int groupId, string content)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);

            if (member == null)
                return Json(new { success = false });

            if (string.IsNullOrWhiteSpace(content))
                return Json(new { success = false });

            var sender = _context.Users.FirstOrDefault(u => u.Id == userId);

            var msg = new GroupMessage
            {
                GroupId = groupId,
                SenderId = userId,
                Content = content.Trim(),
                SentAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.GroupMessages.Add(msg);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                id = msg.Id,
                content = msg.Content,
                sentAt = msg.SentAt.ToLocalTime().ToString("HH:mm"),
                senderName = sender?.Username ?? "Usuario",
                senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? ""
            });
        }

        [HttpPost]
        public async Task<IActionResult> Leave(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);

            if (member != null)
            {
                member.IsActive = false;
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> AddMember(int groupId, int newUserId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);

            if (!isAdmin)
                return Json(new { success = false, message = "No tienes permisos." });

            var yaEsMiembro = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == newUserId && m.IsActive);

            if (yaEsMiembro)
                return Json(new { success = false, message = "Ya es miembro del grupo." });

            _context.GroupMembers.Add(new GroupMember
            {
                GroupId = groupId,
                UserId = newUserId,
                Role = "Member",
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            });

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var group = _context.Groups
                .FirstOrDefault(g => g.Id == groupId && g.CreatorId == userId);

            if (group == null)
                return RedirectToAction("Index");

            group.IsActive = false;
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }
}