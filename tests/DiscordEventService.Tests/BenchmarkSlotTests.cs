using DiscordEventService.Services.MemeIndexing;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class BenchmarkSlotTests
{
    [Theory]
    [InlineData("google/gemini-3-flash-preview", "google/gemini-3-flash-preview", null)]
    [InlineData("google/gemini-3.8-flash|effort=low", "google/gemini-3.8-flash", "low")]
    [InlineData("vendor/model:free|effort=xhigh", "vendor/model:free", "xhigh")]
    public void TryParse_ValidSlot_SplitsModelAndEffort(string value, string expectedModel, string? expectedEffort)
    {
        Assert.True(BenchmarkSlot.TryParse(value, out var slot, out var error));

        Assert.Null(error);
        Assert.Equal(expectedModel, slot!.Model);
        Assert.Equal(expectedEffort, slot.ReasoningEffort);
        Assert.Equal(value, slot.Key);
    }

    [Fact]
    public void TryParse_SlotWithSpaces_NormalizesTheKey()
    {
        Assert.True(BenchmarkSlot.TryParse("  vendor/model | effort = low ", out var slot, out _));

        Assert.Equal("vendor/model|effort=low", slot!.Key);
    }

    [Theory]
    [InlineData(null, "no model id")]
    [InlineData("", "no model id")]
    [InlineData("|effort=low", "no model id")]
    [InlineData("vendor/model|temp=1", "unknown override 'temp'")]
    [InlineData("vendor/model|effort", "is not one of")]
    [InlineData("vendor/model|effort=", "is not one of")]
    [InlineData("vendor/model|effort=lwo", "effort 'lwo' is not one of")]
    [InlineData("vendor/model|effort=LOW", "effort 'LOW' is not one of")]
    [InlineData("vendor/model|effort=low|effort=high", "set more than once")]
    [InlineData("vendor/model|", "unknown override ''")]
    public void TryParse_InvalidSlot_FailsAndNamesTheCause(string? value, string expectedError)
    {
        Assert.False(BenchmarkSlot.TryParse(value, out var slot, out var error));

        Assert.Null(slot);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void TryParseAll_NoSlots_FailsAndNamesTheSetting()
    {
        Assert.False(BenchmarkSlot.TryParseAll([], out var slots, out var error));

        Assert.Empty(slots);
        Assert.Contains("OpenRouter:BenchmarkModels is empty", error);
    }

    [Fact]
    public void TryParseAll_OneInvalidSlot_FailsTheWholeList()
    {
        Assert.False(BenchmarkSlot.TryParseAll(["vendor/a", "vendor/b|effort=nope"], out _, out var error));

        Assert.Contains("vendor/b|effort=nope", error);
    }

    // The same slot twice is a valid noise measurement, not a configuration error.
    [Fact]
    public void TryParseAll_ValidSlots_KeepsOrderAndDuplicates()
    {
        Assert.True(BenchmarkSlot.TryParseAll(
            ["vendor/a|effort=low", "vendor/b", "vendor/a|effort=low"], out var slots, out var error));

        Assert.Null(error);
        Assert.Equal(["vendor/a|effort=low", "vendor/b", "vendor/a|effort=low"], slots.Select(s => s.Key));
    }
}
