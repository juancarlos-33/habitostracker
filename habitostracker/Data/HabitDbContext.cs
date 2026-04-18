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

        public DbSet<BlockedIP> BlockedIPs { get; set; }
        public DbSet<Block> Blocks { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }

        public DbSet<Group> Groups { get; set; }
        public DbSet<GroupMember> GroupMembers { get; set; }
        public DbSet<GroupMessage> GroupMessages { get; set; }

        public DbSet<HabitComment> HabitComments { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Follow> Follows { get; set; }
        public DbSet<PostLike> PostLikes { get; set; }
        public DbSet<ConnectionBlock> ConnectionBlocks { get; set; }
        public DbSet<SavedPost> SavedPosts { get; set; }
        public DbSet<CommentReply> CommentReplies { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<PostComment> PostComments { get; set; }
        public DbSet<MessageRequest> MessageRequests { get; set; }
        public DbSet<GroupMessageRead> GroupMessageReads { get; set; }
        public DbSet<HabitProgress> HabitProgresses { get; set; }
        public DbSet<HabitProgressReaction> HabitProgressReactions { get; set; }
        public DbSet<HabitProgressComment> HabitProgressComments { get; set; }
        public DbSet<GroupReport> GroupReports { get; set; }
        public DbSet<BlockedIP> BlockedIP { get; set; }
        public DbSet<AdminAccessCode> AdminAccessCodes { get; set; }
        public DbSet<CommentLike> CommentLikes { get; set; }
        public DbSet<CommentReplyLike> CommentReplyLikes { get; set; }
        public DbSet<SupportMessage> SupportMessages { get; set; }
        public DbSet<PostReport> PostReports { get; set; }
        public DbSet<FriendRequest> FriendRequests { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<SuspiciousActivity> SuspiciousActivities { get; set; }
        public DbSet<AdminLog> AdminLogs { get; set; }
        public DbSet<HabitHistory> HabitHistories { get; set; }
        public DbSet<Achievement> Achievements { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Payment> Payments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Follow>()
    .HasOne(f => f.Following)
    .WithMany()
    .HasForeignKey(f => f.FollowingId)
    .OnDelete(DeleteBehavior.Restrict);


            // 🔥 MESSAGE
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

            // 🔥 HABIT COMMENT (EL FIX REAL)
            modelBuilder.Entity<HabitComment>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
