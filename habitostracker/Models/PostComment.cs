using System;

namespace HabitTrackerApp.Models
{
    public class PostComment
    {
        public int Id { get; set; }

        public int PostId { get; set; }

        public int UserId { get; set; }

        public string Username { get; set; }

        public string Comment { get; set; }

        public DateTime CreatedAt { get; set; }

        public string? ImagePath { get; set; }
    }
}