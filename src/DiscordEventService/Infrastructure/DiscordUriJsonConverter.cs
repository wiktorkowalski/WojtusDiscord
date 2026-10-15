using System.Text.Json;
using System.Text.Json.Serialization;
using DSharpPlus.Net;

namespace DiscordEventService.Infrastructure;

// DiscordUri has no public URL property, so the default serializer wrote embed image and thumbnail
// URLs as {"Type":0} (#413). Write-only: nothing deserializes a stored embed back into DSharpPlus types.
internal sealed class DiscordUriJsonConverter : JsonConverter<DiscordUri>
{
    public override DiscordUri Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("DiscordUri is serialized only.");

    public override void Write(Utf8JsonWriter writer, DiscordUri value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
