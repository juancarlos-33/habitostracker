using System;

namespace HabitTrackerApp.Models
{
    public class Message
    {
        public int Id { get; set; }

        public int SenderId { get; set; }

        public int ReceiverId { get; set; }

        public string Content { get; set; }

        public DateTime SentAt { get; set; } = DateTime.Now;

        public bool IsRead { get; set; } = false;

        public User Sender { get; set; }

        public User Receiver { get; set; }
    }
}