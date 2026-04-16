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
        public string Role { get; set; } = "Member"; // Admin, Member
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}