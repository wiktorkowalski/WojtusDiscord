# Web search for the conversational assistant, over OpenRouter

Research for ticket #254. Question: how should the §-238 conversational assistant get web
search, comparing (1) OpenRouter's **web plugin** (`plugins: [{"id":"web"}]` / `:online`
suffix) against (2) exposing search as a **normal app-driven tool** in the hand-driven
agentic loop.

All wire-mechanism, pricing, injection-path and citation-format claims are cited to
primary sources (OpenRouter docs, OpenAI .NET SDK public API surface) below. Read against
`docs/adr/0006-conversational-assistant-meai-agentic-loop.md` and
`src/DiscordEventService/Services/Conversation/{OpenRouterChatOptions,ConversationService}.cs`.

---

## TL;DR — recommendation

> **Superseded by the [#261 addendum](#addendum-261--the-openrouterweb_search-server-tool-spiked-live)
> (2026-07-16): use the `openrouter:web_search` *server tool*, not the plugin.** The live
> spike confirmed it composes with the hand-driven loop and app tools on the streaming
> path, needs no per-round options refactor, and its cost lands in `usage.cost`. The
> plugin analysis below stands as the fallback if the server tool ever regresses.

**Use the OpenRouter `web` plugin, injected via the existing `JsonPatch` helper, and
attach it selectively (not on every loop round).** It is the only option that needs *no*
new provider, no new API key, and no new HTTP client — it reuses ADR-0005's "OpenRouter is
the single provider" and ADR-0006's "keep the wire out of app code" thesis, and it slots
into `OpenRouterChatOptions.BuildRawRepresentation` as literally one more `Patch.Set` line
next to the `provider`/`reasoning`/`usage` patches already there. The one real hazard is
cost: the `web` plugin runs a search **once per request it is attached to**, so attaching
it to every round of the hand-driven tool loop bills up to `MaxToolRounds` searches per
turn. Attach it conditionally so a turn bills at most one `$0.005` Exa search — which
as-built requires a small refactor (per-round options construction inside the loop; neither
existing `Create` call site works — see (a)) that the epic must spec. The plain app-owned Exa/Brave/Tavily tool gives finer per-call control
and true model-decides-when semantics, but it costs a new provider + key + client +
citation plumbing — exactly the wire work ADR-0006 chose MEAI to avoid — so it is the
*alternative*, not the default. If model-controlled search without app plumbing is wanted,
evaluate OpenRouter's newer **`openrouter:web_search` server tool** (open question C below).

Key facts: Exa engine **$0.005/request** (≤10 results) + $0.001/extra result, default
`max_results=5`; `:online` == `plugins:[{"id":"web"}]` exactly; `plugins` is a top-level
body field so the `$.plugins` JsonPatch works; citations come back as OpenAI-standard
`url_citation` **annotations**, which the OpenAI .NET SDK exposes typed only on
*non-streaming* `ChatCompletion` — on the **streaming** path the app uses they must be
recovered via `StreamingChatCompletionUpdate.Patch.TryGetValue("$.annotations", …)`, the
same experimental-JsonPatch trick already used for `usage.cost`.

---

## (a) Loop / streaming / cost composition

**Composes with a hand-driven tool loop, and works with streaming.** The plugin is nothing
more than a top-level `plugins` field on the chat-completions request body. The app's loop
re-sends the full `messages` list each round and calls
`chatClient.GetStreamingResponseAsync(messages, roundOptions, …)`
(`ConversationService.cs:70`); adding `plugins` to `roundOptions` changes nothing about how
the loop is driven or streamed. OpenRouter documents the plugin on the standard chat
completions request and the `:online` shortcut on the model slug, both fully compatible with
streaming responses.
Source: <https://openrouter.ai/docs/features/web-search> — "`:online` … is a shortcut for
using the `web` plugin, and is exactly equivalent to" `plugins: [{ "id": "web" }]`.

**But the `web` plugin fires a search once per request it is attached to — this is the cost
trap.** OpenRouter contrasts the always-on plugin with the newer *server tool*: "Server
tools give the model control over when and how often to search, rather than always running
once per request." So the `web` plugin = search-runs-once-per-request; if it is attached to
every round of a multi-round tool loop, every round bills a search.
Source: <https://openrouter.ai/docs/guides/features/plugins/web-search>.
*Implication:* attach the plugin **selectively** — and note that the as-built loop has
**no attach point that means "just the answer round"**. `OpenRouterChatOptions.Create(...)`
runs once before the loop (`ConversationService.cs:50`) and that single `ChatOptions` is
reused by *every* tool round, so gating there hits the cost trap (up to `MaxToolRounds`
searches per turn). The other `Create` call (`:133`) is the **round-cap fallback**, not the
normal synthesis round: the loop `yield break`s from *inside* itself the moment a round
returns zero tool calls (`:101-109`), and `:133` only executes when all `MaxToolRounds`
(default 8) rounds made tool calls — attaching there means search fires almost never.
Getting "one search, on the answer round" therefore requires **per-round options
construction**: move the `Create` call inside the loop (or vary the
`RawRepresentationFactory` closure per round) and set the plugin only on the rounds that
should search — a small refactor the epic must spec, not a one-parameter bool on existing
code. The `openrouter:web_search` server tool (open question C) would sidestep this
entirely by letting the model decide when to search.

**`usage.cost` — the app already reads the number that includes it (medium confidence on
itemisation).** `usage.cost` is documented as the total credit cost of the request, and the
web plugin is billed as a surcharge *on that request*, so the per-search fee is part of the
same `usage.cost` the app recovers today via
`ChatTokenUsage.Patch.TryGetValue("$.cost", …)` (`ConversationService.cs:180`). The docs do
**not** separately itemise the search fee inside the usage object, so treat "the plugin fee
is inside `usage.cost`" as strongly implied but not explicitly stated.
Source (cost field): <https://openrouter.ai/docs/use-cases/usage-accounting>.
*Aside worth a separate check:* that same usage-accounting page now states the
`usage: { include: true }` request parameter is **deprecated and has no effect** (usage is
always returned). The repo's `OpenRouterChatOptions` still patches `$.usage` with
`{ include: true }` (`OpenRouterChatOptions.cs:34`); if the doc is current that patch is now
a no-op and could be dropped. Not a web-search blocker — flagging because I saw it.

**Verdict on composition:** the plugin composes with the hand-driven loop and streaming
cleanly; the only design constraint it imposes is *don't attach it to every round*.

---

## (b) Pricing

Current OpenRouter web-search pricing (per the docs), by engine:

| Engine | Price | Notes |
|---|---|---|
| **Exa** (default for models without native search) | **$0.005 / request** (≤10 results) | +$0.001 per result above 10 |
| **Parallel** | $0.001 / request (≤10 results) | +$0.001 per result above 10 |
| **Perplexity** | $0.005 / request | |
| **Native** (OpenAI/Anthropic/Google/xAI/Perplexity built-in) | provider passthrough | "Pricing is passed through directly from the provider" — no OpenRouter markup number quoted |

- **Default `max_results` = 5** (the config comment reads `"max_results": 1, // Defaults to 5`),
  which is ≤10, so a default Exa search bills the flat **$0.005**.
- Default engine selection: **native** search for OpenAI/Anthropic/Google/Perplexity/xAI,
  **Exa** for everything else. Since the assistant defaults to Anthropic/Google models
  (`ConversationOptions.Model`), leaving `engine` unset would use *native* passthrough
  pricing; set `"engine": "exa"` explicitly if you want the predictable $0.005 flat rate.
Source: <https://openrouter.ai/docs/features/web-search> and
<https://openrouter.ai/docs/guides/features/plugins/web-search>.

Older figure, now superseded: OpenRouter's launch blog quoted "**$4 per 1,000 web results**"
(= $0.004/result). The current per-request pricing above supersedes it; cited only so the
discrepancy isn't a surprise.
Source: <https://openrouter.ai/blog/announcements/introducing-web-search-via-the-api/>.

**Prompt-token cost on top of the search fee:** results are injected into the prompt as an
extra message (see (d)), so each search also adds input tokens billed at the model's normal
rate. Total per search ≈ plugin fee ($0.005 Exa) + N injected-result tokens × model input
price.

---

## (c) MEAI injection path

**`plugins` is a top-level request-body field, so the JsonPatch mechanism the repo already
uses works verbatim.** OpenRouter's example body puts `plugins` at the top level alongside
`model`/`messages`. The repo's `OpenRouterChatOptions.BuildRawRepresentation`
(`OpenRouterChatOptions.cs:18-38`) already patches three top-level fields (`$.provider`,
`$.reasoning`, `$.usage`) onto a fresh `ChatCompletionOptions` via the experimental
`options.Patch.Set(...)` API (pinned by `OpenAI 2.11.0`, `Microsoft.Extensions.AI[.OpenAI]
10.7.0` in the csproj). The web plugin is one more line in that exact block:

```csharp
// Enable OpenRouter's Exa-backed web search for this request (§254). One search is
// billed per request this patch is attached to — attach selectively, not every round.
options.Patch.Set("$.plugins"u8,
    BinaryData.FromString("""[{"id":"web","engine":"exa","max_results":5}]"""));
```

(`BinaryData.FromString` with a raw JSON string literal, or `BinaryData.FromObjectAsJson`
with an anonymous object as the sibling patches do — either serialises to the same body.)
To make it selective, thread a `bool includeWebSearch` through `OpenRouterChatOptions.Create`
and only emit the patch when true — but see (a): neither existing `Create` call site is a
usable attach point (`:50` is shared by every loop round; `:133` is the rarely-hit round-cap
fallback), so selective attachment also means moving options construction inside the loop
so each round can decide.

**The `:online` suffix is the no-body-patch alternative — a pure model-id string change.**
OpenRouter documents `:online` as "exactly equivalent to" `plugins: [{ "id": "web" }]` with
default config. The client is built with `.GetChatClient(conversation.Model)`
(`ConversationRegistration.cs:97`), so appending `:online` to `ConversationOptions.Model`
(e.g. `"anthropic/claude-sonnet-4.6:online"`) turns web search on with **zero** code change
— but it is *unconditional* (every request that model makes searches), which is the wrong
default for a multi-round loop (see (a)). Use `:online` only if you accept a search on every
round, or point a *separate* "final answer" chat client at the `:online` slug. The explicit
`$.plugins` patch is preferred because it can be attached per-round and lets you pin
`engine`/`max_results`.
Source: <https://openrouter.ai/docs/features/web-search>.

Confirmed the patch mechanism exists exactly where described: `OpenRouterChatOptions.cs` is
the single place ChatOptions.RawRepresentationFactory + `Patch.Set` live, and
`ConversationService` builds every round's options through it — so the plugin patch slots in
with no new infrastructure.

---

## (d) Result quality / citations format

**Citations come back as OpenAI-standard `url_citation` annotations on the assistant
message.** OpenRouter normalises all engines to this schema:

```json
{
  "type": "url_citation",
  "url_citation": {
    "url": "https://www.example.com/web-search-result",
    "title": "Title of the web search result",
    "content": "Content of the web search result",
    "start_index": 100,
    "end_index": 200
  }
}
```

`content` is "Extractive excerpts drawn from the page that Exa selects as most relevant"
(with `[...]` markers between excerpts); `start_index`/`end_index` mark where in the reply
text the citation applies.
Source: <https://openrouter.ai/docs/features/web-search> and
<https://openrouter.ai/docs/guides/features/plugins/web-search>.

**Results are injected into the prompt as an extra message → they consume prompt tokens at
the model's normal rate.** OpenRouter prepends a search-results block using a default
`search_prompt`: `"A web search was conducted on \`date\`. Incorporate the following web
search results into your response. IMPORTANT: Cite them using markdown links named using
the domain of the source. Example: [nytimes.com](https://nytimes.com/some-page)."`
Customisable via `"search_prompt": "…"` in the plugin config. So the model *also* emits
inline markdown citations in its own text, independent of the structured annotations.
Source: <https://openrouter.ai/docs/features/web-search>.

**The OpenAI .NET SDK surfaces annotations typed ONLY on the non-streaming response — the
app's streaming path must recover them via `Patch`.** From the SDK's public API surface
(`openai-dotnet/api/OpenAI.net8.0.cs`, `OpenAI 2.11.0`):
- Non-streaming `ChatCompletion` has `[Experimental("OPENAI001")] IReadOnlyList<ChatMessageAnnotation> Annotations { get; }`,
  and `ChatMessageAnnotation` exposes `WebResourceUri` (Uri), `WebResourceTitle` (string),
  `StartIndex`, `EndIndex`.
- **`StreamingChatCompletionUpdate` has NO typed `Annotations` property** — its members are
  `ContentUpdate`, `ToolCallUpdates`, `Usage`, `FinishReason`, … and a
  `[Experimental("SCME0001")] ref JsonPatch Patch`. There is no typed annotation surface on
  the streamed delta.
Source: <https://github.com/openai/openai-dotnet/blob/main/api/OpenAI.net8.0.cs>
(lines ~1510 `ChatCompletion.Annotations`, ~1768 `ChatMessageAnnotation`, and the
`StreamingChatCompletionUpdate` block).

Consequence for this codebase: because `ConversationService` streams
(`GetStreamingResponseAsync`), MEAI's `ChatResponseUpdate.RawRepresentation` is a
`StreamingChatCompletionUpdate`, which does not carry typed annotations. Recover them with
the **same pattern already used for `usage.cost`** (`ConversationService.cs:170-182`): find
the update whose `RawRepresentation is StreamingChatCompletionUpdate raw`, then
`raw.Patch.TryGetValue("$.annotations"u8, out …)` (experimental `SCME0001`). OpenRouter
delivers the annotations attached to the final content chunk of the stream. MEAI itself does
not expose a typed `Annotations` on `ChatResponseUpdate`, so the JsonPatch recovery is the
route regardless.

---

## Alternative considered — search as a normal app-driven tool

Register `web_search` via `AIFunctionFactory` like the existing curated tools
(`ConversationToolRegistry`), have it call a search API directly, and let the loop dispatch
it. Tradeoffs:

- **Pros:** the *model* decides when to search (fires only on the round it calls the tool,
  never on rounds it doesn't — no per-round surcharge); results flow back as ordinary tool
  output that the loop already renders and the §-conversation-state store already persists,
  so cross-round memory of what was searched is free; full choice of provider and full
  control over how citations are formatted into the reply.
- **Cons:** it reintroduces exactly the wire work ADR-0006 chose MEAI to avoid — a new HTTP
  client, response parsing, a second provider + API key + its own billing/observability,
  and hand-rolled citation formatting — and it steps outside ADR-0005's "OpenRouter is the
  single provider." No OpenRouter standalone search endpoint exists to keep it single-provider
  (the docs expose web search only through chat-completions, via `:online`/plugin/server-tool),
  so a direct tool means a third-party dependency.
- **Direct-provider price (for sizing):** Exa's own API is **$7 / 1,000 searches** (≤10
  results; +$1/1k for extra results; page contents +$1/1k pages; 20k requests/month free)
  — i.e. slightly more per search than OpenRouter's $0.005 Exa passthrough, but with a free
  tier. Source: <https://exa.ai/pricing>. Brave/Tavily are comparable-order alternatives.

Net: the app-owned tool is the right choice only if per-call model control or a specific
search provider is a hard requirement; otherwise its plumbing cost outweighs the plugin's
one-line patch.

---

## Open questions

- **C. `openrouter:web_search` server tool.** ~~Worth a 10-minute spike against the real
  API before committing.~~ **Resolved by the #261 addendum below**: it composes
  transparently (server-side execution, no app dispatch), and is now the recommendation.
- **`usage.cost` itemisation.** Docs confirm `usage.cost` is the total request cost but do
  not itemise the web-search surcharge inside it; verify empirically that a plugin search
  moves `usage.cost` (it should) so the §5 usage ledger attributes it correctly.
- **`usage:{include:true}` deprecation.** The usage-accounting page states this parameter is
  now deprecated/no-effect; verify and, if so, drop the `$.usage` patch in
  `OpenRouterChatOptions.cs:34` in the same change (separate from web search).
- **Streaming annotation chunk.** Confirmed annotations are OpenAI-standard and recovered via
  `StreamingChatCompletionUpdate.Patch`; the exact chunk OpenRouter attaches them to (final
  content chunk vs. a dedicated trailing chunk) isn't documented — verify against a live
  stream when wiring the recovery. **Answered for the server tool by the #261 addendum:**
  annotations arrive spread across *multiple* mid-stream delta chunks (one per citation as
  the model writes), at Patch path `$.choices[0].delta.annotations` — the recovery must
  accumulate across all updates, not read one trailing chunk.

---

## Addendum (#261) — the `openrouter:web_search` server tool, spiked live

Spike for ticket #261 (2026-07-16), against the real OpenRouter API
(`anthropic/claude-sonnet-4.6`, Anthropic-pinned provider, streaming), answering open
question C. Two legs: raw SSE over chat-completions with an app `function` tool **and**
`{"type":"openrouter:web_search","parameters":{"engine":"exa","max_results":3}}` in the
same `tools` array (three cases: web-only prompt, app-tool-only prompt, app-tool
dispatch round-trip); then the same composition through the app's exact stack
(`OpenAI 2.11.0` + `Microsoft.Extensions.AI[.OpenAI] 10.7.0`, `AsIChatClient()`,
`GetStreamingResponseAsync`) with the wire body captured via a logging transport.

### Verdict — **spec the server tool in the epic, not the selective plugin**

It is the best-of-both option #254 hoped for: model-decides-when-to-search semantics with
zero app plumbing, **and it removes the per-round options refactor entirely** — because a
search is billed only when the model invokes one (never per attached request), the tool can
be attached to *every* loop round from the existing shared
`OpenRouterChatOptions.BuildRawRepresentation`, one line, no per-round options
construction. The selective-plugin patch (and its loop refactor) stands as the documented
fallback.

### (a) Execution model — server-side, within one request, no app dispatch

OpenRouter executes the search entirely server-side and the model incorporates results
before the stream ends: `finish_reason=stop`, full cited answer text, **no
`openrouter:web_search` tool call ever surfaced to the client** in any case. The model may
search **multiple times in one request** (the web-only case ran 3 searches — visible as
`server_tool_use_details: {"web_search_requests": 3, "tool_calls_requested": 3,
"tool_calls_executed": 3}` on the usage object). The app's dispatch loop never sees the
search; it only ever dispatches its own `function` tools.

### (b) Coexistence with app tools — clean, on both the raw and MEAI paths

- **App-tool prompt, server tool attached:** the `meme_search` call streamed and assembled
  normally (`finish_reason=tool_calls`, standard `toolu_…` id, well-formed argument
  deltas); no search fired, no search fee. Streaming tool-call assembly is unperturbed.
- **Mixed round (both in one turn):** the model can run a server-side search *and* emit an
  app tool call *and* answer text **in the same round, with `finish_reason=stop`**. The
  loop's round classifier must key on "did the round return tool calls" — which
  `ConversationService` already does — not on `finish_reason`.
- **MEAI injection:** the server tool is not a `function` tool, so it cannot ride
  `ChatOptions.Tools`. It slots in as one `JsonPatch` line next to the existing
  `provider`/`reasoning` patches:

  ```csharp
  options.Patch.Append("$.tools"u8,
      BinaryData.FromString("""{"type":"openrouter:web_search","parameters":{"engine":"exa","max_results":3}}"""));
  ```

  `JsonPatch.Append` **merges with the typed model's serialization** — the captured wire
  body shows `tools: [{function meme_search…}, {openrouter:web_search…}]`, i.e. the
  adapter-serialized `ChatOptions.Tools` entries followed by the appended server tool.
  Nothing is clobbered (`Patch.Set("$.tools"…)` would replace the array — use `Append`).

### (c) Cost — inside `usage.cost`, and itemisable

The search fee lands in the same `usage.cost` the app already recovers via
`ChatTokenUsage.Patch.TryGetValue("$.cost"u8, …)`. Better than #254 could confirm for the
plugin, it is **itemisable**: the usage object carries
`cost_details.upstream_inference_cost` (model tokens only), and in the 3-search case
`cost − upstream_inference_cost = $0.015` — exactly 3 × the $0.005 Exa fee — plus the
`server_tool_use_details.web_search_requests` counter. The §-ledger can attribute search
spend precisely per round.

Cost caveats for the epic:
- **The model controls search count** — one request billed 3 searches when the prompt
  invited it. `max_results` bounds results per search; `max_total_results` bounds
  cumulative results per request (an indirect search cap); there is no direct
  "max searches" knob. Soft-cap alerting (#256) covers the tail risk.
- **Searches don't persist across rounds**: results are injected into *that request's*
  context only — they are not in the message history the app replays — so a later round
  of the same turn may re-search what round N already searched (observed: the dispatch
  round-trip case searched once in round 1 and again in round 2, +$0.005). Acceptable at
  this scale; worth knowing when reading the ledger.
- Injected results are billed as normal prompt tokens (the 3-search request carried
  ~13k prompt tokens vs ~750 for the no-search request).

### (d) Citations — same `url_citation` schema, different delivery

Annotations are the same OpenAI-standard `url_citation` objects the plugin produces
(`url`, `title`, `content` with Exa's `[...]` excerpt markers) — but `start_index` /
`end_index` were **always 0** (no reply-text offsets; offset-based citation rendering is
out), and they arrive **spread across multiple mid-stream delta chunks** (one annotation
per chunk as the model writes), not on a single trailing chunk. On the MEAI streaming
path they are invisible to the typed surface and recover via the raw update's Patch at
**`$.choices[0].delta.annotations`** — note the full path from the chunk root;
`$.annotations` misses (#254 assumed the shorter path). Recovery must accumulate across
all updates of the round.

### What the epic should spec

1. Append the server tool in `OpenRouterChatOptions.BuildRawRepresentation` (every round,
   config-gated, pin `engine:"exa"` + `max_results`), via `Patch.Append("$.tools"…)`.
2. Citation recovery in the streaming sink: accumulate `$.choices[0].delta.annotations`
   from each raw update; render as markdown links (the model already emits inline
   domain-named links in its text; annotations carry the structured source list).
3. Ledger: keep reading `usage.cost`; optionally also record
   `cost_details.upstream_inference_cost` and `server_tool_use_details.web_search_requests`
   to itemise search spend.
4. No loop changes: mixed rounds (text + app tool call + server search,
   `finish_reason=stop`) are already handled by the has-tool-calls round classifier.

Spike artifacts (raw chunk dumps, harnesses) were session-scratchpad throwaways; the raw
JSONL digests are summarised above.
