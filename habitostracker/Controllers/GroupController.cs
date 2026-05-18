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

        [HttpPost]
        public async Task<IActionResult> ToggleAdminOnly(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId && g.IsActive);
            if (group == null) return Json(new { success = false });

            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);

            if (!isAdmin && group.CreatorId != userId)
                return Json(new { success = false, error = "Sin permisos." });

            group.IsAdminOnly = !group.IsAdminOnly;
            await _context.SaveChangesAsync();

            return Json(new { success = true, isAdminOnly = group.IsAdminOnly });
        }

        [HttpGet]
        public IActionResult Index()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var memberships = _context.GroupMembers
    .Where(m => m.UserId == userId)
    .Include(m => m.Group).ThenInclude(g => g.Members)
    .Include(m => m.Group).ThenInclude(g => g.Creator)
    .ToList();

            var inactiveGroupIds = memberships.Where(m => !m.IsActive).Select(m => m.GroupId).ToHashSet();

            var groups = memberships
    .Select(m => m.Group)
    .Where(g => g.IsActive)
    .OrderByDescending(g => g.CreatedAt)
    .ToList();

            // calcular no leídos por grupo
            var unreadByGroup = new Dictionary<int, int>();
            foreach (var g in groups)
            {
                var member = _context.GroupMembers
                    .FirstOrDefault(m => m.GroupId == g.Id && m.UserId == userId);
                if (member == null) continue;

                var unread = _context.GroupMessages
                    .Where(m => m.GroupId == g.Id && !m.IsDeleted
                        && m.SenderId != userId
                        && m.SentAt >= member.JoinedAt)
                    .Include(m => m.Reads)
                    .Count(m => !m.Reads.Any(r => r.UserId == userId));

                unreadByGroup[g.Id] = unread;
            }
            ViewBag.UnreadByGroup = unreadByGroup;
            ViewBag.InactiveGroupIds = inactiveGroupIds;

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

            // para grupos públicos mostrar todos los usuarios, para privados solo amigos
            var type = Request.Query["type"].ToString();
            List<HabitTrackerApp.Models.User> amigos;
            if (type == "public" || type == "channel")
            {
                amigos = _context.Users
                    .Where(u => u.Id != userId && u.Role != "Guest" && u.Role != "SuperAdmin" && u.Role != "System")
                    .OrderBy(u => u.Username)
                    .ToList();
            }
            else
            {
                amigos = _context.Users.Where(u => friendIds.Contains(u.Id)).ToList();
            }
            ViewBag.Amigos = amigos;
            ViewBag.FriendIds = friendIds;
            ViewBag.NombreInicial = nombre ?? "";
            ViewBag.Preselect = preselect ?? 0;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(string name, string? description, List<int> memberIds)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var friendIds = _context.FriendRequests
                .Where(f => (f.SenderId == userId || f.ReceiverId == userId) && f.Status == "Accepted")
                .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
                .ToList();

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "El nombre del grupo es requerido.";
                return RedirectToAction("Create");
            }

            // 🔥 límite de grupos para usuarios no premium
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (currentUser != null && !currentUser.IsPremium)
            {
                var groupCount = _context.GroupMembers.Count(m => m.UserId == userId && m.IsActive);
                if (groupCount >= 10)
                {
                    TempData["Error"] = "🚫 Límite alcanzado. Los usuarios gratuitos pueden estar en máximo 10 grupos. ¡Hazte Premium para grupos ilimitados!";
                    return RedirectToAction("Create");
                }
            }

            var type = Request.Form["type"].ToString();
            if (type != "public" && type != "channel") type = "private";

            // 🔥 para canales no se requiere mínimo un miembro
            var validMembers = memberIds.Distinct().Where(id => id != userId).ToList();
            if (!validMembers.Any() && type != "channel")
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
                IsActive = true,
                Type = type,
                IsPublic = type == "public" || type == "channel",
                InviteCode = Guid.NewGuid().ToString("N").Substring(0, 10)
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
            var invitedViaRequest = new List<int>();

            foreach (var memberId in validMembers)
            {
                var memberUser = _context.Users.FirstOrDefault(u => u.Id == memberId);
                var isFriend = friendIds.Contains(memberId);

                // si tiene perfil privado y no es amigo → solicitud de unión
                if (memberUser != null && memberUser.IsPrivate && !isFriend && type == "public")
                {
                    _context.GroupJoinRequests.Add(new GroupJoinRequest
                    {
                        GroupId = group.Id,
                        UserId = memberId,
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow
                    });
                    invitedViaRequest.Add(memberId);
                }
                else
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
            }
            await _context.SaveChangesAsync();

            // 🔥 notificar a cada miembro añadido
            foreach (var memberId in validMembers)
            {
                if (invitedViaRequest.Contains(memberId))
                {
                    await SendNotification(
                        memberId,
                        $"{creator?.Username} te invitó al grupo \"{group.Name}\" — acepta desde Grupos",
                        $"/Group/Join/{group.InviteCode}",
                        userId
                    );
                }
                else
                {
                    await SendNotification(
                        memberId,
                        $"{creator?.Username} te añadió al grupo \"{group.Name}\"",
                        $"/Group/Chat/{group.Id}",
                        userId
                    );
                }
            }

            // 🔥 mensaje de sistema: grupo creado
            await CreateSystemMessage(group.Id, $"🎉 Grupo creado por {creator?.Username}");

            return RedirectToAction("Chat", new { id = group.Id });
        }

        [HttpGet]
        public IActionResult GetUsersForGroup(string type)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var friendIds = _context.FriendRequests
                .Where(f => (f.SenderId == userId || f.ReceiverId == userId) && f.Status == "Accepted")
                .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
                .ToList();

            List<object> users;

            if (type == "public" || type == "channel")
            {
                users = _context.Users
                    .Where(u => u.Id != userId && u.Role != "Guest"
                        && u.Role != "SuperAdmin" && u.Role != "System")
                    .OrderBy(u => u.Username)
                    .Select(u => (object)new
                    {
                        id = u.Id,
                        username = u.Username,
                        img = u.ProfileImage ?? u.ProfilePicture ?? "",
                        letter = u.Username.Substring(0, 1).ToUpper(),
                        isPrivate = u.IsPrivate,
                        isFriend = friendIds.Contains(u.Id)
                    })
                    .ToList();
            }
            else
            {
                users = _context.Users
                    .Where(u => friendIds.Contains(u.Id))
                    .Select(u => (object)new
                    {
                        id = u.Id,
                        username = u.Username,
                        img = u.ProfileImage ?? u.ProfilePicture ?? "",
                        letter = u.Username.Substring(0, 1).ToUpper(),
                        isPrivate = u.IsPrivate,
                        isFriend = true
                    })
                    .ToList();
            }

            return Json(users);
        }
        [HttpGet]
        public IActionResult DiscoverPartial(string type)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var groups = _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members.Where(m => m.IsActive))
                .Where(g => g.IsActive && g.Type == type)
                .OrderByDescending(g => g.CreatedAt)
                .ToList();

            var myGroupIds = _context.GroupMembers
                .Where(m => m.UserId == userId && m.IsActive)
                .Select(m => m.GroupId).ToList();

            var myRequests = _context.GroupJoinRequests
                .Where(r => r.UserId == userId)
                .ToDictionary(r => r.GroupId, r => r.Status);

            ViewBag.MyGroupIds = myGroupIds;
            ViewBag.MyRequests = myRequests;
            ViewBag.UserId = userId;
            ViewBag.Type = type;
            return PartialView("_DiscoverPartial", groups);
        }
        [HttpPost]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var msg = _context.GroupMessages.FirstOrDefault(m => m.Id == messageId);
            if (msg == null) return Json(new { success = false });

            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == msg.GroupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            var group = _context.Groups.FirstOrDefault(g => g.Id == msg.GroupId);
            bool isOwner = msg.SenderId == userId;
            bool isCreator = group?.CreatorId == userId;

            if (!isOwner && !isAdmin && !isCreator)
                return Json(new { success = false, error = "Sin permisos." });

            msg.IsDeleted = true;
            msg.Content = "🚫 Mensaje eliminado";
            await _context.SaveChangesAsync();

            await _hub.Clients.Group("group-" + msg.GroupId)
                .SendAsync("GroupMessageDeleted", messageId.ToString());

            return Json(new { success = true });
        }
        [HttpGet]
        public IActionResult Chat(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == id && m.UserId == userId);

            var group = _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members.Where(m => m.IsActive)).ThenInclude(m => m.User)
                .FirstOrDefault(g => g.Id == id && g.IsActive);

            if (group == null) return RedirectToAction("Index");
            if (member == null) return RedirectToAction("Index");
            var messages = _context.GroupMessages
                .Where(m => m.GroupId == id && !m.IsDeleted && m.SentAt >= member.JoinedAt)
                    .Include(m => m.Sender)
        .Include(m => m.Reads)
        .Include(m => m.ReplyToMessage).ThenInclude(m => m.Sender)
        .Include(m => m.Reactions)
        .OrderBy(m => m.SentAt)
        .ToList();

            // ✅ calcular no leídos ANTES de marcarlos
            int firstUnreadId = 0;
            int unreadCount = 0;

            if (member.IsActive)
            {
                var unreadMsgs = messages
                    .Where(m => m.SenderId != userId && !m.Reads.Any(r => r.UserId == userId))
                    .ToList();

                unreadCount = unreadMsgs.Count;
                firstUnreadId = unreadMsgs.FirstOrDefault()?.Id ?? 0;

                // ahora sí marcar como leídos
                foreach (var msg in unreadMsgs)
                    _context.GroupMessageReads.Add(new GroupMessageRead
                    {
                        GroupMessageId = msg.Id,
                        UserId = userId,
                        ReadAt = DateTime.UtcNow
                    });
                if (unreadMsgs.Any()) _context.SaveChanges();
            }

            ViewBag.Messages = messages;
            ViewBag.CurrentUserId = userId;
            ViewBag.IsAdmin = member.Role == "Admin" && member.IsActive;
            ViewBag.IsMember = member.IsActive;
            ViewBag.TotalMembers = group.Members.Count(m => m.IsActive);
            ViewBag.IsMuted = member.IsMuted;
            ViewBag.UnreadCount = unreadCount;
            ViewBag.FirstUnreadId = firstUnreadId;

            ViewBag.IsAdminOnly = group.IsAdminOnly;
            ViewBag.CanWrite = (!group.IsAdminOnly) || (member.Role == "Admin") || (group.CreatorId == userId);
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

        // ══ GRUPOS PÚBLICOS Y CANALES ══

        [HttpGet]
        public IActionResult Discover()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var groups = _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members.Where(m => m.IsActive))
                .Where(g => g.IsActive && (g.Type == "public" || g.Type == "channel"))
                .OrderByDescending(g => g.CreatedAt)
                .ToList();

            var myGroupIds = _context.GroupMembers
                .Where(m => m.UserId == userId && m.IsActive)
                .Select(m => m.GroupId)
                .ToList();

            var myRequests = _context.GroupJoinRequests
                .Where(r => r.UserId == userId)
                .Select(r => new { r.GroupId, r.Status })
                .ToList();

            ViewBag.MyGroupIds = myGroupIds;
            ViewBag.MyRequests = myRequests.ToDictionary(r => r.GroupId, r => r.Status);
            ViewBag.UserId = userId;
            return View(groups);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RequestJoin([FromBody] int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId && g.IsActive);
            if (group == null) return Json(new { success = false, error = "Grupo no encontrado." });

            // canal — unirse directo sin aprobación
            if (group.Type == "channel")
            {
                var alreadyMember = _context.GroupMembers
                    .Any(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
                if (alreadyMember) return Json(new { success = false, error = "Ya eres miembro." });

                var exMember = _context.GroupMembers
                    .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId);
                if (exMember != null) { exMember.IsActive = true; exMember.JoinedAt = DateTime.UtcNow; }
                else _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = groupId,
                    UserId = userId,
                    Role = "Member",
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await _context.SaveChangesAsync();
                await CreateSystemMessage(groupId, $"👋 {_context.Users.FirstOrDefault(u => u.Id == userId)?.Username} se unió al canal");
                return Json(new { success = true, joined = true });
            }

            // grupo público — verificar límite
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (currentUser != null && !currentUser.IsPremium)
            {
                var groupCount = _context.GroupMembers.Count(m => m.UserId == userId && m.IsActive);
                if (groupCount >= 10)
                    return Json(new { success = false, error = "Alcanzaste el límite de 10 grupos del plan gratuito. ¡Hazte Premium!" });
            }

            var existingRequest = _context.GroupJoinRequests
                .FirstOrDefault(r => r.GroupId == groupId && r.UserId == userId);
            if (existingRequest != null)
            {
                if (existingRequest.Status == "Pending")
                    return Json(new { success = false, error = "Ya enviaste una solicitud, espera aprobación." });
                if (existingRequest.Status == "Accepted")
                    return Json(new { success = false, error = "Ya eres miembro de este grupo." });
                // si fue rechazada, permitir reintentar
                existingRequest.Status = "Pending";
                existingRequest.CreatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return Json(new { success = true, joined = false });
            }

            _context.GroupJoinRequests.Add(new GroupJoinRequest
            {
                GroupId = groupId,
                UserId = userId,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // notificar al admin/creador
            var admins = _context.GroupMembers
                .Where(m => m.GroupId == groupId && m.IsActive && m.Role == "Admin")
                .Select(m => m.UserId).ToList();

            var requester = _context.Users.FirstOrDefault(u => u.Id == userId);
            foreach (var adminId in admins)
                await SendNotification(adminId,
                    $"📩 {requester?.Username} quiere unirse a \"{group.Name}\"",
                    $"/Group/JoinRequests/{groupId}", userId);

            return Json(new { success = true, joined = false });
        }

        [HttpGet]
        public IActionResult JoinRequests(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == id && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin) return RedirectToAction("Index");

            var group = _context.Groups.FirstOrDefault(g => g.Id == id && g.IsActive);
            if (group == null) return RedirectToAction("Index");

            var requests = _context.GroupJoinRequests
                .Include(r => r.User)
                .Where(r => r.GroupId == id && r.Status == "Pending")
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            ViewBag.Group = group;
            return View(requests);
        }

        [HttpPost]
        public async Task<IActionResult> RespondJoinRequest([FromBody] RespondJoinDto dto)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var request = _context.GroupJoinRequests
                .Include(r => r.User)
                .FirstOrDefault(r => r.Id == dto.RequestId);
            if (request == null) return Json(new { success = false });

            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == request.GroupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin) return Json(new { success = false });

            request.Status = dto.Accept ? "Accepted" : "Rejected";
            await _context.SaveChangesAsync();

            var group = _context.Groups.FirstOrDefault(g => g.Id == request.GroupId);

            if (dto.Accept)
            {
                // verificar límite del solicitante
                var requesterUser = _context.Users.FirstOrDefault(u => u.Id == request.UserId);
                if (requesterUser != null && !requesterUser.IsPremium)
                {
                    var groupCount = _context.GroupMembers.Count(m => m.UserId == request.UserId && m.IsActive);
                    if (groupCount >= 10)
                        return Json(new { success = false, error = $"{requesterUser.Username} alcanzó el límite de grupos." });
                }

                var exMember = _context.GroupMembers
                    .FirstOrDefault(m => m.GroupId == request.GroupId && m.UserId == request.UserId);
                if (exMember != null) { exMember.IsActive = true; exMember.JoinedAt = DateTime.UtcNow; }
                else _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = request.GroupId,
                    UserId = request.UserId,
                    Role = "Member",
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await _context.SaveChangesAsync();

                await CreateSystemMessage(request.GroupId, $"✅ {request.User?.Username} se unió al grupo");
                await SendNotification(request.UserId,
                    $"✅ Tu solicitud para unirte a \"{group?.Name}\" fue aceptada",
                    $"/Group/Chat/{request.GroupId}", userId);
            }
            else
            {
                await SendNotification(request.UserId,
                    $"❌ Tu solicitud para unirte a \"{group?.Name}\" fue rechazada",
                    $"/Group/Index", userId);
            }

            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult Join(string code)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var group = _context.Groups
                .Include(g => g.Members.Where(m => m.IsActive))
                .Include(g => g.Creator)
                .FirstOrDefault(g => g.InviteCode == code && g.IsActive);

            if (group == null)
            {
                TempData["Error"] = "El enlace de invitación no es válido o expiró.";
                return RedirectToAction("Index");
            }

            var isMember = group.Members.Any(m => m.UserId == userId);
            ViewBag.IsMember = isMember;
            ViewBag.UserId = userId;
            return View(group);
        }

        [HttpPost]
        public async Task<IActionResult> JoinViaCode(string code)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var group = _context.Groups
                .Include(g => g.Members)
                .FirstOrDefault(g => g.InviteCode == code && g.IsActive);

            if (group == null)
            {
                TempData["Error"] = "Enlace inválido.";
                return RedirectToAction("Index");
            }

            var alreadyMember = group.Members.Any(m => m.UserId == userId && m.IsActive);
            if (alreadyMember) return RedirectToAction("Chat", new { id = group.Id });

            // canales — entrada directa
            if (group.Type == "channel")
            {
                var exMember = group.Members.FirstOrDefault(m => m.UserId == userId);
                if (exMember != null) { exMember.IsActive = true; exMember.JoinedAt = DateTime.UtcNow; }
                else _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = group.Id,
                    UserId = userId,
                    Role = "Member",
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await _context.SaveChangesAsync();
                var user = _context.Users.FirstOrDefault(u => u.Id == userId);
                await CreateSystemMessage(group.Id, $"👋 {user?.Username} se unió por enlace de invitación");
                return RedirectToAction("Chat", new { id = group.Id });
            }

            // grupos públicos/privados — verificar límite y crear solicitud
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (currentUser != null && !currentUser.IsPremium)
            {
                var groupCount = _context.GroupMembers.Count(m => m.UserId == userId && m.IsActive);
                if (groupCount >= 10)
                {
                    TempData["Error"] = "Alcanzaste el límite de 10 grupos del plan gratuito.";
                    return RedirectToAction("Index");
                }
            }

            var existingRequest = _context.GroupJoinRequests
                .FirstOrDefault(r => r.GroupId == group.Id && r.UserId == userId);
            if (existingRequest == null)
            {
                _context.GroupJoinRequests.Add(new GroupJoinRequest
                {
                    GroupId = group.Id,
                    UserId = userId,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                var admins = _context.GroupMembers
                    .Where(m => m.GroupId == group.Id && m.IsActive && m.Role == "Admin")
                    .Select(m => m.UserId).ToList();
                foreach (var adminId in admins)
                    await SendNotification(adminId,
                        $"📩 {currentUser?.Username} quiere unirse a \"{group.Name}\" (por enlace)",
                        $"/Group/JoinRequests/{group.Id}", userId);
            }

            TempData["Success"] = "Solicitud enviada. Espera a que un admin la apruebe.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> GenerateInviteCode(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin) return Json(new { success = false });

            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return Json(new { success = false });

            if (string.IsNullOrEmpty(group.InviteCode))
            {
                group.InviteCode = Guid.NewGuid().ToString("N").Substring(0, 10);
                await _context.SaveChangesAsync();
            }

            var link = $"{Request.Scheme}://{Request.Host}/Group/Join/{group.InviteCode}";
            return Json(new { success = true, code = group.InviteCode, link });
        }

        [HttpPost]
        public async Task<IActionResult> ResetInviteCode(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == groupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            if (!isAdmin) return Json(new { success = false });

            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return Json(new { success = false });

            group.InviteCode = Guid.NewGuid().ToString("N").Substring(0, 10);
            await _context.SaveChangesAsync();

            var link = $"{Request.Scheme}://{Request.Host}/Group/Join/{group.InviteCode}";
            return Json(new { success = true, link });
        }

        [HttpPost]
        public async Task<IActionResult> ReactToMessage(int messageId, string emoji)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            // si ya reaccionó con ese mismo emoji, quitarlo (toggle)
            var existing = await _context.GroupMessageReactions
                .FirstOrDefaultAsync(r => r.GroupMessageId == messageId && r.UserId == userId && r.Emoji == emoji);

            if (existing != null)
            {
                _context.GroupMessageReactions.Remove(existing);
            }
            else
            {
                // quitar reacción anterior del mismo usuario en ese mensaje
                var prev = await _context.GroupMessageReactions
                    .FirstOrDefaultAsync(r => r.GroupMessageId == messageId && r.UserId == userId);
                if (prev != null) _context.GroupMessageReactions.Remove(prev);

                _context.GroupMessageReactions.Add(new GroupMessageReaction
                {
                    GroupMessageId = messageId,
                    UserId = userId,
                    Emoji = emoji,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            // devolver todas las reacciones del mensaje agrupadas
            var reactions = await _context.GroupMessageReactions
                .Where(r => r.GroupMessageId == messageId)
                .GroupBy(r => r.Emoji)
                .Select(g => new { emoji = g.Key, count = g.Count() })
                .ToListAsync();

            // notificar en tiempo real a todos en el grupo
            var msg = await _context.GroupMessages.FindAsync(messageId);
            if (msg != null)
            {
                await _hub.Clients.Group("group-" + msg.GroupId)
                    .SendAsync("GroupMessageReaction", messageId.ToString(), reactions);
            }

            return Json(new { success = true, reactions });
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(int groupId, string content, int? replyToMessageId = null)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
            if (member == null) return Json(new { success = false });
            if (string.IsNullOrWhiteSpace(content)) return Json(new { success = false });

            // 🔒 Verificar si el grupo es solo para administradores
            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return Json(new { success = false });

            // Verificar canal — solo creador y admins
            if (group.Type == "channel")
            {
                bool isAdminOrCreator = userId == group.CreatorId ||
                    _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId && m.IsActive && m.Role == "Admin");
                if (!isAdminOrCreator)
                    return Json(new { success = false, error = "Solo el creador y administradores pueden escribir en este canal." });
            }

            // Verificar IsAdminOnly para grupos normales
            if (group.IsAdminOnly)
            {
                bool isAdminOrCreator = userId == group.CreatorId ||
                    _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId && m.IsActive && m.Role == "Admin");
                if (!isAdminOrCreator)
                    return Json(new { success = false, error = "Solo administradores pueden escribir aquí." });
            }

            var sender = _context.Users.FirstOrDefault(u => u.Id == userId);
            var msg = new GroupMessage
            {
                GroupId = groupId,
                SenderId = userId,
                Content = content.Trim(),
                SentAt = DateTime.UtcNow,
                IsDeleted = false,
                ReplyToMessageId = replyToMessageId
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

            // obtener info del mensaje al que responde
            string replyContent = "";
            string replySender = "";
            if (replyToMessageId.HasValue)
            {
                var replyMsg = _context.GroupMessages
                    .Include(m => m.Sender)
                    .FirstOrDefault(m => m.Id == replyToMessageId.Value);
                replyContent = replyMsg?.Content ?? "";
                replySender = replyMsg?.Sender?.Username ?? "";
            }

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
                totalMembers,
                replyToMessageId,
                replyContent,
                replySender
            });
        }
        [HttpPost]
        public async Task<IActionResult> SendFile(int groupId, IFormFile file, string? content)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers
                .FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
            if (member == null || file == null) return Json(new { success = false });
            // 🔒 Verificar si el grupo es solo para administradores
            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return Json(new { success = false });

            // Verificar canal — solo creador y admins
            if (group.Type == "channel")
            {
                bool isAdminOrCreator = userId == group.CreatorId ||
                    _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId && m.IsActive && m.Role == "Admin");
                if (!isAdminOrCreator)
                    return Json(new { success = false, error = "Solo el creador y administradores pueden escribir en este canal." });
            }

            // Verificar IsAdminOnly para grupos normales
            if (group.IsAdminOnly)
            {
                bool isAdminOrCreator = userId == group.CreatorId ||
                    _context.GroupMembers.Any(m => m.GroupId == groupId && m.UserId == userId && m.IsActive && m.Role == "Admin");
                if (!isAdminOrCreator)
                    return Json(new { success = false, error = "Solo administradores pueden escribir aquí." });
            }

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

            var msg = new GroupMessage
            {
                GroupId = groupId,
                SenderId = userId,
                Content = content?.Trim() ?? "",
                FileUrl = fileUrl,
                SentAt = DateTime.UtcNow
            };
            _context.GroupMessages.Add(msg);
            await _context.SaveChangesAsync();
            _context.GroupMessageReads.Add(new GroupMessageRead { GroupMessageId = msg.Id, UserId = userId, ReadAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();

            var totalMembers = _context.GroupMembers.Count(m => m.GroupId == groupId && m.IsActive);
            return Json(new { success = true, id = msg.Id, content = msg.Content, sentAt = msg.SentAt.ToLocalTime().ToString("hh:mm tt"), senderName = sender?.Username ?? "Usuario", senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "", fileUrl, fileType, totalMembers });
        }

        [HttpPost]
        public async Task<IActionResult> SendAudio(int groupId, IFormFile audio)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var member = _context.GroupMembers.FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
            if (member == null || audio == null) return Json(new { success = false });

            // 🔒 Verificar si el grupo es solo para administradores (lo añadiremos después)
            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group != null && group.IsAdminOnly)
            {
                bool isAdminOrCreator = _context.GroupMembers
                    .Any(m => m.GroupId == groupId && m.UserId == userId && m.IsActive && (m.Role == "Admin" || m.UserId == group.CreatorId));
                if (!isAdminOrCreator)
                    return Json(new { success = false, error = "Solo administradores pueden enviar audios aquí." });
            }

            var sender = _context.Users.FirstOrDefault(u => u.Id == userId);
            var cloudinary = GetCloudinary();
            using var stream = audio.OpenReadStream();
            var result = await cloudinary.UploadAsync(new RawUploadParams
            {
                File = new FileDescription(audio.FileName, stream),
                Folder = "group_audios",
                PublicId = $"audio_{Guid.NewGuid()}"
            });
            var audioUrl = result.SecureUrl.ToString();

            // ✅ CORREGIDO: Content = "" , FileUrl = audioUrl
            var msg = new GroupMessage
            {
                GroupId = groupId,
                SenderId = userId,
                Content = "",          // ← vacío, no la URL
                FileUrl = audioUrl,    // ← aquí va la URL
                SentAt = DateTime.UtcNow
            };
            _context.GroupMessages.Add(msg);
            await _context.SaveChangesAsync();
            _context.GroupMessageReads.Add(new GroupMessageRead { GroupMessageId = msg.Id, UserId = userId, ReadAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();

            var totalMembers = _context.GroupMembers.Count(m => m.GroupId == groupId && m.IsActive);
            return Json(new
            {
                success = true,
                id = msg.Id,
                audioUrl = audioUrl,
                sentAt = msg.SentAt.ToLocalTime().ToString("hh:mm tt"),
                senderName = sender?.Username ?? "Usuario",
                senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "",
                totalMembers
            });
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
        public IActionResult GetUnreadCount(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var count = _context.GroupMessages
                .Where(m => m.GroupId == groupId && !m.IsDeleted && m.SenderId != userId)
                .Include(m => m.Reads)
                .Count(m => !m.Reads.Any(r => r.UserId == userId));
            return Json(new { count });
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

            return RedirectToAction("Chat", new { id = groupId });
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
            // 🔥 límite de grupos para el nuevo usuario si no es premium
            var newUserObj = await _context.Users.FirstOrDefaultAsync(u => u.Id == newUserId);
            if (newUserObj != null && !newUserObj.IsPremium)
            {
                var groupCount = _context.GroupMembers.Count(m => m.UserId == newUserId && m.IsActive);
                if (groupCount >= 10)
                    return Json(new { success = false, message = $"{newUserObj.Username} ya está en el máximo de grupos permitidos (10). Necesita Premium para más." });
            }

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
        [HttpGet]
        public IActionResult GetMyGroups()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var groups = _context.GroupMembers
                .Where(m => m.UserId == userId && m.IsActive)
                .Include(m => m.Group).ThenInclude(g => g.Members)
                .Select(m => new {
                    id = m.Group.Id,
                    name = m.Group.Name,
                    image = m.Group.ImageUrl ?? "",
                    memberCount = m.Group.Members.Count(mb => mb.IsActive)
                })
                .ToList();
            return Json(groups);
        }

        [HttpPost]
        public async Task<IActionResult> ForwardMessage(int messageId, int targetGroupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var originalMsg = _context.GroupMessages
                .FirstOrDefault(m => m.Id == messageId && !m.IsDeleted);
            if (originalMsg == null) return Json(new { success = false });

            var isMember = _context.GroupMembers
                .Any(m => m.GroupId == targetGroupId && m.UserId == userId && m.IsActive);
            if (!isMember) return Json(new { success = false, error = "No eres miembro de ese grupo." });

            var sender = _context.Users.FirstOrDefault(u => u.Id == userId);
            var forwardedContent = string.IsNullOrEmpty(originalMsg.Content)
                ? "" : $"↪️ {originalMsg.Content}";

            var newMsg = new GroupMessage
            {
                GroupId = targetGroupId,
                SenderId = userId,
                Content = forwardedContent,
                FileUrl = originalMsg.FileUrl,
                SentAt = DateTime.UtcNow,
                IsDeleted = false
            };
            _context.GroupMessages.Add(newMsg);
            await _context.SaveChangesAsync();

            _context.GroupMessageReads.Add(new GroupMessageRead
            {
                GroupMessageId = newMsg.Id,
                UserId = userId,
                ReadAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            var totalMembers = _context.GroupMembers.Count(m => m.GroupId == targetGroupId && m.IsActive);

            await _hub.Clients.Group("group-" + targetGroupId)
                .SendAsync("ReceiveGroupMessage",
                    userId.ToString(),
                    sender?.Username ?? "Usuario",
                    sender?.ProfileImage ?? sender?.ProfilePicture ?? "",
                    forwardedContent,
                    newMsg.SentAt.ToLocalTime().ToString("hh:mm tt"),
                    newMsg.Id.ToString(),
                    originalMsg.FileUrl ?? "",
                    string.IsNullOrEmpty(originalMsg.FileUrl) ? "text" : "image",
                    "");

            return Json(new { success = true });
        }
        [HttpPost]
        public async Task<IActionResult> StarMessage(int messageId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var msg = _context.GroupMessages.FirstOrDefault(m => m.Id == messageId && !m.IsDeleted);
            if (msg == null) return Json(new { success = false });

            // verificar que sea miembro del grupo
            var isMember = _context.GroupMembers
                .Any(m => m.GroupId == msg.GroupId && m.UserId == userId && m.IsActive);
            if (!isMember) return Json(new { success = false });

            var existing = _context.StarredMessages
                .FirstOrDefault(s => s.MessageId == messageId && s.UserId == userId);

            if (existing != null)
            {
                _context.StarredMessages.Remove(existing);
                await _context.SaveChangesAsync();
                return Json(new { success = true, starred = false });
            }

            _context.StarredMessages.Add(new StarredMessage
            {
                MessageId = messageId,
                UserId = userId,
                StarredAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return Json(new { success = true, starred = true });
        }

        [HttpGet]
        public IActionResult GetStarredMessages(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var starred = _context.StarredMessages
                .Where(s => s.UserId == userId && s.Message.GroupId == groupId && !s.Message.IsDeleted)
                .Include(s => s.Message).ThenInclude(m => m.Sender)
                .OrderByDescending(s => s.StarredAt)
                .Select(s => new {
                    id = s.Message.Id,
                    content = s.Message.Content ?? "📎 Archivo",
                    sender = s.Message.Sender.Username,
                    sentAt = s.Message.SentAt,
                    fileUrl = s.Message.FileUrl ?? ""
                })
                .ToList();
            return Json(starred);
        }

        [HttpPost]
        public async Task<IActionResult> PinMessage(int messageId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var msg = _context.GroupMessages.FirstOrDefault(m => m.Id == messageId && !m.IsDeleted);
            if (msg == null) return Json(new { success = false });

            var isAdmin = _context.GroupMembers
                .Any(m => m.GroupId == msg.GroupId && m.UserId == userId && m.Role == "Admin" && m.IsActive);
            var group = _context.Groups.FirstOrDefault(g => g.Id == msg.GroupId);
            if (!isAdmin && group?.CreatorId != userId)
                return Json(new { success = false, error = "Solo admins pueden fijar mensajes." });

            // toggle — si ya está fijado, desfijar
            if (group.PinnedMessageId == messageId)
            {
                group.PinnedMessageId = null;
                await _context.SaveChangesAsync();
                await _hub.Clients.Group("group-" + msg.GroupId)
                    .SendAsync("GroupMessageUnpinned");
                return Json(new { success = true, pinned = false });
            }

            group.PinnedMessageId = messageId;
            await _context.SaveChangesAsync();

            var sender = _context.Users.FirstOrDefault(u => u.Id == userId);
            await _hub.Clients.Group("group-" + msg.GroupId)
                .SendAsync("GroupMessagePinned", messageId.ToString(), msg.Content ?? "📎 Archivo", sender?.Username ?? "");

            return Json(new { success = true, pinned = true, content = msg.Content ?? "📎 Archivo" });
        }
        [HttpPost]
        public async Task<IActionResult> EditMessage(int messageId, string newContent)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var msg = _context.GroupMessages.FirstOrDefault(m => m.Id == messageId);
            if (msg == null) return Json(new { success = false });
            if (msg.SenderId != userId) return Json(new { success = false, error = "Solo puedes editar tus mensajes." });
            if (string.IsNullOrWhiteSpace(newContent)) return Json(new { success = false });

            msg.Content = newContent.Trim();
            msg.IsEdited = true;
            await _context.SaveChangesAsync();

            await _hub.Clients.Group("group-" + msg.GroupId)
                .SendAsync("GroupMessageEdited", messageId.ToString(), msg.Content);

            return Json(new { success = true, content = msg.Content });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleGroupType(int groupId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var group = await _context.Groups
                .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive);
            if (group == null)
                return Json(new { success = false, error = "Grupo no encontrado" });

            if (group.CreatorId != userId)
                return Json(new { success = false, error = "Solo el creador puede cambiar la visibilidad del grupo." });

            group.Type = group.Type == "public" ? "private" : "public";
            group.IsPublic = group.Type == "public";
            await _context.SaveChangesAsync();

            await CreateSystemMessage(groupId, group.Type == "public"
                ? "🌍 El grupo ahora es publico"
                : "🔒 El grupo ahora es privado");

            return Json(new { success = true, newType = group.Type, isPublic = group.IsPublic });
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

            var group = _context.Groups.FirstOrDefault(g => g.Id == groupId);
            bool isPublicOrChannel = group?.Type == "public" || group?.Type == "channel";

            List<object> available;

            if (isPublicOrChannel)
            {
                available = _context.Users
                    .Where(u => !currentMemberIds.Contains(u.Id)
                        && u.Role != "Guest"
                        && u.Role != "System"
                        && u.Role != "SuperAdmin")
                    .Select(u => new { u.Id, u.Username, img = u.ProfileImage ?? u.ProfilePicture ?? "" })
                    .Cast<object>()
                    .ToList();
            }
            else
            {
                var friendIds = _context.FriendRequests
                    .Where(f => (f.SenderId == userId || f.ReceiverId == userId) && f.Status == "Accepted")
                    .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
                    .ToList();

                available = _context.Users
                    .Where(u => friendIds.Contains(u.Id) && !currentMemberIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Username, img = u.ProfileImage ?? u.ProfilePicture ?? "" })
                    .Cast<object>()
                    .ToList();
            }

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

    public class RespondJoinDto
    {
        public int RequestId { get; set; }
        public bool Accept { get; set; }
    }



}
