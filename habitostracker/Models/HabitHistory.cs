namespace HabitTrackerApp.Models
{
    public class HabitHistory
    {
        public int Id { get; set; }

        public int HabitId { get; set; }

        public string HabitName { get; set; }

        public DateTime Date { get; set; }

        public bool Completed { get; set; }
    }
}
