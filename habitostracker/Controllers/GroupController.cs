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

            _context.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                UserId = userId,
                Role = "Admin",
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            });

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
                .Include(m => m.Reads)
                .OrderBy(m => m.SentAt)
                .ToList();

            // 🔥 marcar mensajes como leídos
            var unreadIds = messages
                .Where(m => m.SenderId != userId && !m.Reads.Any(r => r.UserId == userId))
                .Select(m => m.Id)
                .ToList();

            foreach (var msgId in unreadIds)
            {
                _context.GroupMessageReads.Add(new GroupMessageRead
                {
                    GroupMessageId = msgId,
                    UserId = userId,
                    ReadAt = DateTime.UtcNow
                });
            }
            if (unreadIds.Any()) _context.SaveChanges();

            var totalMembers = group.Members.Count(m => m.IsActive);

            ViewBag.Messages = messages;
            ViewBag.CurrentUserId = userId;
            ViewBag.IsAdmin = member.Role == "Admin";
            ViewBag.TotalMembers = totalMembers;

            return View(group);
        }

        // 🔥 detalles del grupo
        [HttpGet]
        public IActionResult Details(int id)
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

            ViewBag.IsAdmin = member.Role == "Admin";
            ViewBag.CurrentUserId = userId;
            return View(group);
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(int groupId, string content)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);

            if (member == null) return Json(new { success = false });
            if (string.IsNullOrWhiteSpace(content)) return Json(new { success = false });

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

            // 🔥 marcar como leído por el sender
            _context.GroupMessageReads.Add(new GroupMessageRead
            {
                GroupMessageId = msg.Id,
                UserId = userId,
                ReadAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            var totalMembers = _context.GroupMembers
                .Count(m => m.GroupId == groupId && m.IsActive);

            return Json(new
            {
                success = true,
                id = msg.Id,
                content = msg.Content,
                sentAt = msg.SentAt.ToLocalTime().ToString("hh:mm tt"),
                senderName = sender?.Username ?? "Usuario",
                senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "",
                totalMembers
            });
        }

        // 🔥 marcar mensajes como leídos
        [HttpPost]
        public async Task<IActionResult> MarkRead(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var messages = _context.GroupMessages
                .Where(m => m.GroupId == groupId && !m.IsDeleted && m.SenderId != userId)
                .Include(m => m.Reads)
                .ToList();

            foreach (var msg in messages)
            {
                if (!msg.Reads.Any(r => r.UserId == userId))
                {
                    _context.GroupMessageReads.Add(new GroupMessageRead
                    {
                        GroupMessageId = msg.Id,
                        UserId = userId,
                        ReadAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // 🔥 obtener estado de lectura de un mensaje
        [HttpGet]
        public IActionResult GetMessageReads(int messageId)
        {
            var reads = _context.GroupMessageReads
                .Where(r => r.GroupMessageId == messageId)
                .Include(r => r.User)
                .Select(r => new { r.UserId, r.User.Username, r.ReadAt })
                .ToList();

            return Json(reads);
        }

        // 🔥 actualizar nombre del grupo (solo admin)
        [HttpPost]
        public async Task<IActionResult> UpdateName(int groupId, string name)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);

            if (!isAdmin || string.IsNullOrWhiteSpace(name))
                return Json(new { success = false });

            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return Json(new { success = false });

            group.Name = name.Trim();
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // 🔥 reportar grupo
        [HttpPost]
        public async Task<IActionResult> Report([FromBody] ReportGroupDto dto)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var yaReporto = _context.GroupReports
                .Any(r => r.GroupId == dto.GroupId && r.ReporterId == userId);

            if (yaReporto)
                return Json(new { success = false, message = "Ya reportaste este grupo." });

            _context.GroupReports.Add(new GroupReport
            {
                GroupId = dto.GroupId,
                ReporterId = userId,
                Reason = dto.Reason,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            return Json(new { success = true });
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
        public async Task<IActionResult> RemoveMember(int groupId, int memberId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);

            if (!isAdmin)
                return Json(new { success = false });

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == memberId && m.IsActive);

            if (member != null)
            {
                member.IsActive = false;
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var group = _context.Groups
                .FirstOrDefault(g => g.Id == groupId && g.CreatorId == userId);

            if (group == null) return RedirectToAction("Index");

            group.IsActive = false;
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }
    }

    public class ReportGroupDto
    {
        public int GroupId { get; set; }
        public string Reason { get; set; }
    }
}