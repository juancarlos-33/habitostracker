using System;

namespace HabitTrackerApp.Models
{
    public class PostReport
    {
        public int Id { get; set; }

        public int PostId { get; set; }
        public Post Post { get; set; } // 🔥 RELACIÓN

        public int ReportedByUserId { get; set; }
        public User ReportedByUser { get; set; } // 🔥 RELACIÓN

        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}