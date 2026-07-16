# Conversation memory (§5) + model-call retry policy, re-validated against the as-built loop

Research for ticket #258 (part of the wayfinder map #253 "conversational assistant harness
deepening" epic). Two AFK validations against the merged §1–§6 code:

1. **§5 memory** — does ADR-0006's conversation-store design (append-only, full turns incl.
   tool calls, token-aware sliding window, separate from ingestion) survive contact with the
   as-built loop? Can a *stored* turn faithfully reconstruct the `List<ChatMessage>` the loop
   replays (assistant `FunctionCallContent` + tool `FunctionResultContent`, in order, CallId
   paired)? Absorbs the scope of open issue #243.
2. **Retry policy** — enumerate OpenRouter's transient failure modes on the *streamed* path and
   pin retryable-ness, backoff, max attempts, and how a mid-stream retry interacts with
   already-yielded text deltas and the Discord bubble.

Read against `docs/adr/0006-conversational-assistant-meai-agentic-loop.md` (¶15 = the §5 design),
`CONTEXT.md → Conversational assistant`, and
`src/DiscordEventService/Services/Conversation/{ConversationService,OpenRouterChatOptions}.cs` +
`Services/EventHandlers/ConversationEventHandler.cs`. Every external claim is cited to a primary
source (MEAI/OpenAI .NET source on GitHub, learn.microsoft.com, OpenRouter docs) or to `file:line`.

---

## TL;DR

- **§5 was never built.** §1–§4 + §6 merged; §5 (conversation memory **and** the usage ledger)
  is entirely absent from the code. `ConversationService.GenerateReplyAsync` builds a fresh
  `[system, user]` list every turn (`ConversationService.cs:53-57`) — there is **no** store, **no**
  ledger entity, and `RecordRoundCost` only logs/traces the cost, persisting nothing
  (`ConversationService.cs:159-168`). So this is a greenfield build, not a revision; ADR-0006 ¶15
  and #243 still describe the target.
- **A stored turn *can* faithfully reconstruct the replay — but not by naively round-tripping
  `ChatMessage` JSON.** MEAI's `AIContent` is `[JsonPolymorphic(TypeDiscriminatorPropertyName =
  "$type")]` with stable discriminators (`functionCall`, `functionResult`), so the *types* survive
  a System.Text.Json round-trip. **The trap is `FunctionResultContent.Result`, typed `object?`:**
  our tools return `string`, and MEAI's OpenAI adapter has a `Result as string` fast-path that
  emits the raw string as the tool message — but after a JSON round-trip `Result` deserializes as a
  `JsonElement`, the fast-path misses, and the adapter JSON-re-serializes it, so the replayed tool
  message arrives **quote-wrapped and escaped**, differing on the wire from what the model saw
  live. **Fix: persist tool results as a plain text column and rehydrate `new
  FunctionResultContent(callId, text)` (a `string` again) — do not store an opaque
  `JsonSerializer.Serialize(chatMessage)` blob.** Verified against dotnet/extensions source
  (citations below).
- **The usage ledger cannot drive the sliding window.** OpenRouter reports `usage` per *request*
  (whole-prompt totals), never per stored message, and there is no local tokenizer for Anthropic
  models. Budget the window with a **local GPT-style token estimate** (OpenRouter normalizes counts
  to a GPT tokenizer by default, so `cl100k`/`o200k` is a fair proxy) plus a hard **last-M-turns**
  safety cap. Use the real `usage.cost`/tokens only for the ledger + observability.
- **Retry belongs around the per-round streamed call, not the whole turn.** Retryable: pre-stream
  `408/429/500/502/503/504` and transport faults; **and the OpenRouter mid-stream error frame** —
  an SSE `chat.completion.chunk` carrying a top-level `error` and `choices[0].finish_reason:
  "error"` (does **not** throw; looks like a normal empty completion unless you inspect the raw
  chunk). Not retryable: `400/401/402/403`. Exponential backoff, base ~1s, ×2, jitter, honor
  `Retry-After`, **max 3 attempts**. On a **mid-stream** failure, discard the round's partial
  deltas and **restart the round**, editing the in-flight Discord bubble down to the interim
  narration first (don't append a second copy of the answer). A retried round **re-bills** any
  server-side `web_search` it triggers — a known cost, not a bug.

---

# Part A — §5 conversation memory

## A0. As-built reality: §5 does not exist yet

The design in ADR-0006 ¶15 ("Conversation state is a new append-only store … recording full turns
*including* tool calls and tool results … replay is a token-aware sliding window") and in #243 was
written up-front and **has not been implemented**. Evidence in the merged code:

- **History is in-flight only.** `GenerateReplyAsync` opens every turn with a freshly built
  two-message list and never loads anything:
  ```csharp
  List<ChatMessage> messages =
  [
      new(ChatRole.System, BuildSystemPrompt(options)),
      new(ChatRole.User, userMessage ?? string.Empty),
  ];
  ```
  (`ConversationService.cs:53-57`). Within a turn it appends assistant + tool messages
  (`:92-93`, `:117-121`), but the list is a local that dies when the turn returns. A follow-up in
  the same thread starts from `[system, user]` again — the cross-turn amnesia #243 exists to fix.
- **No conversation entities.** `Data/Entities/` holds only `Core/` and `Events/` — there is no
  `Conversation`/`ConversationTurn`/`ConversationMessage` entity and no usage-ledger entity
  (directory listing; `DiscordDbContext` has no such `DbSet`).
- **The usage ledger is a comment, not code.** `RecordRoundCost` extracts `usage.cost` from the raw
  patch and *logs + traces* it, returning the number — it writes no row
  (`ConversationService.cs:159-168`, `ExtractCostUsd` `:170-182`). `OpenRouterChatOptions.cs:31-34`
  even labels the `usage:{include:true}` patch "Captured per round for the §5 usage ledger", but the
  capture sink was never built.

**Consequence:** #258 is greenfield. Nothing in the as-built code *contradicts* ADR-0006 ¶15 in a
way that needs rework — the loop's in-turn message-list discipline (assistant-then-tool, well
ordered) is exactly what the store must persist and replay. The mismatches in §A6 are gaps and
under-specifications, not conflicts.

## A1. Faithful reconstruction of the replay — verified against MEAI source

The loop maintains, per turn, a `List<ChatMessage>` shaped like:

```
[ system,
  user,
  assistant( [ TextContent?, FunctionCallContent(id=c1), FunctionCallContent(id=c2) ] ),
  tool( [ FunctionResultContent(callId=c1, "…") ] ),
  tool( [ FunctionResultContent(callId=c2, "…") ] ),
  assistant( [ TextContent("final answer") ] ) ]
