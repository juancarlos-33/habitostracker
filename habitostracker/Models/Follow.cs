using System;

namespace HabitTrackerApp.Models
{
    public class Follow
    {
        public int Id { get; set; }

        public DateTime CreatedAt { get; set; }

        public int FollowerId { get; set; }
        public User Follower { get; set; }   // 👈 ESTO FALTABA

        public int FollowingId { get; set; }
        public User Following { get; set; }  // 👈 ESTO FALTABA
    }
}