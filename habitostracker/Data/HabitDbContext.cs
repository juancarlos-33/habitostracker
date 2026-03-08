using Microsoft.EntityFrameworkCore;
using HabitTrackerApp.Models;

namespace HabitTrackerApp.Data
{
    public class HabitDbContext : DbContext
    {
        public HabitDbContext(DbContextOptions<HabitDbContext> options)
            : base(options)
        {
        }

        public DbSet<Habit> Habits { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<FriendRequest> FriendRequests { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<AdminLog> AdminLogs { get; set; }
        public DbSet<HabitHistory> HabitHistories { get; set; }
        public DbSet<Achievement> Achievements { get; set; }
        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Message>()
                .HasOne(m => m.Receiver)
                .WithMany()
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