```

built at `ConversationService.cs:53-57` (system+user), `:92-93` (`sink.ToChatResponse()` →
`messages.AddRange(response.Messages)` for the assistant turn, which MEAI assembles from the streamed
fragments — the loop deliberately never reassembles tool-call fragments by index), and `:117-121`
(one `new ChatMessage(ChatRole.Tool, [result])` per tool call). The tool result object is
`new FunctionResultContent(call.CallId, result)` where `result` is the tool's return — **always a
`string`** in this codebase (every tool method returns `Task<string>`; the unknown-tool and
exception paths also assign strings — `ConversationToolset.InvokeAsync`,
`ConversationToolRegistry.cs:527-569`, result built at `:569`).

For a stored turn to reconstruct this, three things must round-trip: the **concrete content type**,
the **CallId pairing**, and the **tool-result payload byte-for-byte on the wire**.

### Type discriminators round-trip (confirmed)

`AIContent` is polymorphic with a stable `$type` discriminator:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FunctionCallContent), typeDiscriminator: "functionCall")]
[JsonDerivedType(typeof(FunctionResultContent), typeDiscriminator: "functionResult")]
[JsonDerivedType(typeof(TextContent), typeDiscriminator: "text")]
[JsonDerivedType(typeof(TextReasoningContent), typeDiscriminator: "reasoning")]
[JsonDerivedType(typeof(UsageContent), typeDiscriminator: "usage")]
… (24 derived types total)
public class AIContent
```
Source: dotnet/extensions `Microsoft.Extensions.AI.Abstractions/Contents/AIContent.cs`
(<https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/Contents/AIContent.cs>).
`AIJsonUtilities.DefaultOptions` is the serializer options that carry these contracts (source-gen'd
for the abstractions exchange types, string enums, `WhenWritingNull`):
<https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aijsonutilities.defaultoptions>.
So `JsonSerializer.Serialize(chatMessage, AIJsonUtilities.DefaultOptions)` → deserialize gives back
a `ChatMessage` whose `Contents` are the correct concrete subtypes. **Types are safe.**

### CallId pairs round-trip (confirmed, with a rehydration caveat)

`FunctionResultContent.CallId` is inherited from `ToolResultContent` and is a plain serialized
`string`; `FunctionCallContent` carries `CallId`, `Name`, `Arguments`. Persist both `CallId`s and the
OpenAI adapter emits assistant `tool_calls[].id == CallId` and tool `tool_call_id == CallId`, so the
pairing the provider requires holds as long as **the same CallId strings are stored and restored on
both sides**. (Anthropic-over-OpenRouter ids look like `toolu_…`; they are opaque — store verbatim.)

### The `FunctionResultContent.Result` trap (confirmed — this is the load-bearing finding)

`FunctionResultContent.Result` is declared `public object? Result { get; set; }` with **no**
converter and no `JsonPropertyName`; `Exception` is `[JsonIgnore]` and returns null after
deserialization. Source: dotnet/extensions
`Microsoft.Extensions.AI.Abstractions/Contents/FunctionResultContent.cs`.

MEAI's OpenAI adapter turns a `FunctionResultContent` into a `ToolChatMessage` like this:

