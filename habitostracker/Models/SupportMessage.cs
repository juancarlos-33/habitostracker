using System;

namespace HabitTrackerApp.Models
{
    public class SupportMessage
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Message { get; set; }

        public string Response { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}