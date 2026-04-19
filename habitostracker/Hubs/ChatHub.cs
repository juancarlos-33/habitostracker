using Microsoft.AspNetCore.SignalR;
using HabitTrackerApp.Data;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;

namespace HabitTrackerApp.Hubs
{
    public class ChatHub : Hub
    {
        private static readonly HashSet<string> ConnectedUsers = new HashSet<string>();
        private static readonly Dictionary<string, HashSet<string>> ActiveGroupChats = new();
        private readonly HabitDbContext _context;
        private readonly OnlineUsersService _onlineUsers;

        public ChatHub(HabitDbContext context, OnlineUsersService onlineUsers)
        {
            _context = context;
            _onlineUsers = onlineUsers;
        }

        public async Task KickBlockedIP(string ip)
        {
            await Clients.All.SendAsync("IPBlocked", ip);
        }

        public async Task SendNotification(string userId, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveNotification", message);
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

        public async Task MessagesViewed(string senderId)
        {
            if (!string.IsNullOrEmpty(senderId))
                await Clients.Group(senderId).SendAsync("ForceSeenUpdate");
        }

        public async Task EnterChat(string withUserId)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
                _onlineUsers.SetActiveChat(userId, withUserId);
            await Task.CompletedTask;
        }

        public async Task LeaveChat(string withUserId)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
                _onlineUsers.ClearActiveChat(userId);
            await Task.CompletedTask;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers.SetOnline(userId);
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
                await Clients.All.SendAsync("UserOnline", userId);

                var pendingSenders = await _context.Messages
                    .Where(m => m.ReceiverId == int.Parse(userId) && !m.IsRead)
                    .Select(m => m.SenderId)
                    .Distinct()
                    .ToListAsync();

                foreach (var senderId in pendingSenders)
                {
                    var pendingMsgIds = await _context.Messages
                        .Where(m => m.SenderId == senderId &&
                                    m.ReceiverId == int.Parse(userId) &&
                                    !m.IsRead)
                        .Select(m => m.Id)
                        .ToListAsync();

                    foreach (var msgId in pendingMsgIds)
                        await Clients.Group(senderId.ToString())
                            .SendAsync("MessageSentConfirm", msgId, "");
                }

                try
                {
                    var userIdInt = int.Parse(userId);
                    var groupIds = await _context.GroupMembers
                        .Where(m => m.UserId == userIdInt && m.IsActive)
                        .Select(m => m.GroupId)
                        .ToListAsync();

                    foreach (var gid in groupIds)
                    {
                        var unreadGroupMsgs = await _context.GroupMessages
                            .Where(m => m.GroupId == gid && !m.IsDeleted && m.SenderId != userIdInt)
                            .Include(m => m.Reads)
                            .Where(m => !m.Reads.Any(r => r.UserId == userIdInt))
                            .Select(m => m.Id)
                            .ToListAsync();

                        foreach (var msgId in unreadGroupMsgs)
                        {
                            var readCount = await _context.GroupMessageReads
                                .CountAsync(r => r.GroupMessageId == msgId);
                            var activeInChat = ActiveGroupChats.ContainsKey(gid.ToString())
                                ? ActiveGroupChats[gid.ToString()].Count : 0;
                            await Clients.Group("group-" + gid)
                                .SendAsync("GroupMessageRead", msgId.ToString(), readCount, activeInChat);
                        }
                    }
                }
                catch { }

                if (!ConnectedUsers.Contains(userId))
                {
                    ConnectedUsers.Add(userId);
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                    var superAdmin = await _context.Users.FirstOrDefaultAsync(u => u.Role == "SuperAdmin");
                    if (superAdmin != null && user != null && user.Role != "SuperAdmin")
                        await Clients.User(superAdmin.Id.ToString())
                            .SendAsync("UserConnectedNotification", user.Username);
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers.SetOffline(userId);
                ConnectedUsers.Remove(userId);

                // limpiar de todos los chats activos
                foreach (var group in ActiveGroupChats)
                    group.Value.Remove(userId);

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                if (user != null)
                {
                    user.LastOnline = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
                await Clients.All.SendAsync("UserOffline", userId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        // ── LLAMADAS ──────────────────────────────────────────────
        public async Task CallUser(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("IncomingCall", Context.UserIdentifier);
        }

        public async Task SendOffer(string receiverId, string offer)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveOffer", offer);
        }

        public async Task SendIceCandidate(string receiverId, string candidate)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveIceCandidate", candidate);
        }

        public async Task SendAnswer(string receiverId, string answer)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveAnswer", answer);
        }