```csharp
string? result = resultContent.Result as string;
if (result is null && resultContent.Result is not null)
{
    try
    {
        result = JsonSerializer.Serialize(resultContent.Result,
            AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
    }
    catch (NotSupportedException) { /* skip */ }
}
yield return new ToolChatMessage(resultContent.CallId, result ?? string.Empty);
```
Source: dotnet/extensions `Microsoft.Extensions.AI.OpenAI` chat message conversion
(`ToOpenAIChatMessages`), <https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.OpenAI>.

- **Live path (in-turn today):** `Result` is a `string` → `Result as string` succeeds → the wire
  tool message content is the raw string (`Top 5 posters: …`).
- **Round-tripped-through-JSON path:** `object?` deserializes a JSON string as a **`JsonElement`**
  (`ValueKind.String`), so `Result as string` returns **null**, the code falls to
  `JsonSerializer.Serialize(jsonElement)`, and the wire tool message becomes the **quoted, escaped**
  form (`"Top 5 posters: …"` with `\n`→`\\n` etc.). Same information, **different bytes** — the model
  replays a subtly different tool result than it originally saw.

This is why "just `JsonSerializer.Serialize(chatMessage)` into a `jsonb` column and deserialize back"
is **not** a faithful store. Two ways out, recommend the first:

1. **Normalized columns (recommended).** Store the tool result as a plain `text` column; rehydrate
   `new FunctionResultContent(callId, name, storedText)` so `Result` is a `string` and the fast-path
   fires. This also dodges the trap for `FunctionCallContent.Arguments` (a `IDictionary<string,
   object?>` whose values also deserialize to `JsonElement` — harmless for arguments because the
   adapter always JSON-serializes them, but cleaner to reconstruct from stored raw-JSON arguments).
2. **Blob + post-fix.** Store the polymorphic blob, then after deserialize walk the contents and
   rewrite any `FunctionResultContent` whose `Result is JsonElement { ValueKind: String } e` back to
   `e.GetString()`. Works, but it re-introduces the exact fragility the normalized schema avoids and
   is easy to forget for the next content type.

**Confidence:** high on the mechanism (all three facts read from current dotnet/extensions source).
Not empirically reproduced end-to-end in this session — worth a one-shot integration test (persist a
tool round, rehydrate, capture the wire body via a logging transport, assert the tool message equals
the live one) when §5 is built.

### Reasoning content — an open fidelity question

A streamed round may include `TextReasoningContent` (`$type:"reasoning"`) in the assembled assistant
message. The in-turn loop already carries whatever `sink.ToChatResponse().Messages` contains across
rounds, so if reasoning is present it is *already* being replayed within a turn and works. For
cross-turn memory the question is whether Anthropic-over-OpenRouter **requires** the reasoning/thinking
block (and its signature) to be echoed back with the tool-use turn, and whether that signature stays
valid once persisted for hours/days. Flag as an open question (A-OQ2) — the safe default is to persist
reasoning content faithfully but be ready to strip it on replay if the provider rejects stale
signatures.

## A2. Proposed store schema (ADR-0002 grain)

Two append-only tables, snowflake-keyed conversation + one row per persisted `ChatMessage`, matching
the codebase conventions: `Guid Id` PK defaulted to `uuidv7()` (every entity gets this via
`UuidV7Extensions.ConfigureUuidGeneration` — `Data/UuidV7Extensions.cs:7-15`), `ulong` Discord
snowflakes, `snake_case` columns (EFCore.NamingConventions), `jsonb` for structured blobs (precedent:
`MemeIndexEntity.RawResponseJson`, `MemeIndexEntityConfiguration.cs:40`), UTC timestamps.

**`conversation`** — one row per thread / DM channel (the Snowflake is the natural conversation key):

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | `uuidv7()` default |
| `channel_discord_id` | `bigint` | thread or DM channel snowflake — **unique** (the conversation key) |
| `guild_discord_id` | `bigint?` | null for a DM |
| `created_at_utc` | `timestamptz` | |
| `last_activity_at_utc` | `timestamptz` | for pruning / ordering |

**`conversation_message`** — one row per `ChatMessage` the loop appended, append-only, ordered:

| column | type | notes |
|---|---|---|
| `id` | `uuid` PK | `uuidv7()` — also gives natural insertion order |
| `conversation_id` | `uuid` FK → `conversation.id` | `OnDelete(Restrict)` (append-only; never cascade-delete history) |
| `turn_index` | `int` | monotonic per conversation; the model turn a message belongs to (user+its assistant/tool rounds share intent but each message is its own row) |
| `role` | `int`/`text` | user \| assistant \| tool (persisted enum — never renumber, per the `MemeIndexStatus` convention `MemeIndexEntity.cs:5-12`) |
| `text` | `text?` | the message's `TextContent` (may be null for a pure tool-call assistant message) |
| `tool_calls` | `jsonb?` | assistant only: array of `{ id, name, arguments_json }` — raw JSON args, not a re-serialized dict |
| `tool_call_id` | `text?` | tool role only: the `CallId` this result answers |
| `tool_name` | `text?` | tool role only |
| `tool_result` | `text?` | tool role only: the tool's **raw string** (rehydrate `Result` as `string`) |
| `reasoning` | `text?`/`jsonb?` | optional; see A-OQ2 before relying on replay |
| `prompt_tokens` / `completion_tokens` | `int?` | provider-reported for the round that produced this message (assistant rows); null otherwise |
| `est_tokens` | `int` | locally estimated size, the **window budget currency** (see A4) |
| `created_at_utc` | `timestamptz` | |

