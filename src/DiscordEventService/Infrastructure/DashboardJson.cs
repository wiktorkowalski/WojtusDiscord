using System.Text.Json;

namespace DiscordEventService.Infrastructure;

// Builds the dashboard API's JsonSerializerOptions so tests can assert serialization through the exact same pipeline.
internal static class DashboardJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new SnowflakeJsonConverter());
        options.Converters.Add(new NullableSnowflakeJsonConverter());
    }

    public static JsonSerializerOptions CreateOptions()
    {
        // Mirror ASP.NET Core's defaults (camelCase) plus the snowflake converters.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}
