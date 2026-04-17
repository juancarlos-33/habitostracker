using System;

namespace HabitTrackerApp.Models
{
    public class MessageRequest
    {
        public int Id { get; set; }
        public int SenderId { get; set; }
        public User Sender { get; set; }
        public int ReceiverId { get; set; }
        public User Receiver { get; set; }
        public string FirstMessage { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}