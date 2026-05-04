using System;

namespace HabitTrackerApp.Models
{
    public class StarredMessage
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public GroupMessage Message { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public DateTime StarredAt { get; set; } = DateTime.UtcNow;
    }
}