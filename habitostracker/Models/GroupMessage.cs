using System;
using System.Collections.Generic;

namespace HabitTrackerApp.Models
{
    public class GroupMessage
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public Group Group { get; set; }
        public ICollection<GroupMessageReaction> Reactions { get; set; } = new List<GroupMessageReaction>();
        public ICollection<GroupMessageRead> Reads { get; set; } = new List<GroupMessageRead>();
        public int? SenderId { get; set; }
        public User Sender { get; set; }
        public string Content { get; set; }
        public string? FileUrl { get; set; }
        public string? Reaction { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public bool IsSystem { get; set; } = false; // 🔥 mensajes de sistema
    }
}