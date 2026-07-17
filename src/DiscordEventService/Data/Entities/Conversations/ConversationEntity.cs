namespace DiscordEventService.Data.Entities.Conversations;

// One conversation per thread / DM channel (#267, ADR-0006 ¶15) — the channel
// snowflake is the natural conversation key. Deliberately separate from the
// ingestion tables (messages/message_events): this is the assistant's replay
// store, not an ingested event (CONTEXT.md, three senses of "message").
public class ConversationEntity
{
    public Guid Id { get; set; }

    // Thread or DM channel snowflake — unique; the conversation key the handler
    // already passes as ConversationContext.ChannelId.
    public ulong ChannelDiscordId { get; set; }

    // Null for a DM.
    public ulong? GuildDiscordId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // Bumped on every persisted message; pruning/ordering handle.
    public DateTime LastActivityAtUtc { get; set; }
}
