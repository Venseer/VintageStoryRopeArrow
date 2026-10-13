using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace RopeArrow;

public sealed class BlockRopeArrowRope : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        return false;
    }

    public override void OnEntityCollide(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, Vec3d collideSpeed, bool isImpact)
    {
        base.OnEntityCollide(world, entity, pos, facing, collideSpeed, false);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (RopeArrowModSystem.Settings.DestroyWholeRopeWhenSegmentBroken)
        {
            BlockPos above = pos.UpCopy();
            for (int ropeCount = 0; ropeCount < RopeArrowModSystem.Settings.MaxRopeLength; ropeCount++)
            {
                Block aboveBlock = world.BlockAccessor.GetBlock(above);
                if (aboveBlock is BlockRopeArrowAnchor anchorBlock &&
                    world.BlockAccessor.GetBlockEntity(above) is BlockEntityRopeArrowAnchor anchor)
                {
                    anchor.PrepareForBreak(forceReturnMaterials: true);
                    anchorBlock.OnBlockBroken(world, above, byPlayer, dropQuantityMultiplier);
                    break;
                }

                if (aboveBlock is not BlockRopeArrowRope)
                {
                    break;
                }

                above.Y++;
            }
        }

        int[] ropeBlockIds =
        {
            world.GetBlock(new AssetLocation("ropearrow:rope-north"))?.BlockId ?? -1,
            world.GetBlock(new AssetLocation("ropearrow:rope-east"))?.BlockId ?? -1,
            world.GetBlock(new AssetLocation("ropearrow:rope-south"))?.BlockId ?? -1,
            world.GetBlock(new AssetLocation("ropearrow:rope-west"))?.BlockId ?? -1
        };

        BlockPos below = pos.DownCopy();
        while (Array.IndexOf(ropeBlockIds, world.BlockAccessor.GetBlock(below).BlockId) >= 0)
        {
            world.BlockAccessor.SetBlock(0, below);
            below.Y--;
        }

        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        return [];
    }
}

public sealed class BlockRopeArrowAnchor : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        return false;
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

        if (api?.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityRopeArrowAnchor anchor && anchor.HasArrowTransform)
        {
            sourceMesh = sourceMesh.Clone();
            anchor.TransformAnchorRope(sourceMesh, pos);
        }
    }

    public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
    {
        decalModelData = decalModelData.EmptyClone();
    }

    public override Cuboidf GetParticleBreakBox(IBlockAccessor blockAccess, BlockPos pos, BlockFacing facing)
    {
        if (blockAccess.GetBlockEntity(pos) is BlockEntityRopeArrowAnchor anchor && anchor.HasArrowTransform)
        {
            return anchor.GetArrowBounds(pos);
        }

        return base.GetParticleBreakBox(blockAccess, pos, facing);
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return GetArrowBoxes(blockAccessor, pos);
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return [new Cuboidf(0, 0, 0, 0, 1, 0)];
    }

    private Cuboidf[] GetArrowBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is not BlockEntityRopeArrowAnchor anchor || !anchor.HasArrowTransform)
        {
            return [];
        }

        return [anchor.GetArrowBounds(pos)];
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityRopeArrowAnchor anchor)
        {
            anchor.PrepareForBreak();
        }

        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        List<ItemStack> drops = [];
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityRopeArrowAnchor anchor)
        {
            foreach (string material in anchor.SourceArrowMaterials)
            {
                AddDrop(drops, world, $"game:arrow-{material}");
            }

            if (anchor.ReturnMaterialsOnBreak)
            {
                if(RopeArrowModSystem.ServerRequiresRustyGear) {
                    AddDrop(drops, world, "game:gear-rusty");
                }
                AddDrop(drops, world, "game:rope");
            }
        }

        return drops.ToArray();
    }

    private static void AddDrop(List<ItemStack> drops, IWorldAccessor world, string code)
    {
        Item? item = world.GetItem(new AssetLocation(code));
        if (item != null)
        {
            drops.Add(new ItemStack(item));
        }
    }
}

