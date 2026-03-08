using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using HabitTrackerApp.Data;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApp.Models;

namespace HabitTrackerApp.Hubs
{


    public class ChatHub : Hub
    {
        private static HashSet<string> ConnectedUsers = new HashSet<string>();
        private readonly HabitDbContext _context;

        public ChatHub(HabitDbContext context)
        {
            _context = context;
        }

        public async Task ForceLogout(string userId)
        {
            await Clients.User(userId).SendAsync("ForceLogout");
        }

        public async Task JoinUserGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }

        public async Task UserTyping(string receiverId, string username)
        {
            await Clients.Group(receiverId).SendAsync("ShowTyping", username);
        }

        public async Task StopTyping(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("HideTyping");
        }

        // 🔔 CUANDO EL USUARIO VE LOS MENSAJES
        public async Task MessagesViewed(string senderId)
        {
            await Clients.Group(senderId)
                .SendAsync("ForceSeenUpdate");
        }

        // 🟢 CUANDO EL USUARIO SE CONECTA
        public override async Task OnConnectedAsync()
        {
            var userId = Context.GetHttpContext()?.User?.FindFirst("UserId")?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // unir automáticamente al grupo del usuario
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);

                // avisar que está online
                await Clients.Others.SendAsync("UserOnline", userId);

                // solo ejecutar la primera vez que se conecta
                if (!ConnectedUsers.Contains(userId))
                {
                    ConnectedUsers.Add(userId);

                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                    var superAdmin = await _context.Users.FirstOrDefaultAsync(u => u.Role == "SuperAdmin");

                    if (superAdmin != null && user != null && user.Role != "SuperAdmin")
                    {
                        await Clients.User(superAdmin.Id.ToString())
                            .SendAsync("UserConnectedNotification", user.Username);
                    }
                }
            }

            await base.OnConnectedAsync();
        }

        // ⚫ CUANDO EL USUARIO SE DESCONECTA
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.GetHttpContext()?.User?.FindFirst("UserId")?.Value;

            if (userId != null)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));

                if (user != null)
                {
                    user.LastOnline = DateTime.Now;
                    await _context.SaveChangesAsync();
                }

                // avisar a todos que está offline
                await Clients.All.SendAsync("UserOffline", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}