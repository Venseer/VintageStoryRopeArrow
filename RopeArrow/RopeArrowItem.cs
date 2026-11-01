using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace RopeArrow;

public sealed class RopeArrowItem : Item
{
    private MultiTextureMeshRef? heldArrowMesh;
    private MultiTextureMeshRef? inventorySpriteMesh;

    public override void OnBeforeRender(ICoreClientAPI clientAPI, ItemStack itemStack, EnumItemRenderTarget itemRenderTarget, ref ItemRenderInfo renderInfo)
    {
        base.OnBeforeRender(clientAPI, itemStack, itemRenderTarget, ref renderInfo);

        if (itemRenderTarget == EnumItemRenderTarget.Gui)
        {
            inventorySpriteMesh ??= GetInventorySpriteMesh(clientAPI);
            if (inventorySpriteMesh != null)
            {
                renderInfo.ModelRef = inventorySpriteMesh;
            }
            return;
        }

        if (itemRenderTarget is not (EnumItemRenderTarget.HandFp or EnumItemRenderTarget.HandTp or EnumItemRenderTarget.HandTpOff)) return;

        heldArrowMesh ??= GetHeldArrowMesh(clientAPI, itemStack);
        if (heldArrowMesh != null)
        {
            renderInfo.ModelRef = heldArrowMesh;
        }
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder description, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, description, world, withDebugInfo);
        description.AppendLine(Lang.Get("ropearrow:itemdesc-arrow-rope"));
    }

    private static MultiTextureMeshRef? GetHeldArrowMesh(ICoreClientAPI capi, ItemStack itemstack)
    {
        string material = itemstack.Collectible.LastCodePart();
        Item? arrow = capi.World.GetItem(new AssetLocation("game", "arrow-" + material));
        return arrow == null ? null : capi.TesselatorManager.GetDefaultItemMeshRef(arrow);
    }

    private MultiTextureMeshRef? GetInventorySpriteMesh(ICoreClientAPI capi)
    {
        var location = new AssetLocation("ropearrow:shapes/item/ropearrow-icon.json");
        var iconShape = Vintagestory.API.Common.Shape.TryGet(capi, location);
        if (iconShape == null) return null;

        iconShape.CollectAndResolveReferences(capi.Logger, location.ToString());
        capi.Tesselator.TesselateShape(this, iconShape, out var mesh);
        return capi.Render.UploadMultiTextureMesh(mesh);
    }
}
