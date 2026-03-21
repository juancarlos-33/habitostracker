using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

public class UserIdProvider : IUserIdProvider
{
    public string GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst("UserId")?.Value;
    }
}