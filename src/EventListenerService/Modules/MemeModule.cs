using System.Text.Json;
using DSharpPlus;
using NetCord.Services.ApplicationCommands;
using EventListenerService.Data;
using NetCord.Rest;

namespace EventListenerService.Modules;

public class MemeModule(WojtusContext dbContext, ILogger<MemeModule> logger) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("scanmemes", "Scan last 10 messages in the memes channel and save to database")]
    public async Task ScanMemesAsync()
    {
        // #memes channel ID
        ulong memesChannelId = 344854522704822282;

        var discordRestClient = new DiscordRestClient(new DSharpPlus.Net.RestClientOptions(), "token", TokenType.Bot);

        try
        {
            ulong? beforeMessageId = null;
            int totalSaved = 0;
            while (true)
            {
                var messages = await discordRestClient.GetChannelMessagesAsync(memesChannelId, 50, beforeMessageId, null, null);
                var messageList = messages.ToList();
                logger.LogInformation("Fetched {Count} messages from memes channel.", messageList.Count);
                if (messageList.Count == 0)
                    break;

                var memeMessages = messageList.Select(message => new Models.MemeMessage
                {
                    MessageId = message.Id,
                    ChannelId = message.ChannelId,
                    AuthorId = message.Author.Id,
                    AuthorUsername = message.Author.Username,
                    Content = message.Content,
                    Timestamp = message.Timestamp.UtcDateTime,
                    ImageUrl = message.Attachments.FirstOrDefault()?.Url,
                    ImageUrlProxy = message.Attachments.FirstOrDefault()?.ProxyUrl
                }).ToList();

                dbContext.MemeMessages.AddRange(memeMessages);
                await dbContext.SaveChangesAsync();
                totalSaved += memeMessages.Count;
                logger.LogInformation("Saved {Count} meme messages to the database.", memeMessages.Count);
                logger.LogInformation("Total saved so far: {TotalSaved}", totalSaved);
                logger.LogInformation("Last message ID: {LastMessageId}", messageList.Last().Id);

                // Use the oldest message ID as the anchor for the next batch
                beforeMessageId = messageList.Last().Id;

                // If less than 50 messages were returned, we've reached the end
                if (messageList.Count < 50)
                    break;
            }

            logger.LogInformation("Saved {Count} meme messages to the database.", totalSaved);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch messages from memes channel");
        }
    }
}
