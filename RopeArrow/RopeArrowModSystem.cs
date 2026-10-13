using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace RopeArrow;

public class RopeArrowModSystem : ModSystem
{
	private const string ConfigFileName = "ropearrow.json";

	public static RopeArrowSettings Settings { get; private set; } = new();

	public static bool ServerRequiresRustyGear { get; private set; }

	private static readonly string[] ArrowTypes =
    [
        "crude",
        "flint",
        "copper",
        "tinbronze",
        "bismuthbronze",
        "blackbronze",
        "gold",
        "silver",
        "iron",
        "meteoriciron",
        "steel",
        "obsidian"
    ];

	public override void Start(ICoreAPI api)
	{
		if (!TryRegisterManagedSettings(api))
		{
			LoadNativeSettings(api);
		}

		api.RegisterEntityBehaviorClass("ropearrow", typeof(RopeArrowBehavior));
		api.RegisterItemClass("RopeArrowItem", typeof(RopeArrowItem));
		api.RegisterBlockClass("RopeArrowAnchor", typeof(BlockRopeArrowAnchor));
		api.RegisterBlockClass("RopeArrowRope", typeof(BlockRopeArrowRope));
		api.RegisterBlockEntityClass("RopeArrowAnchor", typeof(BlockEntityRopeArrowAnchor));
	}

	public override void StartServerSide(ICoreServerAPI api)
	{
		base.StartServerSide(api);
		try
		{
			bool ropeArrowRequiresRustyGear = Settings.RopeArrowRequiresRustyGear;
			ServerRequiresRustyGear = ropeArrowRequiresRustyGear;
			RegisterArrowRecipes(api, ropeArrowRequiresRustyGear);
		}
		catch (Exception exception)
		{
			api.Logger.Error($"[Rope Arrow] Error during server-side initialization: {exception}");
		}
	}

	private static bool TryRegisterManagedSettings(ICoreAPI api)
	{
		if (api.ModLoader.IsModEnabled("configlib"))
		{
			return RegisterManagedSettings(api, "ConfigLib.ConfigLibModSystem");
		}

		return api.ModLoader.IsModEnabled("configkit") && RegisterManagedSettings(api, "ConfigKit.ConfigKitModSystem");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool RegisterManagedSettings(ICoreAPI api, string modSystemTypeName)
	{
		ModSystem? configSystem = api.ModLoader.GetModSystem(modSystemTypeName);
		if (configSystem == null)
		{
			api.Logger.Warning($"[Rope Arrow] Could not find {modSystemTypeName}; using the local config file instead.");
			return false;
		}

		Type[] signature = { typeof(string), typeof(object), typeof(string), typeof(Action), typeof(Action<string>), typeof(Action) };
		MethodInfo? register = configSystem.GetType().GetMethod("RegisterCustomManagedConfig", signature);
		if (register == null)
		{
			api.Logger.Warning($"[Rope Arrow] {modSystemTypeName} does not expose the managed config API; using the local config file instead.");
			return false;
		}

		try
		{
			register.Invoke(configSystem, ["ropearrow", Settings, ConfigFileName, null, null, null]);
			return true;
		}
		catch (Exception exception)
		{
			api.Logger.Error($"[Rope Arrow] Could not register settings with {modSystemTypeName}: {exception}");
			return false;
		}
	}

	private static void LoadNativeSettings(ICoreAPI api)
	{
		try
		{
			Settings = api.LoadModConfig<RopeArrowSettings>(ConfigFileName) ?? new RopeArrowSettings();
			api.StoreModConfig(Settings, ConfigFileName);
		}
		catch (Exception exception)
		{
			api.Logger.Warning($"[Rope Arrow] Could not load {ConfigFileName}; using defaults. {exception.Message}");
			Settings = new RopeArrowSettings();
		}
	}

	private void RegisterArrowRecipes(ICoreServerAPI api, bool requireRustyGear)
    {
        foreach (string arrowType in ArrowTypes)
        {
            string pattern = requireRustyGear ? "AGR" : "AR";

            var ingredients =
                new Dictionary<string, CraftingRecipeIngredient>
                {
                    ["A"] = new CraftingRecipeIngredient
                    {
                        Type = EnumItemClass.Item,
                        Code = new AssetLocation(
                            "game", $"arrow-{arrowType}"),
                        Quantity = 1
                    },
                    ["R"] = new CraftingRecipeIngredient
                    {
                        Type = EnumItemClass.Item,
                        Code = new AssetLocation("game", "rope"),
                        Quantity = 1
                    }
                };

            if (requireRustyGear)
            {
                ingredients["G"] = new CraftingRecipeIngredient
                {
                    Type = EnumItemClass.Item,
                    Code = new AssetLocation("game", "gear-rusty"),
                    Quantity = 1
                };
            }

            var recipe = new GridRecipe
            {
				Name = $"arrow-rope-{arrowType}",
                IngredientPattern = pattern,
                Width = requireRustyGear ? 3 : 2,
                Height = 1,
                Ingredients = ingredients,
                Output = new CraftingRecipeIngredient
                {
                    Type = EnumItemClass.Item,
                    Code = new AssetLocation(
                        "ropearrow", $"arrow-rope-{arrowType}"),
                    Quantity = 1
                }
            };
			if (!recipe.Resolve(api.World, "ropearrow"))
			{
				api.Logger.Warning($"[Rope Arrow] Failed to resolve recipe for {arrowType}");
				continue;
			}
            api.RegisterCraftingRecipe(recipe);
        }
	}
}