public sealed class BlockEntityRopeArrowAnchor : BlockEntity
{
    private const int SegmentIntervalMs = 200;

    private readonly List<BlockPos> ropePositions = new();
    private readonly List<long> arrowEntityIds = new();
    private readonly List<string> sourceArrowMaterials = new();
    private string ropeFacing = "north";
    private long tickListenerId;
    private double arrowX;
    private double arrowY;
    private double arrowZ;
    private float arrowYaw;
    private float arrowRoll;
    private bool unspooling;
    private bool breakPrepared;
    private long landingCheckListenerId;

    public bool ReturnMaterialsOnBreak { get; private set; }
    public bool HasArrowTransform { get; private set; }
    public IReadOnlyList<string> SourceArrowMaterials => sourceArrowMaterials;

    public bool HasArrowEntity(long entityId)
    {
        return arrowEntityIds.Contains(entityId);
    }

    public void SetArrowEntity(Entity arrow, string material)
    {
        if (arrowEntityIds.Contains(arrow.EntityId))
        {
            return;
        }

        arrowEntityIds.Add(arrow.EntityId);
        sourceArrowMaterials.Add(material);
        if (!HasArrowTransform)
        {
            arrowX = arrow.Pos.X;
            arrowY = arrow.Pos.InternalY;
            arrowZ = arrow.Pos.Z;
            arrowYaw = arrow.Pos.Yaw;
            arrowRoll = arrow.Pos.Roll;
            HasArrowTransform = true;
        }

        MarkDirty();
        RegisterLandingCheck();
    }

    public Cuboidf GetArrowBounds(BlockPos blockPos)
    {
        const double halfLength = 0.55;
        const double radius = 0.055;
        double horizontalPitch = Math.Cos(arrowRoll);
        double directionX = -Math.Sin(arrowYaw) * horizontalPitch;
        double directionY = Math.Sin(arrowRoll);
        double directionZ = -Math.Cos(arrowYaw) * horizontalPitch;
        double centerX = arrowX - blockPos.X;
        double centerY = arrowY - blockPos.InternalY;
        double centerZ = arrowZ - blockPos.Z;

        float minX = (float)(centerX - Math.Abs(directionX) * halfLength - radius);
        float minY = (float)(centerY - Math.Abs(directionY) * halfLength - radius);
        float minZ = (float)(centerZ - Math.Abs(directionZ) * halfLength - radius);
        float maxX = (float)(centerX + Math.Abs(directionX) * halfLength + radius);
        float maxY = (float)(centerY + Math.Abs(directionY) * halfLength + radius);
        float maxZ = (float)(centerZ + Math.Abs(directionZ) * halfLength + radius);

        return new Cuboidf(minX, minY, minZ, maxX, maxY, maxZ);
    }

    public void TransformAnchorRope(MeshData mesh, BlockPos blockPos)
    {
        float directionX = (float)(arrowX - blockPos.X - 0.5);
        float directionY = (float)(arrowY - blockPos.InternalY);
        float directionZ = (float)(arrowZ - blockPos.Z - 0.5);
        float length = MathF.Sqrt(directionX * directionX + directionY * directionY + directionZ * directionZ);
        if (length < 0.001f)
        {
            return;
        }

        float inverseLength = 1f / length;
        float[] localUp = { 0f, 1f, 0f };
        float[] targetDirection =
        {
            directionX * inverseLength,
            directionY * inverseLength,
            directionZ * inverseLength
        };
        float[] rotation = Quaternionf.RotationTo(Quaternionf.Create(), localUp, targetDirection);
        float[] rotationMatrix = Mat4f.Create();
        Vec3f baseCenter = new(0.5f, 0f, 0.5f);
        mesh.Scale(baseCenter, 1f, length, 1f);
        Mat4f.FromQuat(rotationMatrix, rotation);
        mesh.MatrixTransform(rotationMatrix, new float[4], baseCenter);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (unspooling && api.Side == EnumAppSide.Server)
        {
            RegisterUnspoolTick();
        }
        if (HasArrowTransform && api.Side == EnumAppSide.Server)
        {
            RegisterLandingCheck();
        }
    }

