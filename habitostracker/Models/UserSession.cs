using System;

namespace HabitTrackerApp.Models
{
    public class UserSession
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public string SessionToken { get; set; }
        public string? Device { get; set; }
        public string? Browser { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}