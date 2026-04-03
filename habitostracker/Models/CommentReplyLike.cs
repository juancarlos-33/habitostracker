public class CommentReplyLike
{
    public int Id { get; set; }

    public int ReplyId { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}