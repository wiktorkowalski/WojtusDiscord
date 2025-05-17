using EventListenerService.Models;
using Microsoft.Extensions.Logging;

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

        // OpenAIClient is configured to use OpenRouter endpoint and API key

        public MemeMetadataGenerationService(
            ILogger<MemeMetadataGenerationService> logger,
            OpenAI.OpenAIClient openAiClient)
        {
            _logger = logger;
            _openAiClient = openAiClient;
        }

        public async Task GenerateMetadataForMemeAsync(MemeMessage message, CancellationToken cancellationToken = default)
        {
            
        }

        public async Task GenerateMetadataForMemesAsync(IEnumerable<MemeMessage> messages, CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                await GenerateMetadataForMemeAsync(message, cancellationToken);
            }
        }
    }
}
