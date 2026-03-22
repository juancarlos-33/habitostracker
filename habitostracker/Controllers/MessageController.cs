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

        public MessageController(HabitDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // =====================================
        // 📥 BANDEJA DE MENSAJES
        // =====================================
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

            // 🔥 cargar AMBOS usuarios
            foreach (var msg in conversations)
            {
                msg.Sender = _context.Users.FirstOrDefault(u => u.Id == msg.SenderId);
                msg.Receiver = _context.Users.FirstOrDefault(u => u.Id == msg.ReceiverId);
            }

            return View(conversations);
        }
        // =====================================
        // 💬 CHAT ENTRE USUARIOS
        // =====================================
        public async Task<IActionResult> Chat(int userId)
        {
            var userIdClaim = User.FindFirst("UserId");

            if (userIdClaim == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var myId = int.Parse(userIdClaim.Value);

            // 🔹 MENSAJES NO LEÍDOS
            var unreadMessages = _context.Messages
                .Where(m => m.SenderId == userId && m.ReceiverId == myId && !m.IsRead)
                .ToList();

            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;

                // 🔔 avisar al emisor que el mensaje fue visto
                await _hubContext.Clients
                    .Group(msg.SenderId.ToString())
                    .SendAsync("MessageSeen", msg.Id);
            }

            _context.SaveChanges();

            // 🔔 actualizar mensajes si el chat ya está abierto
            await _hubContext.Clients
                .Group(userId.ToString())
                .SendAsync("ForceSeenUpdate");

            // 🔹 CARGAR CONVERSACIÓN
            var messages = _context.Messages
                .Where(m =>
                    (m.SenderId == myId && m.ReceiverId == userId) ||
                    (m.SenderId == userId && m.ReceiverId == myId))
                .Include(m => m.Sender)
                .OrderBy(m => m.SentAt)
                .ToList();

            ViewBag.OtherUserId = userId;

            var otherUser = _context.Users.FirstOrDefault(u => u.Id == userId);

            ViewBag.OtherUsername = otherUser?.Username ?? "Usuario";
            ViewBag.OtherLastOnline = otherUser?.LastOnline;
            ViewBag.OtherUserProfileImage = otherUser?.ProfileImage;

            return View(messages);
        }

        // =====================================
        // ✉️ ENVIAR MENSAJE
        // =====================================
        [HttpPost]
        public async Task<IActionResult> Send(int receiverId, string content, IFormFile file)
        {
            var senderId = int.Parse(User.FindFirst("UserId").Value);
            var senderName = User.Identity?.Name ?? "Usuario";

            // 🔥 VALIDAR QUE EL RECEPTOR EXISTE
            var receiverExists = _context.Users.Any(u => u.Id == receiverId);
            if (!receiverExists)
            {
                TempData["Error"] = "El usuario ya no existe.";
                return RedirectToAction("Inbox");
            }

            string filePath = null;

            // 📁 SI HAY ARCHIVO
            if (file != null && file.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");

                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                var fullPath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                filePath = "/uploads/" + fileName;
            }

            // 🔥 VALIDAR QUE NO ESTÉ TODO VACÍO
            if (string.IsNullOrWhiteSpace(content) && file == null)
            {
                return RedirectToAction("Chat", new { userId = receiverId });
            }

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

            // 🔔 GUARDAR NOTIFICACIÓN
            _context.Notifications.Add(new Notification
            {


                UserId = receiverId,
                FromUserId = senderId,
                Message = "💬 Nuevo mensaje de " + senderName,
                CreatedAt = DateTime.Now,
                IsRead = false,
                FromUserImage = _context.Users
                    .Where(u => u.Id == senderId)
                    .Select(u => u.ProfileImage)
                    .FirstOrDefault() ?? "",
                FromUsername = senderName
            });

            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(receiverId.ToString())
    .SendAsync(
        "ReceiveMessage",
        senderId,
        receiverId,
        senderName,
        content ?? ""
    );



            // 🔥 🔥 🔥 AQUÍ ESTÁ LA CLAVE (ENVÍO CORRECTO)

            // 🔥 obtener usuario
            var sender = _context.Users.FirstOrDefault(u => u.Id == senderId);

            // 🔥 enviar por GRUPO (principal)
            await _hubContext.Clients.Group(receiverId.ToString())
                .SendAsync(
                   "ReceiveNotification",
                    senderId,
                    "💬 Nuevo mensaje",
                    senderName,
                    sender?.ProfileImage ?? "",
                    "/Message/Chat?userId=" + senderId
                );

            // 🔥 respaldo por USER (doble seguridad)
            await _hubContext.Clients.User(receiverId.ToString())
                .SendAsync(
                   "ReceiveNotification",
                    senderId,
                    "💬 Nuevo mensaje",
                    senderName,
                    sender?.ProfileImage ?? "",
                    "/Message/Chat?userId=" + senderId
                );


            return RedirectToAction("Chat", new { userId = receiverId });
        }

        // =====================================
        // 👀 MARCAR MENSAJES COMO VISTOS EN TIEMPO REAL
        // =====================================
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

                await _hubContext.Clients
                    .Group(senderId.ToString())
                    .SendAsync("MessageSeen", msg.Id);
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
            ViewBag.MyImage = me?.ProfileImage;
            ViewBag.OtherUsername = other.Username;
            ViewBag.OtherImage = other.ProfileImage;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendAudio(IFormFile audio)
        {
            var senderId = int.Parse(User.FindFirst("UserId").Value);

            if (audio == null || audio.Length == 0)
                return BadRequest();

            // 📁 crear carpeta si no existe
            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/audios");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            // 🧾 nombre único
            var fileName = Guid.NewGuid().ToString() + ".webm";
            var filePath = Path.Combine(folderPath, fileName);

            // 💾 guardar archivo
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await audio.CopyToAsync(stream);
            }

            // 💬 guardar como mensaje
            var receiverId = int.Parse(Request.Form["receiverId"]);

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId, // 🔥 CLAVE
                Content = "/audios/" + fileName,
                SentAt = DateTime.Now,
                IsRead = false
            };
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public IActionResult React(int messageId, string reaction)
        {
            var message = _context.Messages.FirstOrDefault(m => m.Id == messageId);

            if (message == null)
                return NotFound();

            message.Reaction = reaction;

            _context.SaveChanges();

            return Ok();
        }
    }
}