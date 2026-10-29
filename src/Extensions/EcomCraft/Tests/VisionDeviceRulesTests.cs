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
}
