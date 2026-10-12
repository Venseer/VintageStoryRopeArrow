using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace RopeArrow;

public sealed class RopeArrowBehavior : EntityBehavior
{
    private bool finished;

    public RopeArrowBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName()
    {
        return "ropearrow";
    }

    public override void OnGameTick(float deltaTime)
    {
        base.OnGameTick(deltaTime);

        if (finished || entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (entity is not IProjectile projectile || !projectile.Stuck)
        {
            return;
        }

        projectile.Collectible = false;

        BlockPos anchorPosition = entity.Pos.AsBlockPos;
        BlockPos belowBlockCheck = anchorPosition.DownCopy();
        string facing = GetRopeFacing(entity);
        if (entity.World.BlockAccessor.GetBlock(anchorPosition).BlockId != 0)
        {
            if (entity.World.BlockAccessor.GetBlockEntity(anchorPosition) is BlockEntityRopeArrowAnchor existingAnchor &&
                existingAnchor.HasArrowEntity(entity.EntityId))
            {
                projectile.Collectible = false;
                finished = true;
                return;
            }

            projectile.Collectible = true;
            finished = true;
            return;
        }

        if (entity.World.BlockAccessor.GetBlock(belowBlockCheck).BlockId != 0)
        {
            projectile.Collectible = true;
            finished = true;
            return;
        }

        if (!IsHitBlockAllowed(anchorPosition, out BlockFacing hitFace))
        {
            BounceOff(projectile, hitFace);
            finished = true;
            return;
        }

        Block? anchorBlock = entity.World.GetBlock(new AssetLocation($"ropearrow:ropearrowanchor-{facing}"));
        if (anchorBlock == null)
        {
            finished = true;
            return;
        }

        entity.World.BlockAccessor.SetBlock(anchorBlock.BlockId, anchorPosition);
        if (entity.World.BlockAccessor.GetBlockEntity(anchorPosition) is BlockEntityRopeArrowAnchor anchor)
        {
            anchor.SetArrowEntity(entity, GetSourceArrowMaterial(projectile));
            anchor.StartUnspooling(facing);
        }
        else
        {
            entity.World.BlockAccessor.SetBlock(0, anchorPosition);
            projectile.Collectible = true;
            finished = true;
            return;
        }

        finished = true;
    }

    private void BounceOff(IProjectile projectile, BlockFacing hitFace)
    {
        // Un-sticking the projectile is unreliable, so replace it with a dropped item.
        Vec3d away = new(-hitFace.Normali.X * 0.05, 0.05 - hitFace.Normali.Y * 0.05, -hitFace.Normali.Z * 0.05);
        ItemStack? stack = projectile.ProjectileStack;
        if (stack != null)
        {
            entity.World.SpawnItemEntity(stack.Clone(), entity.Pos.XYZ, away);
        }

        entity.Die(EnumDespawnReason.Removed);
    }

    private bool IsHitBlockAllowed(BlockPos anchorPosition, out BlockFacing hitFace)
    {
        Block hitBlock = FindHitBlock(anchorPosition, out hitFace);
        if (hitBlock.BlockId == 0)
        {
            return true;
        }

        RopeArrowSettings settings = RopeArrowModSystem.Settings;
        return MatchesAny(settings.AllowedBlocks, hitBlock.Code) || !MatchesAny(settings.DisallowedBlocks, hitBlock.Code);
    }

    // The arrow rests in an air block; the hit block is the solid neighbour whose face is closest to it.
    private Block FindHitBlock(BlockPos anchorPosition, out BlockFacing hitFace)
    {
        double fx = entity.Pos.X - anchorPosition.X;
        double fy = entity.Pos.Y - anchorPosition.Y;
        double fz = entity.Pos.Z - anchorPosition.Z;

        (BlockFacing Face, double Distance)[] candidates =
        {
            (BlockFacing.WEST, fx), (BlockFacing.EAST, 1 - fx),
            (BlockFacing.DOWN, fy), (BlockFacing.UP, 1 - fy),
            (BlockFacing.NORTH, fz), (BlockFacing.SOUTH, 1 - fz),
        };
        Array.Sort(candidates, (a, b) => a.Distance.CompareTo(b.Distance));

        foreach (var (face, _) in candidates)
        {
            Block neighbour = entity.World.BlockAccessor.GetBlock(anchorPosition.AddCopy(face));
            if (neighbour.BlockId != 0)
            {
                hitFace = face;
                return neighbour;
            }
        }

        hitFace = BlockFacing.DOWN;
        return entity.World.BlockAccessor.GetBlock(0);
    }

    private static bool MatchesAny(System.Collections.Generic.IEnumerable<string>? patterns, AssetLocation code)
    {
        if (patterns == null)
        {
            return false;
        }

        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            string trimmed = pattern.Trim();
            if (WildcardUtil.Match(trimmed, code.ToString()) || WildcardUtil.Match(trimmed, code.Path))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetRopeFacing(Entity projectile)
    {
        double motionX = -Math.Sin(projectile.Pos.Yaw);
        double motionZ = -Math.Cos(projectile.Pos.Yaw);

        if (Math.Abs(motionX) > Math.Abs(motionZ))
        {
            return motionX > 0 ? "east" : "west";
        }

        return motionZ > 0 ? "south" : "north";
    }

    private static string GetSourceArrowMaterial(IProjectile projectile)
    {
        return projectile.ProjectileStack?.Collectible.Variant?["material"] ?? "crude";
    }
}