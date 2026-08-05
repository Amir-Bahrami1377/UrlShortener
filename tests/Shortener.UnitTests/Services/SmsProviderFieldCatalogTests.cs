using FluentAssertions;
using Shortener.Application.Services;

namespace Shortener.UnitTests.Services;

public sealed class SmsProviderFieldCatalogTests
{
    [Fact]
    public void GetFields_Kavenegar_RequiresOnlyApiKey()
    {
        var fields = SmsProviderFieldCatalog.GetFields("kavenegar");

        fields.Should().ContainSingle();
        fields[0].Key.Should().Be("ApiKey");
        fields[0].IsRequired.Should().BeTrue();
        fields[0].IsSecret.Should().BeTrue();
    }

    [Theory]
    [InlineData("melipayamak")]
    [InlineData("farazsms")]
    public void GetFields_UsernamePasswordProviders_RequireBothFields(string providerCode)
    {
        var fields = SmsProviderFieldCatalog.GetFields(providerCode);

        fields.Should().HaveCount(2);
        fields.Select(f => f.Key).Should().BeEquivalentTo(["Username", "Password"]);
        fields.Should().OnlyContain(f => f.IsRequired && f.IsSecret);
    }

    [Theory]
    [InlineData("fake")]
    [InlineData("unknown-provider")]
    [InlineData("")]
    public void GetFields_FakeOrUnknownProvider_ReturnsNoFields(string providerCode)
    {
        SmsProviderFieldCatalog.GetFields(providerCode).Should().BeEmpty();
    }
}
