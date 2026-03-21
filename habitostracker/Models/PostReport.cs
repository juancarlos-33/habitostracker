using System;

namespace HabitTrackerApp.Models
{
    public class PostReport
    {
        public int Id { get; set; }

        public int PostId { get; set; }

        public int ReportedByUserId { get; set; }

        public string Reason { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}