namespace TelegramProxy.Models
{
    public readonly record struct TrafficEvent(long BytesSent, long BytesReceived);
}
