using AwesomeAssertions;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>How a build names itself on Settings' Database tab and in the log.</summary>
public class AppVersionTests
{
    [Test]
    [Arguments("1.0+e680d9042c88eb2c5c0c2a9d0f1df2e19f166e8f", "1.0 (commit e680d90)")]
    [Arguments("1.0+abc", "1.0 (commit abc)")]
    [Arguments("1.0", "1.0")]
    [Arguments("1.0+", "1.0")]
    [Arguments("", "Unknown")]
    [Arguments(null, "Unknown")]
    public void A_build_says_its_version_and_the_commit_it_was_published_from(string? informationalVersion, string shown) =>
        AppVersion.Describe(informationalVersion).Should().Be(shown);
}