    public void StartUnspooling(string facing)
    {
        if (Api?.Side != EnumAppSide.Server || unspooling)
        {
            return;
        }

        ropeFacing = facing;
        unspooling = true;
        MarkDirty();
        RegisterUnspoolTick();
    }

    private void RegisterUnspoolTick()
    {
        if (tickListenerId == 0)
        {
            tickListenerId = RegisterGameTickListener(OnUnspoolTick, SegmentIntervalMs);
        }
    }

    private void RegisterLandingCheck()
    {
        if (Api?.Side == EnumAppSide.Server && landingCheckListenerId == 0)
        {
            landingCheckListenerId = RegisterGameTickListener(CheckHardLanding, 50);
        }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        tickListenerId = 0;
        landingCheckListenerId = 0;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        tickListenerId = 0;
        landingCheckListenerId = 0;
    }

    private void CheckHardLanding(float deltaTime)
    {
        if (Api?.Side != EnumAppSide.Server || !HasArrowTransform || breakPrepared)
        {
            return;
        }

        Cuboidf target = GetArrowBounds(Pos);
        foreach (IPlayer player in Api.World.GetPlayersAround(new Vec3d(arrowX, arrowY, arrowZ), 0.8f, 0.8f))
        {
            EntityPos playerPosition = player.Entity.Pos;
            if (playerPosition.Motion.Y >= -0.35 ||
                playerPosition.X < Pos.X + target.X1 - 0.3 || playerPosition.X > Pos.X + target.X2 + 0.3 ||
                playerPosition.InternalY < Pos.InternalY + target.Y1 - 0.3 || playerPosition.InternalY > Pos.InternalY + target.Y2 + 0.3 ||
                playerPosition.Z < Pos.Z + target.Z1 - 0.3 || playerPosition.Z > Pos.Z + target.Z2 + 0.3)
            {
                continue;
            }

            Block.OnBlockBroken(Api.World, Pos, player);
            return;
        }
    }

    private void OnUnspoolTick(float deltaTime)
    {
        if (!unspooling || ropePositions.Count >= Math.Clamp(RopeArrowModSystem.Settings.MaxRopeLength, 1, 64))
        {
            StopUnspooling();
            return;
        }

        BlockPos nextPosition = new(Pos.X, Pos.Y - ropePositions.Count - 1, Pos.Z, Pos.dimension);
        if (Api.World.BlockAccessor.GetBlock(nextPosition).BlockId != 0)
        {
            StopUnspooling();
            return;
        }

        Block? ropeBlock = Api.World.GetBlock(new AssetLocation($"ropearrow:rope-{ropeFacing}"));
        if (ropeBlock == null)
        {
            StopUnspooling();
            return;
        }

        Api.World.BlockAccessor.SetBlock(ropeBlock.BlockId, nextPosition);
        ropePositions.Add(nextPosition);
        MarkDirty();
    }

    private void StopUnspooling()
    {
        unspooling = false;
        if (tickListenerId != 0)
        {
            UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }

        MarkDirty();
    }

    public void PrepareForBreak(bool forceReturnMaterials = false)
    {
        if (breakPrepared)
        {
            return;
        }

        ReturnMaterialsOnBreak = forceReturnMaterials || (HasAllRopeSegments() &&
            Api.World.Rand.NextDouble() * 100 < Math.Clamp(RopeArrowModSystem.Settings.RefundChancePercent, 0, 100));
        breakPrepared = true;
        StopUnspooling();

        int ropeBlockId = Api.World.GetBlock(new AssetLocation($"ropearrow:rope-{ropeFacing}"))?.BlockId ?? -1;
        foreach (BlockPos ropePosition in ropePositions)
        {
            if (Api.World.BlockAccessor.GetBlock(ropePosition).BlockId == ropeBlockId)
            {
                Api.World.BlockAccessor.SetBlock(0, ropePosition);
            }
        }

        foreach (long arrowEntityId in arrowEntityIds)
        {
            Api.World.GetEntityById(arrowEntityId)?.Die(EnumDespawnReason.Removed);
        }
    }

