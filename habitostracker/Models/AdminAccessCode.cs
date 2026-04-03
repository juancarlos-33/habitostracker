public class AdminAccessCode
{
    public int Id { get; set; }

    public string Code { get; set; }

    public bool IsUsed { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int CreatedByUserId { get; set; }
}