using System;

namespace HabitTrackerApp.Models
{
    public class SavedPost
    {
        public int Id { get; set; }

        public int PostId { get; set; }

        public int UserId { get; set; }

        public DateTime CreatedAt { get; set; }

        public Post Post { get; set; }
    }
}