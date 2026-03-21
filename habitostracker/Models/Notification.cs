namespace HabitTrackerApp.Models
{
    public class Notification
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Message { get; set; }

        public bool IsRead { get; set; }

        public string? Link { get; set; }

   


        public DateTime CreatedAt { get; set; }


        public int? FromUserId { get; set; }
        public string FromUsername { get; set; }
        public string FromUserImage { get; set; }
    }
}