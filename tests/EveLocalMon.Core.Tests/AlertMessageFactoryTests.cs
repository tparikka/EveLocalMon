using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests;

public class AlertMessageFactoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ThreatBreakdown Breakdown(int enemy = 0, int neutral = 0, int unknown = 0, int extraNoIcon = 0) =>
        new(enemy, neutral, unknown, extraNoIcon);

    [Fact]
    public void FirstHostileReadsAsAnAppearance()
    {
        AlertMessage message = AlertMessageFactory.Create(
            new ThreatIncreased(0, 2, Breakdown(enemy: 1, neutral: 1), Now));

        Assert.Equal(AlertKind.ThreatIncreased, message.Kind);
        Assert.Equal("EVE: hostile in Local", message.Title);
        Assert.Equal("1 red, 1 neutral — 2 threats visible", message.Body);
        Assert.Equal(AlertPriority.Urgent, message.Priority);
    }

    [Fact]
    public void FurtherHostilesReadAsAnIncrease()
    {
        AlertMessage message = AlertMessageFactory.Create(
            new ThreatIncreased(2, 3, Breakdown(enemy: 2, neutral: 1), Now));

        Assert.Equal("EVE: more hostiles in Local", message.Title);
        Assert.Equal("+1 (2 red, 1 neutral) — now 3 threats", message.Body);
    }

    [Fact]
    public void SingleThreatIsNotPluralised()
    {
        AlertMessage message = AlertMessageFactory.Create(new ThreatIncreased(0, 1, Breakdown(enemy: 1), Now));

        Assert.Equal("1 red — 1 threat visible", message.Body);
    }

    [Fact]
    public void EmptyCategoriesAreLeftOut()
    {
        AlertMessage message = AlertMessageFactory.Create(new ThreatIncreased(0, 1, Breakdown(enemy: 1), Now));

        Assert.DoesNotContain("neutral", message.Body);
        Assert.DoesNotContain("unknown", message.Body);
    }

    [Fact]
    public void PilotsWithNoIconCountAsNeutrals()
    {
        AlertMessage message = AlertMessageFactory.Create(
            new ThreatIncreased(0, 3, Breakdown(neutral: 1, extraNoIcon: 2), Now));

        Assert.Equal("3 neutral — 3 threats visible", message.Body);
    }

    [Theory]
    [InlineData(1, "1 unknown icon")]
    [InlineData(2, "2 unknown icons")]
    public void UnknownIconsAreCalledUnidentifiedNotHostile(int count, string expected)
    {
        AlertMessage message = AlertMessageFactory.Create(new ThreatIncreased(0, count, Breakdown(unknown: count), Now));

        Assert.Contains(expected, message.Body);
    }

    [Fact]
    public void LocalClearIsQuietAndUnambiguous()
    {
        AlertMessage message = AlertMessageFactory.Create(new LocalClear(Now));

        Assert.Equal("EVE: Local clear", message.Title);
        Assert.Equal("No threats visible.", message.Body);
        Assert.Equal(AlertPriority.Low, message.Priority);
    }

    [Fact]
    public void OverflowSaysWhatCannotBeConfirmed()
    {
        AlertMessage message = AlertMessageFactory.Create(
            new ReadabilityChanged(Readability.Full, Readability.Overflow, MemberCount: 100, VisibleRows: 56, Now));

        Assert.Equal(AlertKind.Unreadable, message.Kind);
        Assert.Equal("EVE: Local unreadable", message.Title);
        Assert.Equal("Local too long to read: 100 pilots, 56 visible. Cannot confirm Local is clear.", message.Body);
        Assert.Equal(AlertPriority.High, message.Priority);
    }

    [Fact]
    public void OverflowWithoutAReadableCountSaysSo()
    {
        AlertMessage message = AlertMessageFactory.Create(
            new ReadabilityChanged(Readability.Full, Readability.Overflow, MemberCount: null, VisibleRows: 56, Now));

        Assert.Contains("member count unreadable", message.Body);
        Assert.Contains("Cannot confirm Local is clear.", message.Body);
    }

    [Fact]
    public void NotVisibleExplainsTheLikelyCause()
    {
        AlertMessage message = AlertMessageFactory.Create(
            new ReadabilityChanged(Readability.Full, Readability.NotVisible, MemberCount: null, VisibleRows: 0, Now));

        Assert.Equal("EVE: Local unreadable", message.Title);
        Assert.Contains("covered, minimised or mis-calibrated", message.Body);
        Assert.Contains("Cannot confirm Local is clear.", message.Body);
    }

    [Fact]
    public void BecomingReadableAgainIsGoodNews()
    {
        AlertMessage message = AlertMessageFactory.Create(
            new ReadabilityChanged(Readability.Overflow, Readability.Full, MemberCount: 41, VisibleRows: 41, Now));

        Assert.Equal(AlertKind.Readable, message.Kind);
        Assert.Equal("EVE: Local readable again", message.Title);
        Assert.Equal("41 pilots, all visible.", message.Body);
        Assert.Equal(AlertPriority.Low, message.Priority);
    }

    [Fact]
    public void PilotsJoiningWhileUnreadableSaysTheyWereNotChecked()
    {
        AlertMessage message = AlertMessageFactory.Create(new MemberCountIncreasedWhileUnreadable(100, 103, Now));

        Assert.Equal("EVE: pilots joined, Local unreadable", message.Title);
        Assert.Equal("Local 100 → 103 while unreadable. New pilots not checked.", message.Body);
        Assert.Equal(AlertPriority.High, message.Priority);
    }

    /// <summary>The invariant from doc/PLAN.md §4.6: "I can't read Local" must never be phrased as
    /// a confirmed threat, and must never be phrased as all-clear.</summary>
    [Fact]
    public void UnreadableIsNeverWordedAsAThreatOrAsAllClear()
    {
        AlertEvent[] unreadableEvents =
        [
            new ReadabilityChanged(Readability.Full, Readability.Overflow, 100, 56, Now),
            new ReadabilityChanged(Readability.Full, Readability.NotVisible, null, 0, Now),
            new MemberCountIncreasedWhileUnreadable(100, 103, Now),
        ];

        foreach (AlertEvent alertEvent in unreadableEvents)
        {
            AlertMessage message = AlertMessageFactory.Create(alertEvent);

            Assert.Contains("unreadable", message.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hostile", message.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("No threats visible", message.Body, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                message.Body.Contains("Cannot confirm", StringComparison.OrdinalIgnoreCase)
                || message.Body.Contains("not checked", StringComparison.OrdinalIgnoreCase),
                $"'{message.Body}' must say what could not be confirmed.");
        }
    }
}
