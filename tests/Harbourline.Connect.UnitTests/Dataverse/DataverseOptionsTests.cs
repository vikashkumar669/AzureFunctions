using Harbourline.Connect.Core;
using Harbourline.Connect.Core.Dataverse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Harbourline.Connect.UnitTests.Dataverse;

public sealed class DataverseOptionsTests
{
    private static ServiceProvider BuildProvider(string? url)
    {
        var settings = new Dictionary<string, string?>();
        if (url is not null)
        {
            settings["Dataverse:Url"] = url;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddHarbourlineCore(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void MissingUrl_FailsValidation()
    {
        using var provider = BuildProvider(url: null);

        var options = provider
            .GetRequiredService<IOptions<DataverseOptions>>();

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void NotAUrl_FailsValidation()
    {
        using var provider = BuildProvider("not-a-url");

        var options = provider
            .GetRequiredService<IOptions<DataverseOptions>>();

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void ValidUrl_Binds()
    {
        const string url = "https://harbourline-dev.crm.dynamics.com";
        using var provider = BuildProvider(url);

        var options = provider
            .GetRequiredService<IOptions<DataverseOptions>>().Value;

        Assert.Equal(url, options.Url);
    }
}
