using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace RopeArrow;

public sealed class RopeArrowSettings
{
    [Description("Maximum number of rope segments created by a rope arrow.")]
    [Range(1, 64)]
    [DefaultValue(12)]
    public int MaxRopeLength = 12;

    [Description("Chance to refund the rope and rusty gear when the anchor is broken with all rope segments intact.")]
    [Range(0, 100)]
    [DefaultValue(100)]
    public float RefundChancePercent = 100;

    [Description("Breaking any rope segment also breaks the anchor and returns its components.")]
    [DefaultValue(false)]
    public bool DestroyWholeRopeWhenSegmentBroken = false;

    [Description("Whether the rope arrow requires a rusty gear to craft. Server restart is required for changes to take effect.")]
    [DefaultValue(true)]
    public bool RopeArrowRequiresRustyGear = true;

    [Description("Block codes the rope arrow can attach to; supports wildcards (e.g. *, rock-*, game:log-*). Takes priority over disallowed blocks.")]
    [DefaultValue(new string[] { "*" })]
    public string[] AllowedBlocks = ["*"];

    [Description("Block codes the rope arrow can not attach to; supports wildcards (e.g. rock-*). Ignored when matched by an allowed block.")]
    [DefaultValue(new string[0])]
    public string[] DisallowedBlocks = [];
}