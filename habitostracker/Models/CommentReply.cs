namespace HabitTrackerApp.Models
{
    public class CommentReply
    {
        public int Id { get; set; }

        public int CommentId { get; set; }

        public int UserId { get; set; }

        public string Username { get; set; }

        public string Text { get; set; }
        public int? ParentReplyId { get; set; }
        public string ProfileImage { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}