namespace HabitTrackerApp.Models
{
    public class FriendRequestViewModel
    {
        public int Id { get; set; }

        public int SenderId { get; set; }

        public string SenderUsername { get; set; }

        public string ProfileImage { get; set; }
    }
}