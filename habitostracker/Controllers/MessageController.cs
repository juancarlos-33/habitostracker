using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApp.Hubs;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class MessageController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly CloudinaryService _cloudinaryService;

        public MessageController(HabitDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
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

            return View(conversations);
        }

        public async Task<IActionResult> Chat(int userId)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null) return RedirectToAction("Login", "Account");

            var myId = int.Parse(userIdClaim.Value);

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

            return View(messages);
        }

        [HttpPost]
        public async Task<IActionResult> Send(int receiverId, string content, IFormFile file)
        {
            var currentUser = GetCurrentUser();

            if (currentUser.Role == "Guest")
                return Json(new { success = false, error = "Debes crear una cuenta para enviar mensajes." });

            var senderId = int.Parse(User.FindFirst("UserId").Value);
            var senderName = User.Identity?.Name ?? "Usuario";

            var receiverExists = _context.Users.Any(u => u.Id == receiverId);
            if (!receiverExists)
                return Json(new { success = false, error = "El usuario ya no existe." });

            string filePath = null;

            if (file != null && file.Length > 0)
            {
                var ext = Path.GetExtension(file.FileName).ToLower();
                var videoExts = new[] { ".mp4", ".webm", ".mov", ".avi" };

                if (videoExts.Contains(ext))
                {
                    filePath = await _cloudinaryService.UploadVideoAsync(file);
                }
                else
                {
                    filePath = await _cloudinaryService.UploadImageAsync(file, "habitostracker/messages");
                }
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

            _context.Notifications.Add(new Notification
            {
                UserId = receiverId,
                FromUserId = senderId,
                Message = "💬 Nuevo mensaje de " + senderName,
                CreatedAt = DateTime.Now,
                IsRead = false,
                FromUserImage = sender?.ProfileImage ?? "",
                FromUsername = senderName
            });

            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(receiverId.ToString())
                .SendAsync("ReceiveMessage", senderId, receiverId, senderName, content ?? "", filePath ?? "");

            await _hubContext.Clients.Group(receiverId.ToString())
                .SendAsync("ReceiveNotification", senderId, "💬 Nuevo mensaje", senderName,
                    sender?.ProfileImage ?? "", "/Message/Chat?userId=" + senderId);

            // 🔥 también notificar al emisor para que vea su propio mensaje
            await _hubContext.Clients.Group(senderId.ToString())
                .SendAsync("MessageSentConfirm", message.Id, filePath ?? "");

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
                // eliminar para todos
                _context.Messages.Remove(msg);
                await _context.SaveChangesAsync();
                // notificar al receptor
                await _hubContext.Clients.Group(msg.ReceiverId.ToString())
                    .SendAsync("MessageDeleted", messageId);
            }
            else
            {
                // eliminar solo para mí — marcamos como eliminado
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

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/audios");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            var fileName = Guid.NewGuid().ToString() + ".webm";
            var filePath = Path.Combine(folderPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await audio.CopyToAsync(stream);

            var receiverId = int.Parse(Request.Form["receiverId"]);
            var audioUrl = "/audios/" + fileName;

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = audioUrl,
                SentAt = DateTime.Now,
                IsRead = false
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // 🔥 notificar en tiempo real al receptor
            await _hubContext.Clients.Group(receiverId.ToString())
                .SendAsync("ReceiveAudio", senderId, audioUrl);

            return Json(new { audioUrl });
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



        public class ReactRequest
        {
            public int MessageId { get; set; }
            public string Reaction { get; set; } = "";
        }
    }
}