using Inventory.Api.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Inventory.Api.Tests;

public class SeatCriteriaParserTests
{
    [Fact]
    public async Task ParseAsync_ReturnsCriteria_WhenLlmReturnsValidJson()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"maxPrice": 60, "partySize": 2, "preferAisle": true, "areaPreference": "front"}""");

        var parser = new SeatCriteriaParser(llm.Object, NullLogger<SeatCriteriaParser>.Instance);
        var (criteria, usedFallback) = await parser.ParseAsync("2 seats near the front, aisle, under $60", CancellationToken.None);

        Assert.False(usedFallback);
        Assert.Equal(60, criteria.MaxPrice);
        Assert.Equal(2, criteria.PartySize);
        Assert.True(criteria.PreferAisle);
        Assert.Equal("front", criteria.AreaPreference);
    }

    [Fact]
    public async Task ParseAsync_FallsBackToDefaults_WhenLlmReturnsNull()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // simulates LLM unreachable / circuit open

        var parser = new SeatCriteriaParser(llm.Object, NullLogger<SeatCriteriaParser>.Instance);
        var (criteria, usedFallback) = await parser.ParseAsync("anything", CancellationToken.None);

        Assert.True(usedFallback);
        Assert.Equal(1, criteria.PartySize);
        Assert.Equal("any", criteria.AreaPreference);
    }

    [Fact]
    public async Task ParseAsync_FallsBackToDefaults_WhenLlmReturnsMalformedJson()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Sure! Here's what I found: not actually JSON at all.");

        var parser = new SeatCriteriaParser(llm.Object, NullLogger<SeatCriteriaParser>.Instance);
        var (criteria, usedFallback) = await parser.ParseAsync("anything", CancellationToken.None);

        Assert.True(usedFallback);
        Assert.Equal(1, criteria.PartySize);
    }

    [Fact]
    public async Task ParseAsync_ExtractsJson_WhenLlmWrapsItInProseOrCodeFences()
    {
        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                Here you go:
                ```json
                {"maxPrice": 40, "partySize": 4, "preferAisle": false, "areaPreference": "back"}
                ```
                """);

        var parser = new SeatCriteriaParser(llm.Object, NullLogger<SeatCriteriaParser>.Instance);
        var (criteria, usedFallback) = await parser.ParseAsync("4 seats at the back, under $40", CancellationToken.None);

        Assert.False(usedFallback);
        Assert.Equal(4, criteria.PartySize);
        Assert.Equal("back", criteria.AreaPreference);
    }

    [Fact]
    public void Sanitize_ClampsPartySize_AndRejectsNegativePrice()
    {
        var criteria = new SeatCriteria { PartySize = 999, MaxPrice = -10, AreaPreference = "spaceship" };

        criteria.Sanitize();

        Assert.Equal(10, criteria.PartySize); // clamped to max
        Assert.Null(criteria.MaxPrice);        // negative price rejected
        Assert.Equal("any", criteria.AreaPreference); // invalid value rejected
    }
}
