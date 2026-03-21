using System;

namespace HabitTrackerApp.Models
{
    public class SuspiciousActivity
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Username { get; set; }

        public string Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool Resolved { get; set; } = false;
    }
}