using DiscordEventService.Services;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class UpsertResultTests
{
    [Fact]
    public void Success_WithValue_CarriesValueAndNoFailureReason()
    {
        var id = Guid.NewGuid();

        var result = UpsertResult<Guid>.Success(id);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Failure_WithReason_HasNoValueAndCarriesReason()
    {
        var result = UpsertResult<Guid>.Failure("lost the row");

        Assert.False(result.IsSuccess);
        Assert.Equal(Guid.Empty, result.Value);
        Assert.Equal("lost the row", result.FailureReason);
    }
}
