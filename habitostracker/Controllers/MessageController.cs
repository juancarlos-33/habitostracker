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

            foreach (var msg in conversations)
            {
                msg.Sender = _context.Users.FirstOrDefault(u => u.Id == msg.SenderId);
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
        public async Task<IActionResult> Send(int receiverId, string content)
        {
            var senderId = int.Parse(User.FindFirst("UserId").Value);
            var senderName = User.Identity?.Name ?? "Usuario";

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = content,
                SentAt = DateTime.Now,
                IsRead = false
            };

            _context.Messages.Add(message);
            _context.SaveChanges();

            // 🔔 enviar mensaje al receptor EN TIEMPO REAL
            await _hubContext.Clients.Group(receiverId.ToString()).SendAsync(
                "ReceiveMessage",
                senderId,
                receiverId,
                senderName,
                content
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
    }
}