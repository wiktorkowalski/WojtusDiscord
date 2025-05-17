using System.Text.Json;
using EventListenerService.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace EventListenerService.Services
{
    public interface IMemeMetadataGenerationService
    {
        Task GenerateMetadataForMemeAsync(MemeMessage message, CancellationToken cancellationToken = default);
        Task GenerateMetadataForMemesAsync(IEnumerable<MemeMessage> messages, CancellationToken cancellationToken = default);
    }

    public class MemeMetadataGenerationService : IMemeMetadataGenerationService
    {
        private readonly ILogger<MemeMetadataGenerationService> _logger;
        private readonly OpenAI.OpenAIClient _openAiClient;
        private readonly Data.WojtusContext _dbContext;

        // OpenAIClient is configured to use OpenRouter endpoint and API key

        public MemeMetadataGenerationService(
            ILogger<MemeMetadataGenerationService> logger,
            OpenAI.OpenAIClient openAiClient,
            Data.WojtusContext dbContext)
        {
            _logger = logger;
            _openAiClient = openAiClient;
            _dbContext = dbContext;
        }

        public async Task GenerateMetadataForMemeAsync(MemeMessage message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(message.ImageUrl))
            {
                _logger.LogWarning("No image URL found in message {MessageId}", message.MessageId);
                return;
            }

            // Download image as byte array
            byte[] imageBytes;
            using (var httpClient = new HttpClient())
            {
                imageBytes = await httpClient.GetByteArrayAsync(message.ImageUrl);
            }
            _logger.LogInformation("Downloaded image for message {MessageId}", message.MessageId);

            // Determine the image MIME type based on file signature
            string mimeType = DetermineImageMimeType(imageBytes);
            if (string.IsNullOrEmpty(mimeType))
            {
                _logger.LogWarning("Unable to determine MIME type for image in message {MessageId}", message.MessageId);
                mimeType = "image/jpeg"; // Fallback to default
            }

            string prompt = "You are an expert meme analyzer. Your task is to analyze this meme image and provide:\n1. A detailed description of what's in the image\n2. All text visible in the image (exactly as written)\n3. A list of 10-15 specific keywords/tags that would help someone find this meme\n4. Main objects/entities present in the image\n5. The overall tone or emotion of the meme (funny, sarcastic, etc.)\nProvide your analysis in a clear JSON format with these fields: description (string), textContent (string), keywords (string array), objects (string array), tone (string)\n";

            var chatClient = _openAiClient.GetChatClient("google/gemini-2.5-flash-preview");
            var chatMessages = new List<OpenAI.Chat.ChatMessage>
            {
                new SystemChatMessage(prompt),
                new UserChatMessage([
                    ChatMessageContentPart.CreateImagePart(new BinaryData(imageBytes), mimeType, ChatImageDetailLevel.Auto)
                ])
            };
            var response = await chatClient.CompleteChatAsync(chatMessages);
            // _logger.LogInformation("OpenAI response for message {MessageId}: {Response}", message.MessageId, response);
            
            var chatResponse = response.Value.Content.FirstOrDefault()?.Text;
            if (string.IsNullOrEmpty(chatResponse))
            {
                _logger.LogWarning("No response from OpenAI for message {MessageId}", message.MessageId);
                return;
            }
            
            // _logger.LogInformation("OpenAI response for message {MessageId}: {Response}", message.MessageId, chatResponse);

            try
            {
                // Clean the JSON response if it includes markdown formatting
                if (chatResponse.Contains("```json"))
                {
                    chatResponse = chatResponse.Split("```json")[1].Split("```")[0].Trim();
                }
                else if (chatResponse.Contains("```"))
                {
                    chatResponse = chatResponse.Split("```")[1].Split("```")[0].Trim();
                }
                
                var metadata = JsonSerializer.Deserialize<MemeMetadataResponse>(chatResponse, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if(metadata == null) 
                {
                    _logger.LogError("Failed to deserialize JSON response for message {MessageId}", message.MessageId);
                    return;
                }
                _logger.LogInformation("Saving metadata for message {MessageId}: {Metadata}", message.MessageId, metadata);
                _dbContext.MemeMetadata.Add(new MemeMetadata
                {
                    Id = Guid.NewGuid(),
                    MessageId = message.MessageId,
                    MemeMessage = message,
                    Description = metadata.Description,
                    TextContent = metadata.TextContent,
                    Keywords = metadata.Keywords,
                    Objects = metadata.Objects,
                    Tone = metadata.Tone
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to parse JSON response for message {MessageId}: {Error}", message.MessageId, ex.Message);
            }
        }

        public async Task GenerateMetadataForMemesAsync(IEnumerable<MemeMessage> messages, CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                await GenerateMetadataForMemeAsync(message, cancellationToken);
            }
        }

        // Add this helper method to detect the image type
        private string DetermineImageMimeType(byte[] bytes)
        {
            if (bytes.Length < 4) return string.Empty;
            
            // Check file signatures
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return "image/gif";
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
                return "image/bmp";
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
                return "image/webp";
                
            return string.Empty;
        }
    }
}

public class MemeMetadataResponse
{
    public string? Description { get; set; }
    public string? TextContent { get; set; }
    public string[]? Keywords { get; set; }
    public string[]? Objects { get; set; }
    public string? Tone { get; set; }
}