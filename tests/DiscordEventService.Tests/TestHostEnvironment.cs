using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace DiscordEventService.Tests;

// Minimal IHostEnvironment for constructing services directly in tests. "Test" is
// deliberately not "Development" so environment-gated behavior stays off by default.
internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "DiscordEventService.Tests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
