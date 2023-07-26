namespace ActivityListenerService.Models;

public class PublishedMessagePayload<T>
{
    public T Payload { get; set; }
    public ActivityType ActivityType { get; set; }
}