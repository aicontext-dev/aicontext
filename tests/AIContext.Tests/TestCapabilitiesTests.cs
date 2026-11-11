using Xunit;

namespace AIContext.Tests;

public sealed class TestCapabilitiesTests
{
    [Fact]
    public void Name_EveryCapability_HasItsOwnName()
    {
        var capabilities = Enum.GetValues<LinkCapability>();

        Assert.Equal(capabilities.Length, capabilities.Select(TestCapabilities.Name).Distinct().Count());
    }

    // A job that must really execute the link tests lists their capabilities in the environment. Without this
    // check a runner that cannot create the links would only skip those tests and the job would stay green.
    [RequiredCapabilitiesFact]
    public void RequiredCapabilities_AreAvailableOnThisHost()
    {
        var byName = Enum.GetValues<LinkCapability>().ToDictionary(TestCapabilities.Name);
        var problems = new List<string>();

        foreach (var name in TestCapabilities.RequiredByEnvironment()!)
        {
            if (!byName.TryGetValue(name, out var capability))
            {
                problems.Add($"{name}: unknown capability (known: {string.Join(", ", byName.Keys)})");
            }
            else if (TestCapabilities.Unavailable(capability) is { } reason)
            {
                problems.Add($"{name}: {reason}");
            }
        }

        Assert.True(problems.Count == 0, "Required capabilities are missing: " + string.Join("; ", problems));
    }
}
