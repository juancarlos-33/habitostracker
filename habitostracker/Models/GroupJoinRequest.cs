using System;

namespace HabitTrackerApp.Models
{
    public class GroupJoinRequest
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public Group Group { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public string Status { get; set; } = "Pending"; // "Pending", "Accepted", "Rejected"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}