        public async Task CallReady(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("PeerReady");
        }

        public async Task HangUp(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("CallEnded");
        }

        // ── REACCIONES ────────────────────────────────────────────
        public async Task SendReaction(string receiverId, int messageId, string reaction)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveReaction", messageId, reaction);
        }

        // ── GRABACIÓN ─────────────────────────────────────────────
        public async Task StartRecording(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("UserRecording");
        }

        public async Task StopRecording(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("UserStoppedRecording");
        }

        // ── SESIONES ──────────────────────────────────────────────
        public async Task ForceLogoutSession(string sessionToken)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
                await Clients.Group(userId).SendAsync("ForceLogoutSession", sessionToken);
        }

        // ── GRUPOS ────────────────────────────────────────────────
        public async Task JoinGroupChat(string groupId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "group-" + groupId);

            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                if (!ActiveGroupChats.ContainsKey(groupId))
                    ActiveGroupChats[groupId] = new HashSet<string>();
                ActiveGroupChats[groupId].Add(userId);

                await Clients.Group("group-" + groupId)
                    .SendAsync("GroupActiveUsers", groupId, ActiveGroupChats[groupId].Count);
            }
        }

        public async Task LeaveGroupChat(string groupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "group-" + groupId);

            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId) && ActiveGroupChats.ContainsKey(groupId))
            {
                ActiveGroupChats[groupId].Remove(userId);
                await Clients.Group("group-" + groupId)
                    .SendAsync("GroupActiveUsers", groupId, ActiveGroupChats[groupId].Count);
            }
        }

        public async Task SendGroupMessage(string groupId, string senderId, string senderName,
            string content, string msgId, string fileUrl, string fileType)
        {
            try
            {
                var sender = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(senderId));
                var senderImage = sender?.ProfileImage ?? sender?.ProfilePicture ?? "";
                var time = DateTime.Now.ToString("hh:mm tt");

                await Clients.OthersInGroup("group-" + groupId)
                    .SendAsync("ReceiveGroupMessage", senderId, senderName, senderImage,
                        content, time, msgId, fileUrl, fileType);

                var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == int.Parse(groupId));

                var members = await _context.GroupMembers
                    .Where(m => m.GroupId == int.Parse(groupId)
                             && m.IsActive
                             && m.UserId != int.Parse(senderId)
                             && !m.IsMuted)
                    .ToListAsync();

                foreach (var member in members)
                {
                    string preview;
                    if (fileType == "audio") preview = "🎵 Mensaje de voz";
                    else if (fileType == "image") preview = "📷 Imagen";
                    else if (fileType == "video") preview = "🎥 Video";
                    else preview = content.Length > 50 ? content.Substring(0, 47) + "..." : content;

                    await Clients.Group(member.UserId.ToString())
                        .SendAsync("ReceiveNotification",
                            member.UserId.ToString(),
                            $"{senderName}: {preview}",
                            senderName,
                            senderImage,
                            $"/Group/Chat/{groupId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendGroupMessage error: {ex.Message}");
            }
        }

        public async Task NotifyGroupRead(string groupId, string msgId)
        {
            try
            {
                var readCount = await _context.GroupMessageReads
                    .CountAsync(r => r.GroupMessageId == int.Parse(msgId));

                var activeInChat = ActiveGroupChats.ContainsKey(groupId)
                    ? ActiveGroupChats[groupId].Count : 0;

                await Clients.Group("group-" + groupId)
                    .SendAsync("GroupMessageRead", msgId, readCount, activeInChat);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ NotifyGroupRead error: {ex.Message}");
            }
        }

        public async Task GroupUserTyping(string groupId, string senderName)
        {
            await Clients.OthersInGroup("group-" + groupId)
                .SendAsync("GroupUserTyping", senderName);
        }

        public async Task GroupUserStoppedTyping(string groupId)
        {
            await Clients.OthersInGroup("group-" + groupId)
                .SendAsync("GroupUserStoppedTyping");
        }

        public async Task RemoveMemberFromGroup(string groupId, string removedUserId, string message)
        {
            await Clients.Group("group-" + groupId)
                .SendAsync("MemberRemovedFromGroup", removedUserId, message);
        }

        public async Task NewSessionDetected(string userId)
        {
            await Clients.Group(userId).SendAsync("NewSessionDetected");
        }
    }
}