Design notes:
- **Grain = one row per `ChatMessage`, not per tool call.** An assistant message can bundle text +
  N `FunctionCallContent` (`ConversationService.cs:96-99` collects them with
  `SelectMany(...).OfType<FunctionCallContent>()`), and each tool result is already its own
  `ChatMessage` (`:120`). So `tool_calls` is a `jsonb` array on the assistant row; tool results are
  separate `role=tool` rows. This is a small refinement of #243's `(role, content, tool_call_id,
  name)` sketch, which reads as if one row = one tool call.
- **Separate from ingestion** (ADR-0006 ¶15, CONTEXT.md three-senses-of-message): these tables are
  distinct from `messages`/`message_events`; the bot's own replies still ingest normally.
- **Durability:** persist each new message **awaited, inside the turn** (not fire-and-forget) — #243
  AC calls this out explicitly ("a restart mid-thread loses nothing"; the sibling bots drop rows on
  crash). The natural seam is right after each `messages.Add*` in the loop.

## A3. Where replay hooks into `GenerateReplyAsync`

Two edits to `ConversationService.GenerateReplyAsync`, both small:

1. **Rehydrate at the top.** Replace the fixed `[system, user]` seed (`:53-57`) with
   `[system, ...window, user]`, where `window` is the token-windowed, chronologically ordered
   reconstruction of the conversation's stored messages (A4). The reconstruction maps each stored
   row back to a `ChatMessage`:
   - `role=user/assistant text` → `new ChatMessage(role, text)`
   - assistant with `tool_calls` → `new ChatMessage(ChatRole.Assistant, [ TextContent(text)?,
     ..FunctionCallContent(id, name, args).. ])`
   - `role=tool` → `new ChatMessage(ChatRole.Tool, [ new FunctionResultContent(tool_call_id,
     tool_name, tool_result_string) ])` — **`Result` is the raw string** (A1).
   This needs the conversation key (the reply-surface channel) — already available: the handler
   passes `context.ChannelId` (`ConversationEventHandler.cs:74-79`), which for a bot-owned thread /
   DM is exactly the conversation Snowflake.
2. **Persist as the loop appends.** The loop already builds the correct in-turn message list; add a
   durable write at each append point: the user message once at entry; the assistant turn right after
   `messages.AddRange(response.Messages)` (`:93`); each tool result right after
   `messages.Add(new ChatMessage(ChatRole.Tool, [result]))` (`:120`); and the forced final answer in
   the round-cap tail (`:132-137`). Awaited, so a crash mid-turn leaves a consistent prefix.

**DI note (build constraint):** `ConversationService` is resolved in the **DSharpPlus child
container**; a conversation-store service + its `DbContext`/`DbSet` must be dual-registered via
`CoreServiceRegistration.CoreServiceTypes` (root + child), or it won't resolve in the handler — the
same rule the epic already calls out and that `ConversationRegistration` follows for every other
conversational service (`ConversationRegistration.cs:49-81`). The store is a **scoped** service over
the request `DbContext` (like `MemeSearchService`), captured into the loop the same way the tool
registry captures scoped services.

## A4. The sliding window and its token budget

**The usage ledger cannot size the window.** OpenRouter's `usage` is per *request* (whole-prompt
`prompt_tokens` + `completion_tokens`), never per stored message — you only get an exact per-message
number for the *assistant completion* of each round, never for user or tool messages, and never for
messages you haven't sent yet. So "count tokens from the ledger" doesn't answer "how many stored
messages fit in N tokens *before* I call".

**There is no local exact tokenizer for Anthropic models.** Anthropic ships no offline tokenizer;
its count-tokens API is a network round-trip. OpenRouter, by default, **normalizes** reported token
counts to a common GPT tokenizer (native counts are opt-in via usage accounting), so a **local
GPT-style estimate** (`cl100k`/`o200k` via SharpToken/Tiktoken, or even a `chars/4` heuristic) is a
*fair proxy* for how OpenRouter will count the prompt — good enough to budget a window, never exact.

**The window design cannot assume any pre-existing ledger rows.** The ledger does not exist yet
(§A0) — `RecordRoundCost` only logs/traces per round and its own comment admits the sink is unbuilt:
"Logged + traced per round here; **the §5 usage ledger persists it**" (`ConversationService.cs:156-158`).
So the window's token budget must be computed by §5 itself at write time (the `est_tokens` column
below), not read back from a cost table that a prior turn supposedly populated. Even once the ledger
exists, it holds per-*request* rows, not the per-*message* sizes the window needs (above).

**Recommendation:** compute `est_tokens` per message on write with a local GPT-style tokenizer; the
window is "walk stored messages newest→oldest, sum `est_tokens`, stop at the budget", then prepend
`system` and append the new `user`. Add a hard **last-M-turns** cap as a cheap safety net (#243 lists
"budget N tokens / last M turns" — do **both**: token budget primary, turn cap as a backstop against a
few huge tool dumps blowing the estimate). Keep the real provider `usage` for the ledger + Langfuse
only. Never split a tool-call/tool-result pair across the window boundary — if an assistant message
with `tool_calls` is included, its matching `role=tool` rows must be too, or the provider rejects the
dangling `tool_call_id`.

## A5. Usage ledger

Foundation is already half-there: `RecordRoundCost` computes per-round `usage.cost` and tokens; it
just needs a sink. Record **per round** (not per turn — a turn is multiple rounds): `conversation_id`,
`turn_index`, `round`, `model`, `prompt_tokens`, `completion_tokens`, `cost_usd` (from
`ExtractCostUsd`, `ConversationService.cs:170-182`), `latency_ms`, `created_at_utc`. Leave the
guard seam for soft caps (#253: alert-only, never refuse yet) — a per-user/global sum query over this
table is the enforcement point later.

**Failed/partial rounds (ties into Part B):** a round that errors mid-stream and is retried should
still be **observable** — either record the failed attempt with `cost_usd=0`/null and an `error`
flag, or at minimum the succeeding retry. Note that OpenRouter **may bill** a mid-stream failure
partially (tokens were generated before the error), and a **retried** round that re-triggers a
server-side `web_search` **re-bills the search** ($0.005 Exa each; see the web-search note's #261
addendum) — so "cost per turn" is a sum over attempts, not just the winning round. The ledger should
not silently drop failed attempts or it will under-count real spend.

## A6. Mismatches / gaps vs ADR-0006 ¶15 (flagged explicitly)

1. **"recording full turns" is under-specified for the `object?` Result trap.** ADR-0006 says persist
   full turns incl. tool results; it does **not** warn that a JSON round-trip of
   `FunctionResultContent.Result` changes the wire bytes (A1). The epic must spec the normalized-text
   store (or the post-deserialize fix), or the "faithful replay" AC silently fails.
2. **"token-aware sliding window" implies the ledger sizes it — it can't.** ¶15 and #243 pair the
   ledger and the window as if usage rows drive replay; A4 shows windowing needs a *local estimate*
   because per-message usage isn't available. Not a contradiction, but a spec gap the epic must close
   (pick the estimator; pick the budget + turn-cap numbers).
3. **Grain: "ConversationTurn (role …, tool_call_id/name)" reads as one-row-per-tool-call.** The
   as-built loop bundles multiple `FunctionCallContent` in one assistant message (`:96-99`), so the
   faithful grain is one-row-per-`ChatMessage` with a `tool_calls` array on the assistant row (A2).
4. **Reasoning content persistence is unaddressed (A-OQ2).** ¶15 says "full turns including tool
   calls" but is silent on `TextReasoningContent` and whether stale thinking signatures replay.
5. **ADR-0006 ¶13 is already stale on an unrelated point (flag for #259, which amends ADR-0006).**
   The §6 implementation note asserts the action singletons "resolve the singleton `DiscordClient`
   (**forwarded into the DSharpPlus child container alongside `IChatClient`**)". That forwarding was
   **reversed by PR #251** (the §6 boot-deadlock fix, commit `4bbcc86`): forwarding `DiscordClient`
   into the child container re-enters its own construction and deadlocks boot, so the client is now
   reached through a plain `DiscordClientAccessor` holder and is **never** registered in the child
   container (`ConversationRegistration.cs:37-39, 71-74`; `DiscordClientAccessor.cs`). Out of #258's
   core scope, but #259 should correct this line then, and while there, the ¶15 §5 gaps above.
6. **No conflict to rework.** Nothing in §1–§6 fights the design — the in-turn message list is
   exactly the shape to persist. §5 is a clean add, not a retrofit.

---

# Part B — Model-call retry policy

The settled decision (#253): "retry transient model-call failures with backoff, **resume the round**;
visible failure message when retries exhaust. Tool errors keep flowing back to the model (already
built)." This section pins the mechanics against the streamed path.

## B1. Failure modes on the streamed path

The conversation path streams through the stock OpenAI-SDK-backed `IChatClient`
(`chatClient.GetStreamingResponseAsync(messages, roundOptions, ct)`, `ConversationService.cs:70`).
Failures surface in **two distinct places**:

**(i) Before the stream starts** — connection/HTTP status is known before any `data:` chunk. The
OpenAI SDK throws `ClientResultException` (with `.Status`) for a non-2xx, or a transport exception
(`HttpRequestException`/`TaskCanceledException`) for a dropped/timed-out connection. OpenRouter's
`error` body shape is `{ error: { code, message, metadata? } }` and the HTTP status equals
`error.code` (<https://openrouter.ai/docs/api-reference/errors>).

**(ii) Mid-stream** — once headers commit, "the HTTP 200 OK status and headers are already committed
— they can't be changed", so OpenRouter delivers an error as a normal SSE `data:` chunk:

```jsonc
{ "object": "chat.completion.chunk",
  "error": { "code": 502, "message": "…", "metadata": { "error_type": "…", "provider_code": "…" } },
  "choices": [ { "index": 0, "delta": { "content": "" }, "finish_reason": "error" } ] }