    private bool HasAllRopeSegments()
    {
        int ropeBlockId = Api.World.GetBlock(new AssetLocation($"ropearrow:rope-{ropeFacing}"))?.BlockId ?? -1;
        foreach (BlockPos ropePosition in ropePositions)
        {
            if (Api.World.BlockAccessor.GetBlock(ropePosition).BlockId != ropeBlockId)
            {
                return false;
            }
        }

        return true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("ropeArrowUnspooling", unspooling);
        tree.SetInt("ropeArrowAttachedArrowCount", arrowEntityIds.Count);
        for (int i = 0; i < arrowEntityIds.Count; i++)
        {
            tree.SetLong($"ropeArrowAttachedArrowId{i}", arrowEntityIds[i]);
            tree.SetString($"ropeArrowAttachedArrowMaterial{i}", sourceArrowMaterials[i]);
        }
        tree.SetString("ropeArrowFacing", ropeFacing);
        tree.SetBool("ropeArrowHasTransform", HasArrowTransform);
        tree.SetDouble("ropeArrowX", arrowX);
        tree.SetDouble("ropeArrowY", arrowY);
        tree.SetDouble("ropeArrowZ", arrowZ);
        tree.SetFloat("ropeArrowYaw", arrowYaw);
        tree.SetFloat("ropeArrowRoll", arrowRoll);
        tree.SetInt("ropeArrowSegmentCount", ropePositions.Count);
        for (int i = 0; i < ropePositions.Count; i++)
        {
            tree.SetInt($"ropeArrowSegment{i}X", ropePositions[i].X);
            tree.SetInt($"ropeArrowSegment{i}Y", ropePositions[i].Y);
            tree.SetInt($"ropeArrowSegment{i}Z", ropePositions[i].Z);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        unspooling = tree.GetBool("ropeArrowUnspooling");
        arrowEntityIds.Clear();
        sourceArrowMaterials.Clear();
        int attachedArrowCount = tree.GetInt("ropeArrowAttachedArrowCount");
        for (int i = 0; i < attachedArrowCount; i++)
        {
            arrowEntityIds.Add(tree.GetLong($"ropeArrowAttachedArrowId{i}"));
            sourceArrowMaterials.Add(tree.GetString($"ropeArrowAttachedArrowMaterial{i}", "crude"));
        }

        if (attachedArrowCount == 0)
        {
            long legacyArrowEntityId = tree.GetLong("ropeArrowEntityId");
            if (legacyArrowEntityId != 0)
            {
                arrowEntityIds.Add(legacyArrowEntityId);
                sourceArrowMaterials.Add(tree.GetString("ropeArrowSourceMaterial", "crude"));
            }
        }

        ropeFacing = tree.GetString("ropeArrowFacing", "north");
        HasArrowTransform = tree.GetBool("ropeArrowHasTransform");
        arrowX = tree.GetDouble("ropeArrowX");
        arrowY = tree.GetDouble("ropeArrowY");
        arrowZ = tree.GetDouble("ropeArrowZ");
        arrowYaw = tree.GetFloat("ropeArrowYaw");
        arrowRoll = tree.GetFloat("ropeArrowRoll");
        ropePositions.Clear();
        int count = tree.GetInt("ropeArrowSegmentCount");
        for (int i = 0; i < count; i++)
        {
            ropePositions.Add(new BlockPos(
                tree.GetInt($"ropeArrowSegment{i}X"),
                tree.GetInt($"ropeArrowSegment{i}Y"),
                tree.GetInt($"ropeArrowSegment{i}Z"),
                Pos.dimension
            ));
        }
    }
}