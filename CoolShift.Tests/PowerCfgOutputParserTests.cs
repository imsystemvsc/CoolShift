using CoolShift;
using Xunit;

namespace CoolShift.Tests;

public sealed class PowerCfgOutputParserTests
{
    private static readonly Guid SettingGuid = new("0cc5b647-c1df-4637-891a-dec35c318583");

    [Fact]
    public void TryParseSettingValues_UsesCurrentValuesFromEnglishOutput()
    {
        const string output = """
            Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583  (Processor performance core parking min cores)
              Minimum Possible Setting: 0x00000000
              Maximum Possible Setting: 0x00000064
              Possible Settings increment: 0x00000001
              Current AC Power Setting Index: 0x00000019
              Current DC Power Setting Index: 0x0000000a
            Power Setting GUID: 9943e905-9a30-4ec1-9b99-44dd3b76f7a2  (Processor idle promote threshold)
              Current AC Power Setting Index: 0x00000020
              Current DC Power Setting Index: 0x00000020
            """;

        var parsed = PowerCfgOutputParser.TryParseSettingValues(output, SettingGuid, out var values);

        Assert.True(parsed);
        Assert.Equal(new PowerSettingValues(25, 10), values);
    }

    [Fact]
    public void TryParseSettingValues_DoesNotDependOnEnglishLabels()
    {
        const string output = """
            GUID der Energieeinstellung: 0cc5b647-c1df-4637-891a-dec35c318583  (Minimale Kerne beim Parken)
              Minimal mögliche Einstellung: 0x00000000
              Maximal mögliche Einstellung: 0x00000064
              Schrittweite: 0x00000001
              Aktueller Wechselstrom-Einstellungsindex: 0x00000019
              Aktueller Gleichstrom-Einstellungsindex: 0x0000000a
            GUID der Energieeinstellung: 9943e905-9a30-4ec1-9b99-44dd3b76f7a2  (Leerlaufschwelle)
              Aktueller Wechselstrom-Einstellungsindex: 0x00000020
              Aktueller Gleichstrom-Einstellungsindex: 0x00000020
            """;

        var parsed = PowerCfgOutputParser.TryParseSettingValues(output, SettingGuid, out var values);

        Assert.True(parsed);
        Assert.Equal(new PowerSettingValues(25, 10), values);
    }

    [Fact]
    public void TryParseSettingValues_ReturnsFalseWhenCurrentValuesAreMissing()
    {
        const string output = """
            Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583
              Minimum Possible Setting: 0x00000000
            """;

        var parsed = PowerCfgOutputParser.TryParseSettingValues(output, SettingGuid, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParseSettingValues_ReturnsFalseWhenGuidIsMissing()
    {
        const string output = """
            Power Setting GUID: 9943e905-9a30-4ec1-9b99-44dd3b76f7a2
              Current AC Power Setting Index: 0x00000020
              Current DC Power Setting Index: 0x00000020
            """;

        var parsed = PowerCfgOutputParser.TryParseSettingValues(output, SettingGuid, out _);

        Assert.False(parsed);
    }
}
