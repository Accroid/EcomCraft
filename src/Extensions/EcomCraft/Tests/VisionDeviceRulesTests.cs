using EcomCraftStudio.MagicPrompt;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>The vision model device (F71): "auto" tries the GPU once with a time limit and then keeps what it learned; the fixed choices never change.</summary>
public class VisionDeviceRulesTests
{
    [Fact]
    public void UnknownSettingsReadAsAuto()
    {
        Assert.Equal(VisionDeviceRules.Auto, VisionDeviceRules.Normalize(null));
        Assert.Equal(VisionDeviceRules.Auto, VisionDeviceRules.Normalize(""));
        Assert.Equal(VisionDeviceRules.Auto, VisionDeviceRules.Normalize("vulkan"));
        Assert.Equal(VisionDeviceRules.Cpu, VisionDeviceRules.Normalize(" CPU "));
        Assert.Equal(VisionDeviceRules.Gpu, VisionDeviceRules.Normalize("gpu"));
    }

    [Fact]
    public void AutoProbesOnceThenKeepsTheResult()
    {
        Assert.Equal((false, true), VisionDeviceRules.Plan("auto", null));
        Assert.Equal((true, false), VisionDeviceRules.Plan("auto", true));
        Assert.Equal((false, false), VisionDeviceRules.Plan("auto", false));
    }

    [Fact]
    public void FixedChoicesIgnoreWhatAutoLearned()
    {
        Assert.Equal((true, false), VisionDeviceRules.Plan("cpu", null));
        Assert.Equal((true, false), VisionDeviceRules.Plan("cpu", false));
        Assert.Equal((false, false), VisionDeviceRules.Plan("gpu", null));
        Assert.Equal((false, false), VisionDeviceRules.Plan("gpu", true));
    }

    [Fact]
    public void SelfTestPicksTheFasterDeviceThatAnswered()
    {
        Assert.Null(VisionDeviceRules.Choose(null, null));
        Assert.Equal(VisionDeviceRules.Cpu, VisionDeviceRules.Choose(null, 15));
        Assert.Equal(VisionDeviceRules.Gpu, VisionDeviceRules.Choose(4, null));
        Assert.Equal(VisionDeviceRules.Gpu, VisionDeviceRules.Choose(4, 15));
        Assert.Equal(VisionDeviceRules.Cpu, VisionDeviceRules.Choose(40, 15));
    }

    [Fact]
    public void WaitFollowsTheMeasuredSpeedWithinLimits()
    {
        Assert.Equal(90, VisionDeviceRules.TimeoutFor(5, 180));
        Assert.Equal(150, VisionDeviceRules.TimeoutFor(30, 180));
        Assert.Equal(120, VisionDeviceRules.TimeoutFor(60, 120));
    }

    [Fact]
    public void ResultsSaveAndReadBack()
    {
        Dictionary<string, VisionDeviceRules.Record> records = [];
        DateTime now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        string key = VisionDeviceRules.Key("0.40.13", "qwen3-vl:2b-instruct", "AMD Radeon RX 5600 XT 31.0.1");
        VisionDeviceRules.Put(records, key, new VisionDeviceRules.Record(VisionDeviceRules.Cpu, 15.24, now));
        Dictionary<string, VisionDeviceRules.Record> read = VisionDeviceRules.Parse(VisionDeviceRules.Serialize(records));
        Assert.Single(read);
        Assert.Equal(VisionDeviceRules.Cpu, read[key].Device);
        Assert.Equal(15.2, read[key].Seconds);
        Assert.Equal(now, read[key].Checked.ToUniversalTime());
    }

    [Fact]
    public void InvalidEntriesAreSkippedAndDamagedFilesThrow()
    {
        Dictionary<string, VisionDeviceRules.Record> read = VisionDeviceRules.Parse(
            "{\"entries\":{\"a\":{\"device\":\"vulkan\",\"seconds\":3},\"b\":{\"device\":\"gpu\"},\"c\":{\"device\":\"gpu\",\"seconds\":2.5}}}");
        Assert.Equal(["c"], read.Keys.ToList());
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => VisionDeviceRules.Parse("{broken"));
    }

    [Fact]
    public void ChangingModelVersionOrGraphicsGivesANewKey()
    {
        string key = VisionDeviceRules.Key("0.40.13", "qwen3-vl:2b-instruct", "GPU 1.0");
        Assert.NotEqual(key, VisionDeviceRules.Key("0.40.14", "qwen3-vl:2b-instruct", "GPU 1.0"));
        Assert.NotEqual(key, VisionDeviceRules.Key("0.40.13", "qwen3-vl:4b-instruct", "GPU 1.0"));
        Assert.NotEqual(key, VisionDeviceRules.Key("0.40.13", "qwen3-vl:2b-instruct", "GPU 1.1"));
    }

    [Fact]
    public void OldestResultsAreDroppedBeyondTheLimit()
    {
        Dictionary<string, VisionDeviceRules.Record> records = [];
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < VisionDeviceRules.MaxRecords + 3; i++)
        {
            VisionDeviceRules.Put(records, $"k{i}", new VisionDeviceRules.Record(VisionDeviceRules.Gpu, 3, start.AddHours(i)));
        }
        Assert.Equal(VisionDeviceRules.MaxRecords, records.Count);
        Assert.False(records.ContainsKey("k0"));
        Assert.True(records.ContainsKey($"k{VisionDeviceRules.MaxRecords + 2}"));
    }
}
