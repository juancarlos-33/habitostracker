namespace HabitTrackerApp.Models
{
    public class Achievement
    {
        public int Id { get; set; }

        public int HabitId { get; set; }

        public string HabitName { get; set; }

        public string Title { get; set; }

        public DateTime DateUnlocked { get; set; }
    }
}
