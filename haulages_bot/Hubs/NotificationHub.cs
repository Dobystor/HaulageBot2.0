using Microsoft.AspNetCore.SignalR;

namespace haulages_bot.Hubs
{
    public class NotificationHub: Hub
    {
        // Método que puede ser llamado desde el cliente para recibir notificaciones
        public async Task SendNotification(string message)
        {
            // Enviar el mensaje a todos los clientes conectados
            await Clients.All.SendAsync("ReceiveNotification", message);
        }
    }
}
