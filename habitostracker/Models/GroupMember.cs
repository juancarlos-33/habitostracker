using System;

namespace HabitTrackerApp.Models
{
    public class GroupMember
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public Group Group { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public string Role { get; set; } = "Member";
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public bool IsMuted { get; set; } = false; // 🔥 silenciar notificaciones
        public DateTime? LeftAt { get; set; } // 🔥 cuando se salió
    }
}