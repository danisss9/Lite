using Lite.Conformance.Css21;
using static Lite.Tests.TestRunner;

namespace Lite.Tests;

/// <summary>Lane 6.H: a case's second scroll phase must survive the pass-condition route.
/// background-119…205 state "scrolled to the right" AFTER "the box is scrolled down"; the
/// route read only the first phase, so the final render never scrolled sideways and the
/// stated condition could not hold.</summary>
public static class BackgroundRouteTests
{
    [Test]
    public static void ScrollRoute_KeepsTheSecondPhase()
    {
        var stated = SelfTestOracle.Condition("css/CSS2/backgrounds/background-119.xht");
        True(stated.Check is { Scroll: { } scroll }, "background-119 must carry a scroll check");
        Equal("bottom", stated.Check!.Scroll!.To);
        Equal("right", stated.Check.Scroll.Then);
    }
}
