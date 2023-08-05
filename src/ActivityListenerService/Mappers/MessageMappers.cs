using ActivityListenerService.Models;
using DSharpPlus.EventArgs;

namespace ActivityListenerService.Mappers;

public static class MessageMappers
{
    public static DiscordMessage MapToDiscordMessage(this MessageCreateEventArgs createEventArgs)
    {
        return new DiscordMessage
        {
            Id = createEventArgs.Message.Id,
            AuthorId = createEventArgs.Author.Id,
            Content = createEventArgs.Message.Content,
            HasAttatchment = createEventArgs.Message.Attachments.Any(),
            DiscordTimestamp = createEventArgs.Message.Timestamp.UtcDateTime,
            IsEdited = false,
            IsRemoved = false,
            ReplyToMessageId = createEventArgs.Message.ReferencedMessage?.Id ?? null,
        };
    }
    
    public static DiscordMessageEdit MapToDiscordMessageEdit(this MessageUpdateEventArgs updateEventArgs)
    {
        return new DiscordMessageEdit
        {
            Id = updateEventArgs.Message.Id,
            ContentAfter = updateEventArgs.Message.Content,
            ContentBefore = updateEventArgs.MessageBefore.Content,
            DiscordTimestamp = updateEventArgs.Message.Timestamp.UtcDateTime,
        };
    }
    
    public static DiscordMessageEdit MapToDiscordMessageEdit(this MessageDeleteEventArgs deleteEventArgs)
    {
        return new DiscordMessageEdit
        {
            Id = deleteEventArgs.Message.Id,
            ContentAfter = null,
            ContentBefore = deleteEventArgs.Message.Content,
            DiscordTimestamp = deleteEventArgs.Message.Timestamp.UtcDateTime,
        };
    }
}