using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.SignalR;
using HabitTrackerApp.Hubs;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class GroupController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IConfiguration _config;
        private readonly IHubContext<ChatHub> _hub;


        public GroupController(HabitDbContext context, IConfiguration config, IHubContext<ChatHub> hub)
        {
            _context = context;
            _config = config;
            _hub = hub;
        }

        private Cloudinary GetCloudinary()
        {
            var account = new Account(
                _config["Cloudinary:CloudName"],
                _config["Cloudinary:ApiKey"],
                _config["Cloudinary:ApiSecret"]);
            return new Cloudinary(account);
        }

        // 🔥 helper para crear mensaje de sistema
        private async Task<GroupMessage> CreateSystemMessage(int groupId, string content)
        {
            var msg = new GroupMessage
            {
                GroupId = groupId,
                SenderId = null,
                Content = content,
                SentAt = DateTime.UtcNow,
                IsDeleted = false,
                IsSystem = true
            };
            _context.GroupMessages.Add(msg);
            await _context.SaveChangesAsync();

            // notificar via SignalR
            await _hub.Clients.Group("group-" + groupId)
                .SendAsync("ReceiveGroupMessage", "0", "Sistema", "", content,
                    DateTime.Now.ToString("hh:mm tt"), msg.Id.ToString(), "", "system");

            return msg;
        }

        // 🔥 helper para enviar notificación a un usuario
        private async Task SendNotification(int toUserId, string message, string link, int fromUserId)
        {
            var fromUser = _context.Users.FirstOrDefault(u => u.Id == fromUserId);
            var notif = new Notification
            {
                UserId = toUserId,
                Message = message,
                Link = link,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                FromUserId = fromUserId,
                FromUserImage = fromUser?.ProfileImage ?? fromUser?.ProfilePicture ?? "",
                FromUsername = fromUser?.Username ?? ""
            };
            _context.Notifications.Add(notif);
            await _context.SaveChangesAsync();

            await _hub.Clients.Group(toUserId.ToString())
     .SendAsync("ReceiveNotification",
         toUserId.ToString(),
         message,
         fromUser?.Username ?? "",
         fromUser?.ProfileImage ?? fromUser?.ProfilePicture ?? "",
         link);
        }

        [HttpGet]
        public IActionResult Index()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var groups = _context.GroupMembers
                .Where(m => m.UserId == userId && m.IsActive)
                .Include(m => m.Group).ThenInclude(g => g.Members)
                .Include(m => m.Group).ThenInclude(g => g.Creator)
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
            var amigos = _context.Users.Where(u => friendIds.Contains(u.Id)).ToList();
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

            // 🔥 mínimo un miembro además del creador
            var validMembers = memberIds.Distinct().Where(id => id != userId).ToList();
            if (!validMembers.Any())
            {
                TempData["Error"] = "Debes agregar al menos un miembro al grupo.";
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

            var creator = _context.Users.FirstOrDefault(u => u.Id == userId);

            foreach (var memberId in validMembers)
            {
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

            // 🔥 notificar a cada miembro añadido
            foreach (var memberId in validMembers)
            {
                await SendNotification(
                    memberId,
                    $"{creator?.Username} te añadió al grupo \"{group.Name}\"",
                    $"/Group/Chat/{group.Id}",
                    userId
                );
            }

            // 🔥 mensaje de sistema: grupo creado
            await CreateSystemMessage(group.Id, $"🎉 Grupo creado por {creator?.Username}");

            return RedirectToAction("Chat", new { id = group.Id });
        }

        [HttpGet]
        public IActionResult Chat(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            // 🔥 permitir ver el chat aunque no sea miembro activo (para ver historial)
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == id && m.UserId == userId);

            var group = _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members.Where(m => m.IsActive)).ThenInclude(m => m.User)
                .FirstOrDefault(g => g.Id == id && g.IsActive);

            if (group == null) return RedirectToAction("Index");
            if (member == null) return RedirectToAction("Index");

            var messages = _context.GroupMessages
                .Where(m => m.GroupId == id && !m.IsDeleted)
                .Include(m => m.Sender)
                .Include(m => m.Reads)
                .OrderBy(m => m.SentAt)
                .ToList();

            // solo marcar leídos si es miembro activo
            if (member.IsActive)
            {
                var unreadIds = messages
                    .Where(m => m.SenderId != userId && !m.Reads.Any(r => r.UserId == userId))
                    .Select(m => m.Id).ToList();

                foreach (var msgId in unreadIds)
                    _context.GroupMessageReads.Add(new GroupMessageRead
                    {
                        GroupMessageId = msgId,
                        UserId = userId,
                        ReadAt = DateTime.UtcNow
                    });
                if (unreadIds.Any()) _context.SaveChanges();
            }

            ViewBag.Messages = messages;
            ViewBag.CurrentUserId = userId;
            ViewBag.IsAdmin = member.Role == "Admin" && member.IsActive;
            ViewBag.IsMember = member.IsActive;
            ViewBag.TotalMembers = group.Members.Count(m => m.IsActive);
            ViewBag.IsMuted = member.IsMuted;
            return View(group);
        }

        [HttpGet]
        public IActionResult Details(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == id && m.UserId == userId && m.IsActive);
            if (member == null) return RedirectToAction("Index");

            var group = _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members.Where(m => m.IsActive)).ThenInclude(m => m.User)
                .FirstOrDefault(g => g.Id == id && g.IsActive);
            if (group == null) return RedirectToAction("Index");

            ViewBag.IsAdmin = member.Role == "Admin";
            ViewBag.CurrentUserId = userId;
            ViewBag.IsMuted = member.IsMuted;
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

            _context.GroupMessageReads.Add(new GroupMessageRead
            {
                GroupMessageId = msg.Id,
                UserId = userId,
                ReadAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            var totalMembers = _context.GroupMembers.Count(m => m.GroupId == groupId && m.IsActive);
            return Json(new
            {
                success = true,
                id = msg.Id,
                content = msg.Content,
                sentAt = msg.SentAt.ToLocalTime().ToString("hh:mm tt"),
                senderName = sender?.Username ?? "Usuario",
                senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "",
                fileUrl = "",
                fileType = "text",
                totalMembers
            });
        }
        [HttpPost]
        public async Task<IActionResult> SendFile(int groupId, IFormFile file)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
            if (member == null || file == null) return Json(new { success = false });

            var sender = _context.Users.FirstOrDefault(u => u.Id == userId);
            var cloudinary = GetCloudinary();
            string fileUrl = ""; string fileType = "image";

            using var stream = file.OpenReadStream();
            if (file.ContentType.StartsWith("video"))
            {
                fileType = "video";
                var r = await cloudinary.UploadAsync(new VideoUploadParams { File = new FileDescription(file.FileName, stream), Folder = "group_videos" });
                fileUrl = r.SecureUrl.ToString();
            }
            else
            {
                var r = await cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(file.FileName, stream), Folder = "group_images" });
                fileUrl = r.SecureUrl.ToString();
            }

            var msg = new GroupMessage { GroupId = groupId, SenderId = userId, Content = "", FileUrl = fileUrl, SentAt = DateTime.UtcNow };
            _context.GroupMessages.Add(msg);
            await _context.SaveChangesAsync();
            _context.GroupMessageReads.Add(new GroupMessageRead { GroupMessageId = msg.Id, UserId = userId, ReadAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();

            var totalMembers = _context.GroupMembers.Count(m => m.GroupId == groupId && m.IsActive);
            return Json(new { success = true, id = msg.Id, content = "", sentAt = msg.SentAt.ToLocalTime().ToString("hh:mm tt"), senderName = sender?.Username ?? "Usuario", senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "", fileUrl, fileType, totalMembers });
        }

        [HttpPost]
        public async Task<IActionResult> SendAudio(int groupId, IFormFile audio)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
            if (member == null || audio == null) return Json(new { success = false });

            var sender = _context.Users.FirstOrDefault(u => u.Id == userId);
            var cloudinary = GetCloudinary();
            using var stream = audio.OpenReadStream();
            var result = await cloudinary.UploadAsync(new RawUploadParams { File = new FileDescription(audio.FileName, stream), Folder = "group_audios", PublicId = $"audio_{Guid.NewGuid()}" });
            var audioUrl = result.SecureUrl.ToString();

            var msg = new GroupMessage { GroupId = groupId, SenderId = userId, Content = audioUrl, SentAt = DateTime.UtcNow };
            _context.GroupMessages.Add(msg);
            await _context.SaveChangesAsync();
            _context.GroupMessageReads.Add(new GroupMessageRead { GroupMessageId = msg.Id, UserId = userId, ReadAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();

            var totalMembers = _context.GroupMembers.Count(m => m.GroupId == groupId && m.IsActive);
            return Json(new { success = true, id = msg.Id, audioUrl, sentAt = msg.SentAt.ToLocalTime().ToString("hh:mm tt"), senderName = sender?.Username ?? "Usuario", senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "", totalMembers });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateImage(int groupId, IFormFile image)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin || image == null) return Json(new { success = false });

            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return Json(new { success = false });

            var cloudinary = GetCloudinary();
            using var stream = image.OpenReadStream();
            var result = await cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(image.FileName, stream), Folder = "group_images", Transformation = new Transformation().Width(300).Height(300).Crop("fill") });
            group.ImageUrl = result.SecureUrl.ToString();
            await _context.SaveChangesAsync();

            return Json(new { success = true, imageUrl = group.ImageUrl });
        }

        [HttpPost]
        public async Task<IActionResult> MarkRead(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var messages = _context.GroupMessages
                .Where(m => m.GroupId == groupId && !m.IsDeleted && m.SenderId != userId)
                .Include(m => m.Reads).ToList();

            var added = new List<int>();
            foreach (var msg in messages)
            {
                if (!msg.Reads.Any(r => r.UserId == userId))
                {
                    _context.GroupMessageReads.Add(new GroupMessageRead { GroupMessageId = msg.Id, UserId = userId, ReadAt = DateTime.UtcNow });
                    added.Add(msg.Id);
                }
            }
            await _context.SaveChangesAsync();
            return Json(new { success = true, readIds = added });
        }

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

        [HttpPost]
        public async Task<IActionResult> UpdateName(int groupId, string name)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin || string.IsNullOrWhiteSpace(name)) return Json(new { success = false });

            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return Json(new { success = false });
            group.Name = name.Trim();
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // 🔥 silenciar/activar notificaciones
        [HttpPost]
        public async Task<IActionResult> ToggleMute(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
            if (member == null) return Json(new { success = false });

            member.IsMuted = !member.IsMuted;
            await _context.SaveChangesAsync();
            return Json(new { success = true, isMuted = member.IsMuted });
        }

        [HttpPost]
        public async Task<IActionResult> Report([FromBody] ReportGroupDto dto)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var yaReporto = _context.GroupReports.Any(r => r.GroupId == dto.GroupId && r.ReporterId == userId);
            if (yaReporto) return Json(new { success = false, message = "Ya reportaste este grupo." });

            _context.GroupReports.Add(new GroupReport { GroupId = dto.GroupId, ReporterId = userId, Reason = dto.Reason, CreatedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // 🔥 salir del grupo — queda el historial, sale el aviso
        [HttpPost]
        public async Task<IActionResult> Leave(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
            if (member == null) return RedirectToAction("Index");

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            member.IsActive = false;
            member.LeftAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // 🔥 mensaje de sistema
            await CreateSystemMessage(groupId, $"👋 {user?.Username} salió del grupo");

            return RedirectToAction("Index");
        }

        // 🔥 agregar miembro con notificación
        [HttpPost]
        public async Task<IActionResult> AddMember(int groupId, int newUserId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin) return Json(new { success = false, message = "No tienes permisos." });

            var yaEsMiembro = _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == newUserId && m.IsActive);
            if (yaEsMiembro) return Json(new { success = false, message = "Ya es miembro." });

            var exMember = _context.GroupMembers.FirstOrDefault(m => m.GroupId == groupId && m.UserId == newUserId);
            if (exMember != null)
            {
                exMember.IsActive = true;
                exMember.LeftAt = null;
                exMember.JoinedAt = DateTime.UtcNow;
            }
            else
            {
                _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = groupId,
                    UserId = newUserId,
                    Role = "Member",
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }
            await _context.SaveChangesAsync();

            var adder = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            var newUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == newUserId);
            var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId);

            // ✅ verificar que no sean null antes de usarlos
            var adderName = adder?.Username ?? "Alguien";
            var newUserName = newUser?.Username ?? "Usuario";
            var groupName = group?.Name ?? "el grupo";

            // mensaje de sistema en el chat
            await CreateSystemMessage(groupId, $"➕ {adderName} añadió a {newUserName}");

            // ✅ notificación al nuevo miembro con datos correctos
            await SendNotification(
                newUserId,
                $"➕ {adderName} te añadió al grupo \"{groupName}\"",
                $"/Group/Chat/{groupId}",
                userId
            );

            // ✅ notificar por SignalR al nuevo miembro para que vea
            // el mensaje de sistema en tiempo real si está conectado
            await _hub.Clients.Group(newUserId.ToString())
                .SendAsync("AddedToGroup", groupId.ToString(), groupName, adderName);

            return Json(new { success = true });
        }
        // 🔥 eliminar miembro (solo admin) con aviso
        [HttpPost]
        public async Task<IActionResult> RemoveMember(int groupId, int memberId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin) return Json(new { success = false });

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == memberId && m.IsActive);
            if (member == null) return Json(new { success = false });

            var admin = _context.Users.FirstOrDefault(u => u.Id == userId);
            var removedUser = _context.Users.FirstOrDefault(u => u.Id == memberId);

            member.IsActive = false;
            member.LeftAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var sysMsg = $"🚫 {admin?.Username} eliminó a {removedUser?.Username} del grupo";
            await CreateSystemMessage(groupId, sysMsg);

            // 🔥 notificar a todos en el chat (mensaje de sistema)
            await _hub.Clients.Group("group-" + groupId)
                .SendAsync("MemberRemovedFromGroup", memberId.ToString(), sysMsg);

            // 🔥 notificar DIRECTAMENTE al usuario eliminado por su canal personal
            var personalMsg = $"🚫 {admin?.Username} te eliminó del grupo";
            await _hub.Clients.Group(memberId.ToString())
                .SendAsync("YouWereRemovedFromGroup", groupId.ToString(), personalMsg);

            return Json(new { success = true });
        }
        // 🔥 promover a admin (solo el creador del grupo)
        [HttpPost]
        public async Task<IActionResult> PromoteToAdmin(int groupId, int memberId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId && g.IsActive);
            if (group == null) return Json(new { success = false });

            // solo el creador puede dar admin
            if (group.CreatorId != userId) return Json(new { success = false, message = "Solo el creador puede dar admin." });

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == memberId && m.IsActive);
            if (member == null) return Json(new { success = false });

            member.Role = "Admin";
            await _context.SaveChangesAsync();

            var creator = _context.Users.FirstOrDefault(u => u.Id == userId);
            var promoted = _context.Users.FirstOrDefault(u => u.Id == memberId);

            await CreateSystemMessage(groupId, $"⭐ {creator?.Username} nombró admin a {promoted?.Username}");
            await SendNotification(memberId, $"Ahora eres admin del grupo \"{group.Name}\"", $"/Group/Chat/{groupId}", userId);

            return Json(new { success = true });
        }

        // 🔥 quitar admin (solo el creador del grupo)
        [HttpPost]
        public async Task<IActionResult> DemoteAdmin(int groupId, int memberId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId && g.IsActive);
            if (group == null) return Json(new { success = false });

            // solo el creador puede quitar admin
            if (group.CreatorId != userId) return Json(new { success = false, message = "Solo el creador puede quitar admin." });

            // no puede quitarse a sí mismo
            if (memberId == userId) return Json(new { success = false, message = "No puedes quitarte el admin a ti mismo." });

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == memberId && m.IsActive);
            if (member == null) return Json(new { success = false });

            member.Role = "Member";
            await _context.SaveChangesAsync();

            var creator = _context.Users.FirstOrDefault(u => u.Id == userId);
            var demoted = _context.Users.FirstOrDefault(u => u.Id == memberId);

            await CreateSystemMessage(groupId, $"⬇️ {creator?.Username} quitó el admin a {demoted?.Username}");

            return Json(new { success = true });
        }

        // 🔥 buscar amigos que no están en el grupo para añadir
        [HttpGet]
        public IActionResult GetFriendsToAdd(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin) return Json(new { success = false });

            var currentMemberIds = _context.GroupMembers
                .Where(m => m.GroupId == groupId && m.IsActive)
                .Select(m => m.UserId).ToList();

            var friendIds = _context.FriendRequests
                .Where(f => (f.SenderId == userId || f.ReceiverId == userId) && f.Status == "Accepted")
                .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
                .ToList();

            var available = _context.Users
                .Where(u => friendIds.Contains(u.Id) && !currentMemberIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, img = u.ProfileImage ?? u.ProfilePicture ?? "" })
                .ToList();

            return Json(available);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId && g.CreatorId == userId);
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