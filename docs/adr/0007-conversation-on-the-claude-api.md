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
- **Stop reasons.** On `pause_turn` the loop sends the response back and continues. On `refusal` the loop ends the turn.
- **Prompt caching.** The request sets `cache_control` breakpoints on the last function tool, on the system prompt, and on the last message when the request offers tools.
- **Retry.** One layer only: the app `ConversationRetryPolicy`. The SDK has `MaxRetries` 0. Transient failures are HTTP 408, 429 and 5xx (529 included), mid-stream SSE `error` events, transport errors and attempt timeouts. The policy honours `retry-after` and `retry-after-ms`. A `DelegatingHandler` logs one Warning per failed attempt, with the request id.
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
