using Xunit;

namespace DiscordEventService.Tests;

// HealthCheckJob keeps its alert-cooldown and drop-streak debounce state in assembly-wide
// statics, so every test class that calls ExecuteAsync mutates shared state (streaks are
// wiped for event types not currently dropped). One non-parallel collection keeps those
// classes from interleaving; PostgresFixture isolates only the database, not these statics.
[CollectionDefinition("HealthCheckJobStatics", DisableParallelization = true)]
public sealed class HealthCheckJobStaticsCollection;
