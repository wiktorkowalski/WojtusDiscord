using DiscordEventService.Configuration;
using DiscordEventService.Services.Conversation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiscordEventService.Tests;

// The §4 live tools' testable contracts (#270): the guild-resolution guardrails, the
// member-lookup decision tree (id vs fragment, ambiguity, not-found), and the exact
// model-facing output shapes. The projection off real DSharpPlus caches is deliberately
// NOT faked here — that layer is live-verified on the dev bot.
public sealed class ConversationLiveStateToolTests
{
    private const ulong GuildId = 100UL;
    private const ulong ChannelId = 7UL;

    // ─────────────────────────── tool-level contracts ───────────────────────────

    [Fact]
    public async Task VoiceOccupants_InDmWithoutPrimaryGuild_ReportsNoServer()
    {
        var live = new FakeGuildLiveStateService();
        var toolset = BuildToolset(DmContext(), live, primaryGuildId: null);

        var result = await InvokeAsync(toolset, "voice_occupants", []);

        Assert.Contains("server", result, StringComparison.OrdinalIgnoreCase);
        Assert.Null(live.VoiceGuildId);
    }

    [Fact]
    public async Task VoiceOccupants_InDm_FallsBackToPrimaryGuild()
    {
        var live = new FakeGuildLiveStateService { VoiceChannels = [] };
        var toolset = BuildToolset(DmContext(), live, primaryGuildId: GuildId);

        var result = await InvokeAsync(toolset, "voice_occupants", []);

        Assert.Equal(GuildId, live.VoiceGuildId);
        Assert.Contains("Nobody", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VoiceOccupants_FormatsChannelsOccupantsAndJumpLinks()
    {
        var live = new FakeGuildLiveStateService
        {
            VoiceChannels =
            [
                new LiveVoiceChannel(GuildId, 555UL, "Nora", [
                    new LiveVoiceOccupant("Alice", IsMuted: true, IsDeafened: false, IsStreaming: false, HasVideo: false),
                    new LiveVoiceOccupant("Bob", IsMuted: false, IsDeafened: false, IsStreaming: true, HasVideo: true),
                    new LiveVoiceOccupant("Carol", IsMuted: false, IsDeafened: false, IsStreaming: false, HasVideo: false),
                ]),
            ],
        };
        var toolset = BuildToolset(GuildContext(), live);

        var result = await InvokeAsync(toolset, "voice_occupants", []);

        Assert.Contains("**Nora** (3)", result);
        Assert.Contains($"https://discord.com/channels/{GuildId}/555", result);
        Assert.Contains("Alice (muted)", result);
        Assert.Contains("Bob (streaming, camera on)", result);
        Assert.Contains("- Carol", result);
    }

    [Fact]
    public async Task VoiceOccupants_GuildNotInCache_ReportsCleanly()
    {
        var live = new FakeGuildLiveStateService { VoiceChannels = null };
        var toolset = BuildToolset(GuildContext(), live);

        var result = await InvokeAsync(toolset, "voice_occupants", []);

        Assert.Contains("can't see", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemberInfo_Ambiguous_ReturnsCandidateList()
    {
        var live = new FakeGuildLiveStateService
        {
            MemberLookup = MemberLookupResult.Ambiguous(
            [
                new LiveMemberCandidate(1UL, "anna_a", "Anna A", "Ania"),
                new LiveMemberCandidate(2UL, "anna_b", null, "Anka"),
            ]),
        };
        var toolset = BuildToolset(GuildContext(), live);

        var result = await InvokeAsync(toolset, "member_info", new() { ["member"] = "an" });

        Assert.Equal("an", live.MemberQuery);
        Assert.Contains("Several members match", result);
        Assert.Contains("Ania / Anna A (username anna_a, id 1)", result);
        Assert.Contains("Anka (username anna_b, id 2)", result);
    }

    [Fact]
    public async Task MemberInfo_Unknown_ReturnsCleanNotFound()
    {
        var live = new FakeGuildLiveStateService { MemberLookup = MemberLookupResult.NotFound() };
        var toolset = BuildToolset(GuildContext(), live);

        var result = await InvokeAsync(toolset, "member_info", new() { ["member"] = "999" });

        Assert.Contains("No member matching \"999\"", result);
    }

    [Fact]
    public async Task MemberInfo_BlankQuery_AsksForAValue()
    {
        var live = new FakeGuildLiveStateService();
        var toolset = BuildToolset(GuildContext(), live);

        var result = await InvokeAsync(toolset, "member_info", new() { ["member"] = "  " });

        Assert.Null(live.MemberQuery);
        Assert.Contains("Provide", result);
    }

    [Fact]
    public async Task MemberInfo_Found_RendersTheFullSnapshot()
    {
        var live = new FakeGuildLiveStateService
        {
            MemberLookup = MemberLookupResult.Found(new LiveMemberInfo(
                Id: 42UL,
                Username: "wiktor",
                GlobalName: "Wiktor",
                DisplayName: "Wikuś",
                IsBot: false,
                AccountCreatedAt: new DateTimeOffset(2016, 5, 1, 0, 0, 0, TimeSpan.Zero),
                JoinedAt: new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
                BoostingSince: new DateTimeOffset(2024, 3, 4, 0, 0, 0, TimeSpan.Zero),
                TimedOutUntil: null,
                Roles: ["Admin", "Gracz"],
                PresenceStatus: "online",
                Activities: ["Playing Factorio", "Custom status: grinduję"],
                Voice: new LiveMemberVoice("Nora", IsMuted: false, IsDeafened: true, IsStreaming: false, HasVideo: false))),
        };
        var toolset = BuildToolset(GuildContext(), live);

        var result = await InvokeAsync(toolset, "member_info", new() { ["member"] = "42" });

        Assert.Contains("**Wikuś** (username wiktor, id 42)", result);
        Assert.Contains("Status: online", result);
        Assert.Contains("Playing Factorio", result);
        Assert.Contains("Custom status: grinduję", result);
        Assert.Contains("Voice: in **Nora** (deafened)", result);
        Assert.Contains("Roles: Admin, Gracz", result);
        Assert.Contains("Joined this server: 2020-01-02", result);
        Assert.Contains("Account created: 2016-05-01", result);
        Assert.Contains("Boosting the server since 2024-03-04", result);
        Assert.DoesNotContain("Timed out", result);
    }

    [Fact]
    public async Task ServerInfo_RendersTheLiveSnapshot()
    {
        var live = new FakeGuildLiveStateService
        {
            ServerInfo = new LiveServerInfo(
                Id: GuildId,
                Name: "Wojtusiowo",
                CreatedAt: new DateTimeOffset(2017, 8, 9, 0, 0, 0, TimeSpan.Zero),
                OwnerName: "Wikuś",
                MemberCount: 123,
                BoostTier: 2,
                BoostCount: 7,
                TextChannelCount: 20,
                VoiceChannelCount: 5,
                CategoryCount: 4,
                RoleCount: 15,
                EmojiCount: 60),
        };
        var toolset = BuildToolset(GuildContext(), live);

        var result = await InvokeAsync(toolset, "server_info", []);

        Assert.Contains("**Wojtusiowo** (id 100), created 2017-08-09", result);
        Assert.Contains("Owner: Wikuś", result);
        Assert.Contains("Members: 123", result);
        Assert.Contains("Boosts: level 2, 7 boost(s)", result);
        Assert.Contains("Channels: 20 text, 5 voice, 4 categories", result);
        Assert.Contains("Roles: 15 | Emojis: 60", result);
    }

    // ─────────────────────────── MemberMatcher contract ───────────────────────────

    private static readonly LiveMemberCandidate[] Members =
    [
        new(1UL, "anna_gaming", "Anna G", "Ania"),
        new(2UL, "annabelle", null, "Bella"),
        new(3UL, "marek", "Marek", "Marek"),
        new(4UL, "ann", null, "Przemek"),
    ];

    [Fact]
    public void Match_UniqueFragment_Finds()
    {
        var result = MemberMatcher.Match(Members, "marek");

        Assert.Equal(MemberLookupOutcome.Found, result.Outcome);
        Assert.Equal(3UL, result.Member);
    }

    [Fact]
    public void Match_ExactNameBeatsSubstringHits()
    {
        // "ann" is a substring of three members but the exact username of one — the exact
        // hit must win instead of reporting ambiguity.
        var result = MemberMatcher.Match(Members, "ann");

        Assert.Equal(MemberLookupOutcome.Found, result.Outcome);
        Assert.Equal(4UL, result.Member);
    }

    [Fact]
    public void Match_AmbiguousFragment_ReturnsCandidates()
    {
        var result = MemberMatcher.Match(Members, "anna");

        Assert.Equal(MemberLookupOutcome.Ambiguous, result.Outcome);
        Assert.Equal(new[] { 1UL, 2UL }, result.Candidates.Select(c => c.Id).Order().ToArray());
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var result = MemberMatcher.Match(Members, "MAREK");

        Assert.Equal(MemberLookupOutcome.Found, result.Outcome);
        Assert.Equal(3UL, result.Member);
    }

    [Fact]
    public void Match_NoHit_IsNotFound()
    {
        var result = MemberMatcher.Match(Members, "zzz");

        Assert.Equal(MemberLookupOutcome.NotFound, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Match_CapsTheCandidateList()
    {
        var crowd = Enumerable.Range(1, 30)
            .Select(i => new LiveMemberCandidate((ulong)i, $"user{i}", null, $"User {i}"))
            .ToArray();

        var result = MemberMatcher.Match(crowd, "user");

        Assert.Equal(MemberLookupOutcome.Ambiguous, result.Outcome);
        Assert.Equal(10, result.Candidates.Count);
    }

    // ─────────────────────────── helpers ───────────────────────────

    private static ConversationToolset BuildToolset(
        ConversationContext context, FakeGuildLiveStateService live, ulong? primaryGuildId = GuildId)
    {
        var options = Options.Create(new ConversationOptions { PrimaryGuildId = primaryGuildId });
        var registry = new ConversationToolRegistry(
            memeSearch: null!,
            guildStats: null!,
            databaseQuery: null!,
            live,
            new FakeGuildActionService(),
            new FakeConfirmationService(),
            new DatabaseSchemaHint("schema"),
            options,
            NullLogger<ConversationToolRegistry>.Instance);
        return registry.BuildToolset(context);
    }

    private static ConversationContext GuildContext() =>
        new(GuildId, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId);

    private static ConversationContext DmContext() =>
        new(GuildId: null, InvokerId: 42UL, "tester", IsAdmin: false, ChannelId);

    private static async Task<string> InvokeAsync(
        ConversationToolset toolset, string tool, Dictionary<string, object?> arguments)
    {
        var result = await toolset.InvokeAsync(
            new FunctionCallContent("call_1", tool, arguments), CancellationToken.None);
        return result.Result?.ToString() ?? string.Empty;
    }
}

// Canned live-state answers; also used by the other conversation test suites wherever the
// registry needs its full dependency set but the live tools are never invoked.
internal sealed class FakeGuildLiveStateService : IGuildLiveStateService
{
    public IReadOnlyList<LiveVoiceChannel>? VoiceChannels { get; set; } = [];
    public MemberLookupResult? MemberLookup { get; set; } = MemberLookupResult.NotFound();
    public LiveServerInfo? ServerInfo { get; set; }

    public ulong? VoiceGuildId { get; private set; }
    public string? MemberQuery { get; private set; }

    public IReadOnlyList<LiveVoiceChannel>? GetVoiceOccupants(ulong guildId)
    {
        VoiceGuildId = guildId;
        return VoiceChannels;
    }

    public Task<MemberLookupResult?> FindMemberAsync(ulong guildId, string query, CancellationToken cancellationToken)
    {
        MemberQuery = query;
        return Task.FromResult(MemberLookup);
    }

    public LiveServerInfo? GetServerInfo(ulong guildId) => ServerInfo;
}