```
Source: <https://openrouter.ai/docs/api-reference/errors> ("Mid-stream error delivery" — top-level
`error` alongside standard fields, `finish_reason: "error"`, stream ends after this event). The
repo's own build-constraint note already flags this ("Watch for OpenRouter mid-stream error frames
arriving as normal data chunks", epic #238). **Critically, this chunk does not throw** — the OpenAI
SDK's chunk model has no typed `error` field, so it deserializes as a normal update with empty
content and a (non-standard) finish reason. Unless the app inspects the raw chunk, a mid-stream error
looks like a **successful empty completion** — the loop would treat it as "a round with no tool calls
and no text" and fire the `EmptyAnswerFallback` (`ConversationService.cs:101-104`) instead of
retrying. Detection is the crux (B3).

| # | Failure mode | Where it surfaces | Retryable? | Notes |
|---|---|---|---|---|
| 1 | `429` rate limited | pre-stream throw | **Yes** | honor `Retry-After` + `X-RateLimit-Reset` headers (limits doc) |
| 2 | `500` internal | pre-stream throw | **Yes** | masked message |
| 3 | `502` model down / invalid upstream response | pre-stream throw **or** mid-stream frame | **Yes** | most common transient; provider hiccup |
| 4 | `503` no provider meets routing | pre-stream throw | **Yes** (short) | with `allow_fallbacks:false` (`OpenRouterChatOptions.cs:24-25`) OpenRouter can't reroute — retrying the pinned provider may keep failing; cap attempts |
| 5 | `408` request timeout | pre-stream throw | **Yes** | |
| 6 | `504` gateway timeout | pre-stream throw | **Yes** | |
| 7 | Transport fault (conn reset, DNS, TLS) | `HttpRequestException`/`TaskCanceledException` | **Yes** | mirrors the meme client's transport branch (`OpenRouterClient.cs:90-94`) |
| 8 | **Mid-stream `error` frame** (`finish_reason:"error"`) | SSE data chunk, **no throw** | **Yes** | must detect via raw chunk (B3); restart the round |
| 9 | Client-side turn timeout | `OperationCanceledException` from the turn `cts` (`ConversationEventHandler.cs:68,119`) | **No** | user-facing budget exhausted, not a provider fault — don't retry |
| 10 | `400` bad request (malformed body/params) | pre-stream throw | **No** | our bug; retry won't help |
| 11 | `401` bad key | pre-stream throw | **No** | config error; `IsConfigured` gate should preempt (`ConversationService.cs:39-43`) |
| 12 | `402` insufficient credits | pre-stream throw | **No** | needs top-up, not retry (limits doc); a good soft-cap alert trigger |
| 13 | `403` moderation/guardrail block | pre-stream throw | **No** | content refused; surface, don't retry |

Retryable classification anchors to the codebase's existing convention — the meme client already
treats `408/429/≥500` as transient and `4xx` as terminal (`OpenRouterClient.cs:100-103`). Reuse that
predicate; **extend it with the mid-stream frame** (row 8), which the meme client (non-streaming)
never had to handle.

## B2. Backoff shape, attempts, jitter

- **Exponential backoff, base ≈ 1s, factor 2, full jitter** (`delay = random(0, base·2^attempt)`),
  capped ≈ 8–10s. OpenRouter's own guidance: "Retry with exponential backoff. Rate limits are
  transient; wait and retry rather than immediately re-sending. Honor the `Retry-After` header when
  present." (<https://openrouter.ai/docs/api-reference/limits>).
- **Honor `Retry-After` / `X-RateLimit-Reset`** on a 429 — use the header value in place of the
  computed backoff when it's larger.
- **Max 3 attempts** per round (1 initial + 2 retries). Rationale: the whole turn already runs under
  a hard `RequestTimeoutSeconds` (default 120s, `ConversationOptions.cs:59`) enforced by the handler
  `cts` (`ConversationEventHandler.cs:68`); unbounded retries would just burn that budget and stack
  latency. 3 attempts × (call + backoff) fits comfortably and covers the overwhelmingly common
  single-hiccup case. Make attempts/base/cap `ConversationOptions` knobs.
- **No new dependency required, but Polly is the clean option.** The repo has **no** Polly /
  `Microsoft.Extensions.Http.Resilience` package today (csproj has only `OpenAI`,
  `Microsoft.Extensions.AI[.OpenAI]`); the only retry precedent is EF's
  `EnableRetryOnFailure(maxRetryCount:3, maxRetryDelay:5s)` for Postgres (`Program.cs:65-74`). A
  hand-rolled `for`-loop with `Task.Delay(jittered)` around the round is enough and keeps the
  dependency surface flat; adding `Microsoft.Extensions.Http.Resilience` is justifiable if you want
  declarative policy + telemetry. Either way the retry must sit **inside** the app loop, not as an
  `HttpClient` `DelegatingHandler`, because a mid-stream failure (row 8) is not an HTTP-status failure
  the handler pipeline can see — it's a data chunk (B3).

## B3. Where the retry wrapper sits — per round, and how to detect the mid-stream frame

**Per-round, around the streamed call — not around the whole turn.** The loop's unit of model work
is `StreamRoundAsync` (`ConversationService.cs:67-78`), invoked once per round (`:85`) and once for
the round-cap final answer (`:133`). Wrapping the *turn* would replay already-succeeded rounds
(re-dispatching tools, re-billing them); wrapping the *round* re-runs only the failed model call.
"Resume the round" (#253) = re-issue that one `GetStreamingResponseAsync` with the **same** `messages`
prefix (which is intact — the failed round appended nothing durable yet).

**Detection is the hard part** because the two failure surfaces differ:
- **Pre-stream throw** (rows 1–7, 10–13): catch `ClientResultException`/transport exceptions around
  the `await foreach`, classify by `.Status` with the meme-client predicate.
- **Mid-stream frame** (row 8): inspect each streamed update's raw representation for the error —
  the same experimental-`Patch` recovery already used for `usage.cost` and web-search annotations.
  On the streaming path `ChatResponseUpdate.RawRepresentation is StreamingChatCompletionUpdate raw`;
  check `raw.Patch.TryGetValue("$.error", …)` (or `raw.FinishReason`/`$.choices[0].finish_reason ==
  "error"`). If seen, **throw a synthetic transient exception out of `StreamRoundAsync`** so the
  same per-round retry wrapper handles it uniformly. **Do not** let the frame fall through as an
  empty completion — that misfires `EmptyAnswerFallback` (`:101-104`).

**Confidence:** the frame *shape* is primary-sourced (OpenRouter errors doc); the exact way the
**OpenAI .NET SDK** surfaces a chunk carrying `error`+`finish_reason:"error"` (silent empty update
vs. a thrown exception vs. a mapped `ChatFinishReason`) was **not** empirically confirmed here —
verify against a live stream (or a fault-injecting fake) when building, and pick the detection path
that matches. This is B-OQ1.

## B4. Mid-stream retry vs. already-yielded deltas and the Discord bubble

A mid-stream failure is the tricky case: the round may already have `yield return`ed
`AssistantTextDelta`s that the handler flushed into a live Discord message
(`ConversationEventHandler.cs:94-97` → `DiscordStreamingMessage.AppendDeltaAsync`). If you just
restart the round, the retry streams the answer text **again**, and a naive handler would **append a
second copy** below the partial first one.

**Recommendation: restart the round and reset the bubble, don't append.** Concretely:
- The retry happens **inside** `StreamRoundAsync`/its wrapper, *before* the loop decides what the
  round produced — so retries are invisible to the loop's message-list bookkeeping (nothing was
  appended to `messages` yet on a failed round).
- For the **user-visible bubble**, add a render event (e.g. `ConversationUpdate.RoundReset`) that the
  loop yields when it discards a partial round, and have the handler **rewind the current
  `DiscordStreamingMessage`** — clear its buffer back to the pre-round state (the interim narration,
  if any) and re-edit the message down, so the retried stream overwrites rather than appends.
  `DiscordStreamingMessage` already edits a single message in place and re-renders only changed
  chunks (`DiscordStreamingMessage.cs:55-84`), so shrinking the buffer + re-flushing is a natural
  extension (it currently only ever grows — add a "truncate to N and re-render" path).
- **Simpler acceptable fallback:** don't start the bubble until the round *completes*. But that
  loses the live-streaming cadence that is the whole point of §3, so prefer the rewind. Given the
  retry budget is small and mid-stream errors are rare, a brief flicker (answer starts, resets,
  restarts) is acceptable UX.
- **If retries exhaust:** edit the bubble to a visible failure line (#253: "visible failure message
  when retries exhaust") — e.g. a localized "coś się wysypało, spróbuj ponownie" — rather than the
  silent `EmptyAnswerFallback`. This is a new terminal `ConversationUpdate` the handler renders.

**Idempotency caveat:** this "restart the round" story is clean for a *read* round (re-streaming text
is free of side effects) and for a round whose tools haven't dispatched yet (tools dispatch *after*
the model round returns, `:117-121`, so a failed model round has run no tools). It is **not** a
concern for §6 action tools, because those never execute inside the model call — the model only
*requests* them, and irreversible ones stage behind a confirm button anyway. So per-round retry
carries no double-action risk.

## B5. Interaction with the usage ledger and server-side web_search

- **Failed/partial rounds cost money.** A round that streams some tokens then errors may be partially
  billed by OpenRouter, and each retry is a fresh billable request. The ledger (A5) must record
  attempts, not just the winning round, or spend is under-counted.
- **A retried round re-bills any server-side `web_search`.** Per the web-search note (#261 addendum,
  `docs/research/web-search-over-openrouter.md`): the `openrouter:web_search` server tool bills
  ~$0.005 (Exa) *per search the model runs in that request*, and searches don't persist into replayed
  history — so a retried round that searches again pays again. Bounded and acceptable at this scale,
  but the soft-cap alerting (#256) should sum over attempts.
- **Mid-stream retry does not re-dispatch app tools** (they run after the round, B4 idempotency
  note), so no tool is double-executed by a model-call retry.

---

## What the epic should spec

**Memory (§5):**
1. **Normalized store, not a JSON blob.** Tool results as a plain `text` column; rehydrate `new
   FunctionResultContent(callId, name, text)` so `Result` is a `string` — this is the difference
   between faithful and quote-wrapped replay (A1). Schema per A2 (`conversation` +
   `conversation_message`, uuidv7 PK, snowflake conversation key, snake_case, append-only, `jsonb`
   tool-call array, persisted enum for role).
2. **Replay hook.** Rehydrate `[system, ...window, user]` at `ConversationService.cs:53-57`; persist
   each message durably (awaited) at the loop's existing append points (`:93`, `:120`, `:132-137`).
   Conversation key = `context.ChannelId`.
3. **Window budget.** Local GPT-style token estimate as the currency (the ledger can't size the
   window); token budget primary + last-M-turns backstop; never split a tool-call/result pair across
   the boundary. Pick the numbers.
4. **Usage ledger.** Per-round rows (tokens, `usage.cost`, latency); record failed/retried attempts;
   leave the soft-cap seam.
5. **DI.** Dual-register the store service + `DbContext` access via `CoreServiceRegistration`
   (root + DSharpPlus child), scoped over the request `DbContext`.
6. **Integration test** (Testcontainers, no DB mock): persist a turn with a tool round → rehydrate →
   capture the wire body via a logging transport → assert the replayed tool message is byte-identical
   to the live one (this is the test that would have caught the `object?` trap).

**Retry:**
7. **Per-round retry wrapper** around `StreamRoundAsync`, reusing the meme client's transient
   predicate (`408/429/≥500`) **extended** with the mid-stream `error` frame surfaced via
   `StreamingChatCompletionUpdate.Patch` (`$.error` / `finish_reason:"error"`), thrown as a synthetic
   transient so one code path handles both surfaces.
8. **Backoff:** exponential, base ~1s, ×2, full jitter, honor `Retry-After`; **max 3 attempts**;
   knobs on `ConversationOptions`. Turn stays under `RequestTimeoutSeconds`.
9. **Bubble semantics on mid-stream retry:** add a `RoundReset` render event; the handler rewinds the
   current `DiscordStreamingMessage` (extend it with a truncate-and-re-render path) so the retried
   stream overwrites rather than appends. On exhaustion, a visible localized failure message (not the
   silent empty fallback).
10. **Ledger records attempts,** and soft-cap alerting sums over attempts (mid-stream failures +
    web_search re-bills cost money).

---

## Open questions

- **A-OQ1 (verify when building):** the `FunctionResultContent.Result` `object?→JsonElement` trap is
  read from current dotnet/extensions source but not reproduced end-to-end this session — confirm
  with the round-trip integration test (spec item 6).
- **A-OQ2:** does Anthropic-over-OpenRouter require `TextReasoningContent` (and a valid thinking
  signature) to be echoed back with a persisted tool-use turn, and does that signature survive being
  stored for hours/days? Decides whether the store persists+replays reasoning or strips it.
- **A-OQ3:** exact window numbers (token budget, last-M) and estimator choice (SharpToken `o200k` vs
  `chars/4`) — a 10-minute calibration against real thread transcripts once §5 exists.
- **B-OQ1 (verify when building):** how the OpenAI .NET SDK surfaces a chunk carrying
  `error`+`finish_reason:"error"` — silent empty update, thrown exception, or a mapped non-standard
  `ChatFinishReason`. Determines whether detection reads `raw.Patch["$.error"]` or `raw.FinishReason`.
  Verify against a live/fault-injected stream.
- **B-OQ2:** does OpenRouter bill a mid-stream-failed request for the tokens generated before the
  error? Affects whether the ledger should record a cost for failed attempts or just count them.
- **B-OQ3:** with `allow_fallbacks:false` pinned to Anthropic (`OpenRouterChatOptions.cs:24-25`), a
  `503`/`502` may be a sticky provider outage that retrying the *same* provider won't clear — decide
  whether the retry policy should (temporarily, for a failing turn) relax the pin to let OpenRouter
  reroute, or just fail fast to the visible-failure message. (Relaxing the pin changes model identity
  mid-turn — probably not worth it; lean fail-fast.)
