using System;

namespace HabitTrackerApp.Models
{
    public class GroupMessageRead
    {
        public int Id { get; set; }
        public int GroupMessageId { get; set; }
        public GroupMessage GroupMessage { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    }
}