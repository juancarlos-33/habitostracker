namespace HabitTrackerApp.Models
{
    public class Report
    {
        public int Id { get; set; }
        public int ReporterId { get; set; }
        public int ReportedId { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public User Reporter { get; set; }
        public User Reported { get; set; }
    }
}