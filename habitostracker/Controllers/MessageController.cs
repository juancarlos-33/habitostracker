using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApp.Hubs;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using HabitTrackerApp.Services;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class MessageController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly CloudinaryService _cloudinaryService;
        private readonly OnlineUsersService _onlineUsers;

        public MessageController(HabitDbContext context, IHubContext<ChatHub> hubContext, CloudinaryService cloudinaryService, OnlineUsersService onlineUsers)
        {
            _context = context;
            _hubContext = hubContext;
            _cloudinaryService = cloudinaryService;
            _onlineUsers = onlineUsers;
        }

        public IActionResult Inbox()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var conversations = _context.Messages
                .Where(m => m.SenderId == myId || m.ReceiverId == myId)
                .OrderByDescending(m => m.SentAt)
                .ToList()
                .GroupBy(m => m.SenderId == myId ? m.ReceiverId : m.SenderId)
                .Select(g => g.First())
                .ToList();

            foreach (var msg in conversations)
            {
                msg.Sender = _context.Users.FirstOrDefault(u => u.Id == msg.SenderId);
                msg.Receiver = _context.Users.FirstOrDefault(u => u.Id == msg.ReceiverId);
            }

            var me = _context.Users.FirstOrDefault(u => u.Id == myId);
            ViewBag.MyUser = me;

            // 🔥 cargar solicitudes con Sender incluido
            var pendingRequests = _context.MessageRequests
                .Where(r => r.ReceiverId == myId && r.Status == "Pending")
                .ToList();

            foreach (var req in pendingRequests)
            {
                req.Sender = _context.Users.FirstOrDefault(u => u.Id == req.SenderId);
            }

            pendingRequests = pendingRequests.OrderByDescending(r => r.CreatedAt).ToList();
            ViewBag.PendingRequests = pendingRequests;

            return View(conversations);
        }

        [HttpGet]
        public IActionResult GetPendingRequestCount()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return Json(new { count = 0 });

            var myId = int.Parse(userIdClaim.Value);
            var count = _context.MessageRequests
                .Count(r => r.ReceiverId == myId && r.Status == "Pending");
            return Json(new { count });
        }


        [HttpGet]
        public IActionResult GetUnreadMessageCount()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return Json(new { count = 0 });
            var myId = int.Parse(userIdClaim.Value);
            var count = _context.Messages
                .Count(m => m.ReceiverId == myId && !m.IsRead && !m.DeletedByReceiver);
            return Json(new { count });
        }
        public async Task<IActionResult> Chat(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            // 🔥 verificar si puede chatear
            var canChat = CanChat(myId, userId);
            if (!canChat)
            {
                TempData["Error"] = "No puedes chatear con esta persona todavía.";
                return RedirectToAction("Inbox");
            }

            var unreadMessages = _context.Messages
                .Where(m => m.SenderId == userId && m.ReceiverId == myId && !m.IsRead)
                .ToList();

            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
                await _hubContext.Clients.Group(msg.SenderId.ToString()).SendAsync("MessageSeen", msg.Id);
            }

            _context.SaveChanges();
            await _hubContext.Clients.Group(userId.ToString()).SendAsync("ForceSeenUpdate");

            var messages = _context.Messages
                .Where(m => (m.SenderId == myId && m.ReceiverId == userId) ||
                            (m.SenderId == userId && m.ReceiverId == myId))
                .Include(m => m.Sender)
                .OrderBy(m => m.SentAt)
                .ToList();

            ViewBag.OtherUserId = userId;

            var otherUser = _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.Username, u.ProfileImage, u.ProfilePicture, u.LastOnline })
                .FirstOrDefault();

            ViewBag.OtherUsername = otherUser?.Username ?? "Usuario";
            ViewBag.OtherLastOnline = otherUser?.LastOnline;
            ViewBag.OtherUserProfileImage = otherUser?.ProfileImage ?? otherUser?.ProfilePicture;
            ViewBag.ReceiverIsOnline = _onlineUsers.IsOnline(userId.ToString());

            // 🔥 estado de solicitud
            var request = _context.MessageRequests
                .FirstOrDefault(r => (r.SenderId == myId && r.ReceiverId == userId) ||
                                     (r.SenderId == userId && r.ReceiverId == myId));
            ViewBag.MessageRequestStatus = request?.Status ?? "None";
            ViewBag.IsFriend = IsFriend(myId, userId);
            var otherUserRole = _context.Users.Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefault();
            ViewBag.IsSystemChat = otherUserRole == "System";

            return View(messages);
        }

        // 🔥 verificar si pueden chatear libremente
        private bool CanChat(int myId, int otherId)
        {
            // amigos siempre pueden chatear
            if (IsFriend(myId, otherId)) return true;

            // si ya tienen mensajes previos (conversación existente)
            var hasMessages = _context.Messages
                .Any(m => (m.SenderId == myId && m.ReceiverId == otherId) ||
                           (m.SenderId == otherId && m.ReceiverId == myId));
            if (hasMessages) return true;

            // si ya hay solicitud aceptada
            var accepted = _context.MessageRequests
                .Any(r => ((r.SenderId == myId && r.ReceiverId == otherId) ||
                           (r.SenderId == otherId && r.ReceiverId == myId)) &&
                          r.Status == "Accepted");
            return accepted;
        }
        private bool IsFriend(int myId, int otherId)
        {
            return _context.FriendRequests.Any(f =>
                ((f.SenderId == myId && f.ReceiverId == otherId) ||
                 (f.SenderId == otherId && f.ReceiverId == myId)) &&
                f.Status == "Accepted");
        }

        // 🔥 enviar solicitud de mensaje
        [HttpPost]
        public async Task<IActionResult> SendMessageRequest([FromBody] SendMessageRequestDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var me = _context.Users.FirstOrDefault(u => u.Id == myId);

            if (GetCurrentUser().Role == "Guest")
                return Json(new { success = false, error = "Los invitados no pueden enviar mensajes." });

            // ya son amigos — pueden chatear directo
            if (IsFriend(myId, dto.ReceiverId))
                return Json(new { success = false, error = "Ya son amigos, puedes chatear directo." });

            // ya existe solicitud
            var existing = _context.MessageRequests
                .FirstOrDefault(r => r.SenderId == myId && r.ReceiverId == dto.ReceiverId);

            if (existing != null)
            {
                if (existing.Status == "Pending")
                    return Json(new { success = false, error = "Ya enviaste una solicitud, espera a que la acepten." });
                if (existing.Status == "Accepted")
                    return Json(new { success = false, error = "Ya puedes chatear con esta persona." });
                if (existing.Status == "Rejected")
                    return Json(new { success = false, error = "Esta persona rechazó tu solicitud." });
            }

            if (string.IsNullOrWhiteSpace(dto.Message) || dto.Message.Length > 200)
                return Json(new { success = false, error = "El mensaje debe tener entre 1 y 200 caracteres." });

            var request = new MessageRequest
            {
                SenderId = myId,
                ReceiverId = dto.ReceiverId,
                FirstMessage = dto.Message.Trim(),
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.MessageRequests.Add(request);

            // notificación al receptor
            _context.Notifications.Add(new Notification
            {
                UserId = dto.ReceiverId,
                FromUserId = myId,
                Message = $"💬 {me?.Username} quiere enviarte un mensaje",
                CreatedAt = DateTime.UtcNow,
                IsRead = false,
                FromUserImage = me?.ProfileImage ?? me?.ProfilePicture ?? "",
                FromUsername = me?.Username ?? ""
            });

            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(dto.ReceiverId.ToString())
                .SendAsync("ReceiveNotification", myId,
                    $"💬 {me?.Username} quiere enviarte un mensaje",
                    me?.Username, me?.ProfileImage ?? "", "/Message/Inbox");

            return Json(new { success = true });
        }

        // 🔥 responder solicitud de mensaje
        [HttpPost]
        public async Task<IActionResult> RespondMessageRequest([FromBody] RespondRequestDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var request = _context.MessageRequests
                .Include(r => r.Sender)
                .FirstOrDefault(r => r.Id == dto.RequestId && r.ReceiverId == myId);

            if (request == null) return Json(new { success = false });

            request.Status = dto.Accept ? "Accepted" : "Rejected";
            await _context.SaveChangesAsync();

            var me = _context.Users.FirstOrDefault(u => u.Id == myId);

            if (dto.Accept)
            {
                // notificar al que envió la solicitud
                _context.Notifications.Add(new Notification
                {
                    UserId = request.SenderId,
                    FromUserId = myId,
                    Message = $"✅ {me?.Username} aceptó tu solicitud de mensaje",
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false,
                    FromUserImage = me?.ProfileImage ?? me?.ProfilePicture ?? "",
                    FromUsername = me?.Username ?? ""
                });
                await _context.SaveChangesAsync();

                await _hubContext.Clients.Group(request.SenderId.ToString())
                    .SendAsync("ReceiveNotification", myId,
                        $"✅ {me?.Username} aceptó tu solicitud de mensaje",
                        me?.Username, me?.ProfileImage ?? "", $"/Message/Chat?userId={myId}");
            }

            return Json(new { success = true, accepted = dto.Accept });
        }

        // 🔥 verificar estado de solicitud
        [HttpGet]
        public IActionResult GetRequestStatus(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var request = _context.MessageRequests
                .FirstOrDefault(r => (r.SenderId == myId && r.ReceiverId == userId) ||
                                     (r.SenderId == userId && r.ReceiverId == myId));

            var isFriend = IsFriend(myId, userId);
            return Json(new
            {
                status = request?.Status ?? "None",
                isFriend,
                canChat = CanChat(myId, userId)
            });
        }

        [HttpPost]
        public async Task<IActionResult> Send(int receiverId, string content, IFormFile file)
        {
            var currentUser = GetCurrentUser();
            if (currentUser.Role == "Guest")
                return Json(new { success = false, error = "Debes crear una cuenta para enviar mensajes." });

            var senderId = int.Parse(User.FindFirst("UserId").Value);

            // 🔥 verificar que pueden chatear
            if (!CanChat(senderId, receiverId))
                return Json(new { success = false, error = "Primero debes enviar una solicitud de mensaje." });
            var receiverUser2 = _context.Users.FirstOrDefault(u => u.Id == receiverId);
            if (receiverUser2?.Role == "System")
                return Json(new { success = false, error = "Este es un mensaje automático del sistema. No puedes responder." });

            var senderName = User.Identity?.Name ?? "Usuario";
            var receiverExists = _context.Users.Any(u => u.Id == receiverId);
            if (!receiverExists)
                return Json(new { success = false, error = "El usuario ya no existe." });

            string filePath = null;
            if (file != null && file.Length > 0)
            {
                var ext = Path.GetExtension(file.FileName).ToLower();
                var videoExts = new[] { ".mp4", ".webm", ".mov", ".avi" };
                filePath = videoExts.Contains(ext)
                    ? await _cloudinaryService.UploadVideoAsync(file)
                    : await _cloudinaryService.UploadImageAsync(file, "habitostracker/messages");
            }

            if (string.IsNullOrWhiteSpace(content) && file == null)
                return Json(new { success = false });

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = content ?? "",
                SentAt = DateTime.Now,
                IsRead = false,
                FileUrl = filePath
            };
            _context.Messages.Add(message);

            var sender = _context.Users.FirstOrDefault(u => u.Id == senderId);
            var notifMsg = !string.IsNullOrEmpty(filePath)
                ? (filePath.Contains("/video/upload/") ? "📹 Video" : "📷 Foto")
                : $"💬 {content}";

            _context.Notifications.Add(new Notification
            {
                UserId = receiverId,
                FromUserId = senderId,
                Message = notifMsg,
                CreatedAt = DateTime.Now,
                IsRead = false,
                FromUserImage = sender?.ProfileImage ?? "",
                FromUsername = senderName
            });

            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(receiverId.ToString())
                .SendAsync("ReceiveMessage", senderId, receiverId, senderName, content ?? "", filePath ?? "");

            var receiverOnline = _onlineUsers.IsOnline(receiverId.ToString());
            var receiverInChat = _onlineUsers.IsInChatWith(receiverId.ToString(), senderId.ToString());
            var receiverUser = _context.Users.FirstOrDefault(u => u.Id == receiverId);
            var isMuted = receiverUser?.MutedUntil != null && receiverUser.MutedUntil > DateTime.UtcNow;

            if (!receiverInChat && !isMuted)
            {
                await _hubContext.Clients.Group(receiverId.ToString())
                    .SendAsync("ReceiveNotification", senderId, notifMsg, senderName,
                        sender?.ProfileImage ?? "", "/Message/Chat?userId=" + senderId);
            }

            if (receiverOnline)
            {
                await _hubContext.Clients.Group(senderId.ToString())
                    .SendAsync("MessageSentConfirm", message.Id, filePath ?? "");
            }

            return Json(new { success = true, messageId = message.Id, filePath = filePath ?? "" });
        }

        private User GetCurrentUser()
        {
            var username = User.Identity.Name;
            return _context.Users.FirstOrDefault(u => u.Username == username);
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsSeen(int senderId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var messages = _context.Messages
                .Where(m => m.SenderId == senderId && m.ReceiverId == myId && !m.IsRead)
                .ToList();

            foreach (var msg in messages)
            {
                msg.IsRead = true;
                await _hubContext.Clients.Group(senderId.ToString()).SendAsync("MessageSeen", msg.Id);
            }
            _context.SaveChanges();
            return Ok();
        }

        public async Task<IActionResult> Call(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var me = await _context.Users.FindAsync(myId);
            var other = await _context.Users.FindAsync(userId);
            if (other == null) return NotFound();

            ViewBag.MyId = myId;
            ViewBag.MyUsername = me?.Username;
            ViewBag.MyImage = me?.ProfileImage ?? me?.ProfilePicture;
            ViewBag.OtherUserId = userId;
            ViewBag.OtherUsername = other.Username;
            ViewBag.OtherImage = other.ProfileImage ?? other.ProfilePicture;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int messageId, string scope)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var msg = _context.Messages.FirstOrDefault(m => m.Id == messageId);
            if (msg == null) return NotFound();

            if (scope == "all" && msg.SenderId == myId)
            {
                _context.Messages.Remove(msg);
                await _context.SaveChangesAsync();
                await _hubContext.Clients.Group(msg.ReceiverId.ToString())
                    .SendAsync("MessageDeleted", messageId);
            }
            else
            {
                if (msg.SenderId == myId) msg.DeletedBySender = true;
                else msg.DeletedByReceiver = true;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> SendAudio(IFormFile audio)
        {
            var senderId = int.Parse(User.FindFirst("UserId").Value);
            if (audio == null || audio.Length == 0) return BadRequest();

            string audioUrl;
            try { audioUrl = await _cloudinaryService.UploadVideoAsync(audio); }
            catch
            {
                var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/audios");
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
                var fileName = Guid.NewGuid().ToString() + ".webm";
                var filePath = Path.Combine(folderPath, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                    await audio.CopyToAsync(stream);
                audioUrl = "/audios/" + fileName;
            }

            var receiverId = int.Parse(Request.Form["receiverId"]);
            var senderName = User.Identity?.Name ?? "Usuario";
            var sender = _context.Users.FirstOrDefault(u => u.Id == senderId);

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = audioUrl,
                SentAt = DateTime.Now,
                IsRead = false
            };
            _context.Messages.Add(message);

            _context.Notifications.Add(new Notification
            {
                UserId = receiverId,
                FromUserId = senderId,
                Message = "🎤 Mensaje de voz",
                CreatedAt = DateTime.Now,
                IsRead = false,
                FromUserImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "",
                FromUsername = senderName
            });

            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(receiverId.ToString()).SendAsync("ReceiveAudio", senderId, audioUrl);

            var receiverOnline = _onlineUsers.IsOnline(receiverId.ToString());
            var receiverInChat = _onlineUsers.IsInChatWith(receiverId.ToString(), senderId.ToString());
            var receiverUser = _context.Users.FirstOrDefault(u => u.Id == receiverId);
            var isMuted = receiverUser?.MutedUntil != null && receiverUser.MutedUntil > DateTime.UtcNow;

            if (!receiverInChat && !isMuted)
                await _hubContext.Clients.Group(receiverId.ToString())
                    .SendAsync("ReceiveNotification", senderId, "🎤 Mensaje de voz", senderName,
                        sender?.ProfileImage ?? sender?.ProfilePicture ?? "", "/Message/Chat?userId=" + senderId);

            if (receiverOnline)
                await _hubContext.Clients.Group(senderId.ToString())
                    .SendAsync("MessageSentConfirm", message.Id, audioUrl);

            return Json(new { audioUrl, messageId = message.Id });
        }

        [HttpGet]
        public IActionResult GetNewMessages(int senderId, int lastId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var messages = _context.Messages
                .Where(m => ((m.SenderId == senderId && m.ReceiverId == myId) ||
                             (m.SenderId == myId && m.ReceiverId == senderId)) && m.Id > lastId)
                .OrderBy(m => m.SentAt)
                .Select(m => new { id = m.Id, senderId = m.SenderId, content = m.Content, fileUrl = m.FileUrl, isRead = m.IsRead, time = m.SentAt.ToString("hh:mm tt") })
                .ToList();
            return Json(messages);
        }

        [HttpPost]
        public async Task<IActionResult> React([FromBody] ReactRequest request)
        {
            var message = _context.Messages.FirstOrDefault(m => m.Id == request.MessageId);
            if (message == null) return NotFound();
            message.Reaction = request.Reaction;
            _context.SaveChanges();
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var receiverId = message.SenderId == myId ? message.ReceiverId : message.SenderId;
            await _hubContext.Clients.Group(receiverId.ToString())
                .SendAsync("ReceiveReaction", request.MessageId, request.Reaction);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> MuteChat([FromBody] MuteChatDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == myId);
            if (user == null) return Json(new { success = false });
            user.MutedUntil = dto.Hours == -1 ? DateTime.UtcNow.AddYears(99) : DateTime.UtcNow.AddHours(dto.Hours);
            await _context.SaveChangesAsync();
            return Json(new { success = true, mutedUntil = user.MutedUntil });
        }

        [HttpPost]
        public async Task<IActionResult> UnmuteChat()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == myId);
            if (user == null) return Json(new { success = false });
            user.MutedUntil = null;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult GetMuteStatus()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == myId);
            if (user == null) return Json(new { muted = false });
            var muted = user.MutedUntil != null && user.MutedUntil > DateTime.UtcNow;
            return Json(new { muted, mutedUntil = user.MutedUntil });
        }

        [HttpPost]
        public async Task<IActionResult> PinChat([FromBody] PinChatDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == myId);
            if (user == null) return Json(new { success = false });
            var pinned = (user.PinnedChats ?? "").Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (!pinned.Contains(dto.UserId.ToString()))
            {
                if (pinned.Count >= 3) return Json(new { success = false, error = "Máximo 3 chats anclados" });
                pinned.Add(dto.UserId.ToString());
            }
            user.PinnedChats = string.Join(",", pinned);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UnpinChat([FromBody] PinChatDto dto)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == myId);
            if (user == null) return Json(new { success = false });
            var pinned = (user.PinnedChats ?? "").Split(',').Where(x => !string.IsNullOrEmpty(x) && x != dto.UserId.ToString()).ToList();
            user.PinnedChats = string.Join(",", pinned);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpGet]
        public IActionResult IsPinned(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var user = _context.Users.FirstOrDefault(u => u.Id == myId);
            if (user == null) return Json(new { pinned = false });
            var pinned = (user.PinnedChats ?? "").Split(',').Contains(userId.ToString());
            return Json(new { pinned });
        }

        public class MuteChatDto { public int Hours { get; set; } }
        public class PinChatDto { public int UserId { get; set; } }
        public class ReactRequest { public int MessageId { get; set; } public string Reaction { get; set; } = ""; }
        public class SendMessageRequestDto { public int ReceiverId { get; set; } public string Message { get; set; } }
        public class RespondRequestDto { public int RequestId { get; set; } public bool Accept { get; set; } }
    }
}