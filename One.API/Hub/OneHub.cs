using Microsoft.AspNetCore.SignalR;

namespace One.API.Hub
{
    public class OneHub : Microsoft.AspNetCore.SignalR.Hub
    {
        // تقدر تضيف دوال هنا زي:
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}