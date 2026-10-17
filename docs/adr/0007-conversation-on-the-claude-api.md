# Conversational assistant calls the Claude API directly, not OpenRouter

**Status**: Accepted (2026-10-15). Supersedes the transport half of ADR-0006.

ADR-0006 built the conversational assistant as a hand-driven agentic loop on Microsoft.Extensions.AI (`IChatClient`). The transport was the OpenAI client pointed at OpenRouter, with Claude-specific body fields injected through `ChatOptions.RawRepresentationFactory` + `JsonPatch`. This ADR replaces that transport (#426). The assistant now calls the Claude API directly through the official `Anthropic` NuGet package (12.58.0) and its MEAI adapter (`AsIChatClient`). The rest of ADR-0006 stays: the hand-driven loop, the round seam and its narration, the tool set and the admin gate, the confirm flow, the conversation store, the usage ledger and the Langfuse tracing.

The scope is the conversation only. Meme indexing, the meme benchmark, `OpenRouterClient` and `OpenRouterOptions` stay on OpenRouter (Gemini models).

## Why

- **Credits.** The project has credits on the Claude API. OpenRouter bills the same model with a margin on top.
- **Native features.** The Claude API gives prompt caching, strict tools, the native web search tool and the thinking round-trip as first-class request fields. Over OpenRouter each one was a body patch or was not available.
- **Less wire plumbing.** The official SDK owns the Messages API wire. The bot no longer patches request JSON by hand.

## Decisions

- **Model and endpoint.** The model id is `claude-sonnet-5` (was `anthropic/claude-sonnet-5`). Requests go to `https://api.anthropic.com`. The base URL is fixed in code. The loop uses the streaming Messages API.
- **Key.** The conversation reads `Anthropic:ApiKey`, with the env variable `ANTHROPIC_API_KEY` as the fallback. The OpenRouter key stays for the meme pipeline.
- **Thinking.** The request turns on adaptive thinking with summarized display. `output_config.effort` comes from `Conversation:ReasoningEffort` (`low`, `medium`, `high`, `xhigh` or `max`).
- **Web search.** The assistant uses the native server tool `web_search_20260209` with `allowed_callers: ["direct"]`. This turns dynamic filtering off: its code-execution blocks do not round-trip through the MEAI adapter. `Conversation:WebSearch` maps to the tool: `Enabled`, `MaxUses`, `AllowedDomains`/`BlockedDomains` and `UserLocation`. The engine setting (exa) is gone. Citations come as native citations on text blocks. The compact source list of #271 stays.
- **Stop reasons.** On `pause_turn` the loop sends the response back and continues. On `refusal` the loop ends the turn, in any round including the one after the tool-round cap. The turn's rows get `refused = true` and no later window replays them (#450): a refusal left in context got every later turn of a thread refused on prod, harmless ones too.
- **Prompt caching.** The request sets `cache_control` breakpoints on the last function tool, on the system prompt, and on the last message when the request offers tools.
- **Retry.** One layer only: the app `ConversationRetryPolicy`. The SDK has `MaxRetries` 0. Transient failures are HTTP 408, 429 and 5xx (529 included), mid-stream SSE `error` events, transport errors and attempt timeouts. The policy honours `retry-after` and `retry-after-ms`. A `DelegatingHandler` logs one Warning per failed attempt, with the request id. One attempt waits at most 60 s for the response headers.
- **History.** Thinking blocks are kept within a turn and stripped from stored history. The replay window never starts with an assistant message and never ends on a tool-call group without an answer: the API rejects both.
- **Cost.** The Claude API returns no USD cost. The bot computes the cost from the token counts and a price table in config, keyed by model id. For `claude-sonnet-5` the table holds $2/MTok input, $10/MTok output, $0.20/MTok cache read, $2.50/MTok 5-minute cache write, and $10 per 1,000 web searches (verified on the Claude pricing page on 2026-10-15).
- **Ledger.** `conversation_usage.cost_usd` is the token cost plus the search fee. `conversation_usage.upstream_inference_cost_usd` stays and now holds the token-only cost (`cost_usd` less the search fee). New columns `cache_read_tokens` and `cache_write_tokens` hold the cache counts. `prompt_tokens` is the total input: uncached input plus cache read plus cache write.
- **Metrics.** The metric names in `Infrastructure/BotMetrics.cs` do not change. `wojtus_conversation_web_search_cost_usd` is still cost less upstream, so it is still the search fee. The `model` label value changes from `anthropic/claude-sonnet-5` to `claude-sonnet-5`. The #269 cost-cap alerts sum `cost_usd` and keep working.

## Consequences

- **Two keys.** The service holds an OpenRouter key (memes) and an Anthropic key (conversation). Both must be set in each environment where the feature runs.
- **The cost is computed, not billed.** The ledger cost is an estimate from the price table. It matches the invoice only while the table matches the price list.
- **The price table needs care.** A price change or a new model id needs a config change. Each model id in use needs a row in the table.
- **Dynamic filtering is off.** Web search returns raw results without the code-execution filter step. A future MEAI adapter that round-trips those blocks can turn it on.
- **Cutover in the time series.** The `model` label changes value at the deploy. Panels and queries "by model" show two series across the cutover.
- **Upstream cost changes meaning.** Before the cutover, `upstream_inference_cost_usd` was the provider-reported model cost. After it, the column is the computed token cost. The difference `cost_usd − upstream_inference_cost_usd` is the search fee on both sides.
- **Input tokens include the cache.** `prompt_tokens` and the input token metric count cache reads and cache writes. A cached prompt costs less than its token count suggests.
- **Langfuse model definition.** ADR-0006 records a Langfuse project model definition keyed `anthropic/claude-sonnet-5`. The new model id does not match it. A follow-up must add a definition for `claude-sonnet-5`. The `conversation.cost_usd` trace attribute stays the authoritative cost.
- **Outbound monitoring.** The runtime dashboard watches `api.anthropic.com` as its own host. The OpenRouter panels now cover meme vision calls only.

## Amendment: Claude Sonnet 5.5 (2026-10-16, #435)

The model id is now `claude-sonnet-5-5`. The prices are the same as `claude-sonnet-5`. The price table keeps the `claude-sonnet-5` row, so a rollback by `Conversation__Model` still gets a cost.

- **Breaking changes that do not apply.** Sonnet 5.5 rejects `thinking: disabled`, forced `tool_choice` (`any` / `tool`) and non-default sampling values. The loop sends none of them: adaptive thinking, `auto`, and `none` on the post-cap round.
- **Effort.** The effort levels are recalibrated on 5.5. `medium` stays: it is the start point for multistep tool use, and the live tests showed no problem.
- **Preserved thinking.** On 5.5 a replayed thinking block is valid only while everything before it is unchanged. The loop passed the check with `prefix_mismatch_behavior: "error"` (beta `thinking-binding-controls-2026-08-01`) in live tests: multi-round tools, web search plus a local tool in one prompt (the deferred result), and a follow-up in the same thread. A direct API test showed that the `server_tool_use` block `ConversationHistory` rebuilds is byte-identical to the block the API sent, and a thinking block after it stays valid. Moving `cache_control` is not an edit. The request does not send the beta: the history is append-only. A future change that edits earlier messages, the system prompt or the tools within a turn breaks this. Run the same check again for such a change.
- **Text between tool calls.** 5.5 sends notes longer than a sentence or two as thinking blocks, not text. Discord shows only text; the summarized thinking goes to `conversation_message.reasoning` and Langfuse. In the live tests the bot still narrated: the notes before tool calls were short and came back as text in 5 of 5 tool rounds. This is accepted.
- **Refusals.** 5.5 declines in five categories (`cyber`, `bio`, `frontier_llm`, `reasoning_extraction`, `general_harms`). The loop handles `refusal` as before. The server-side fallback (`fallbacks: "default"`) is not used: it retries only `cyber` and `frontier_llm`, it needs a beta header and a raw body field, and the ledger would record the wrong model.
- **Langfuse.** The cost on the generation span comes from `langfuse.observation.cost_details` (#434). No Langfuse model definition is necessary for `claude-sonnet-5-5`.
