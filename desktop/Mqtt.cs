using MQTTnet;
using MQTTnet.Protocol;

namespace RackWatch;

// Broker connection. The board decides the state; the app only shows it, stores it and sends commands.
public static class Mqtt
{
    const string Topic = "rackwatch/room1/";
    static readonly IMqttClient Client = new MqttClientFactory().CreateMqttClient();
    static DateTime lastSeen;

    public static Action<Status>? OnStatus;
    public static Action<Alert>? OnAlert;
    public static Status? Last { get; private set; }
    public static bool Connected => Client.IsConnected;

    // Board is online while live status keeps arriving (every 2 s); "offline" from the Last Will ends it at once
    public static bool BoardOnline => DateTime.Now - lastSeen < TimeSpan.FromSeconds(10);

    public static void Start()
    {
        Client.ApplicationMessageReceivedAsync += e =>
        {
            var message = e.ApplicationMessage;
            var payload = message.ConvertPayloadToString();
            switch (message.Topic[Topic.Length..])
            {
                case "status":
                    Last = Json.Parse<Status>(payload);
                    if (!message.Retain)    // retained = last known value, not live data
                    {
                        lastSeen = DateTime.Now;
                        Db.Save(Last);
                    }
                    OnStatus?.Invoke(Last);
                    break;
                case "alert":
                    var alert = Json.Parse<Alert>(payload);
                    Db.Save(alert);
                    OnAlert?.Invoke(alert);
                    break;
                case "online" when payload == "offline":
                    lastSeen = DateTime.MinValue;
                    break;
            }
            return Task.CompletedTask;
        };
        _ = Task.Run(KeepConnected);
    }

    public static Task Send(string command) => Client.IsConnected
        ? Client.PublishStringAsync(Topic + "cmd", command, MqttQualityOfServiceLevel.AtLeastOnce)
        : Task.CompletedTask;

    static async Task KeepConnected()
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer("localhost", 1883)
            .WithCredentials("desktop", "desktoppass")
            .WithClientId("rackwatch-desktop")
            .Build();

        while (true)
        {
            if (!Client.IsConnected)
            {
                try
                {
                    await Client.ConnectAsync(options);
                    await Client.SubscribeAsync(Topic + "#");
                }
                catch
                {
                    // broker not reachable yet, next try in 5 s
                }
            }
            await Task.Delay(5000);
        }
    }
}
