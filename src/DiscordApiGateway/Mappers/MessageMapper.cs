using DiscordApiGateway.Models;

namespace DiscordApiGateway.Mappers;

public static class MessageMapper
{
    public static DiscordMessage MapMessage(this DSharpPlus.Entities.DiscordMessage message)
    {
        return new DiscordMessage
        {
            Content = message.Content,
            HasAttatchment = message.Attachments.Any(),
            IsEdited = message.EditedTimestamp != null,
            IsRemoved = false,
            DiscordTimestamp = message.Timestamp.DateTime,
            ReplyToMessage = message.ReferencedMessage?.MapMessage(),
            ChannelId = message.ChannelId,
            AuthorId = message.Author.Id
        };
    }
}