using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Core;
using Mafi.Base;
using Mafi.Collections.ImmutableCollections;
using Mafi.Curves;
using Mafi.Core.Entities.Animations;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Mods;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Trains;
using Mafi.Localization;
using Mafi.Base.Prototypes.Trains;

namespace ElevationPP.Stations;

/// <summary>
/// Registers elevated variants of the train stations. Rather than hard-coding each type, it
/// iterates every existing station proto at runtime and clones it — so electrified, molten and any
/// DLC variants are picked up automatically. Each clone gets an elevated track trajectory, a
/// UsingPillar footprint, a widened placement-height range, the source proto's own icon, and is
/// filed under a new "Elevated stations" sub-tab next to Stations. Normal and electrified variants
/// of the same type are combined under one toolbar slot (with the variants in its popup), just like
/// the vanilla Stations tab.
/// </summary>
internal class ElevatedStationData : IModData
{
    private static readonly Proto.ID ELEVATED_CATEGORY_ID = new Proto.ID("ElevationPP_ElevatedStations");
    private static readonly LocStr1 ELEVATED_PREFIX =
        Loc.Str1("ElevationPP_ElevatedPrefix", "Elevated {0}", "Name prefix for elevated train station variants.");
    private const int PLACEMENT_HEIGHT_MAX = 16;

    public void RegisterData(ProtoRegistrator registrator)
    {
        ProtosDb db = registrator.PrototypesDb;

        // 1. New "Elevated stations" sub-tab under the Trains category, next to Stations.
        if (db.TryGetProto<ToolbarCategoryProto>(Ids.ToolbarCategories.Trains, out var trainsCat))
        {
            db.Add(new ToolbarCategoryProto(ELEVATED_CATEGORY_ID,
                Proto.CreateStr(ELEVATED_CATEGORY_ID, "Elevated stations", "Elevated train station modules.", null),
                1.5f, "Assets/Base/Icons/Toolbar/TrainStations.svg", false, "", null, null, trainsCat));
        }
        else
        {
            Log.Error("Elevation++: Trains toolbar category not found; elevated stations have no tab.");
        }
        ImmutableArray<ToolbarEntryData> categoryArray = registrator.GetCategoryToArray(ELEVATED_CATEGORY_ID, false, 110);

        TrainTrackTrajectoryData moduleTraj = makeElevatedTrajectory(registrator,
            new Vector2f(0, 0), new Vector2f(2, 0), new Vector2f(3, 0), new Vector2f(5, 0));
        TrainTrackTrajectoryData rootTraj = makeElevatedTrajectory(registrator,
            new Vector2f(0, 0), new Vector2f(1, 0), new Vector2f(1, 0), new Vector2f(2, 0));
        if (moduleTraj == null || rootTraj == null)
        {
            return;
        }

        // 2. Clone every cargo module and root. Collect by group so normal/electrified variants of a
        //    type can be combined into one toolbar slot afterwards.
        var moduleGroups = new Dictionary<ProductType, List<Proto>>();
        var rootGroup = new List<Proto>();
        var isElectrified = new Dictionary<Proto, bool>();

        foreach (TrainStationModuleProto v in new List<TrainStationModuleProto>(db.All<TrainStationModuleProto>()))
        {
            if (v is ElevatedStationModuleProto)
            {
                continue;
            }
            Proto p = registerElevatedModule(registrator, db, v, moduleTraj, categoryArray);
            if (p == null)
            {
                continue;
            }
            if (!moduleGroups.TryGetValue(v.ProductType, out var list))
            {
                moduleGroups[v.ProductType] = list = new List<Proto>();
            }
            list.Add(p);
            isElectrified[p] = v.ElectrificationType != ElectrificationType.None;
        }

        foreach (TrainStationRootProto v in new List<TrainStationRootProto>(db.All<TrainStationRootProto>()))
        {
            if (v is ElevatedStationRootProto)
            {
                continue;
            }
            Proto p = registerElevatedRoot(registrator, db, v, rootTraj, categoryArray);
            if (p == null)
            {
                continue;
            }
            rootGroup.Add(p);
            isElectrified[p] = v.ElectrificationType != ElectrificationType.None;
        }

        // 3. Combine normal + electrified of each type under a single toolbar slot (popup variants).
        foreach (var group in moduleGroups.Values)
        {
            combineUnderOneSlot(group, isElectrified);
        }
        combineUnderOneSlot(rootGroup, isElectrified);

        Log.Info($"Elevation++: registered elevated stations — {isElectrified.Count} proto(s) in " +
                 $"{moduleGroups.Count + 1} toolbar group(s).");
    }

    private static void combineUnderOneSlot(List<Proto> group, Dictionary<Proto, bool> isElectrified)
    {
        if (group.Count < 2)
        {
            return;
        }
        // Non-electrified first (tier I), electrified after (tier II). Link them into a tier chain
        // — each higher tier's "next tier" is the one below — which is what makes the toolbar show
        // them combined under one slot with the variants in the popup (same as the vanilla Stations
        // tab). SetNextTier points from the higher tier down to the lower one.
        group.Sort((a, b) => isElectrified[a].CompareTo(isElectrified[b]));
        for (int i = 1; i < group.Count; i++)
        {
            ((IProtoWithUpgrade)group[i]).SetNextTier((IProtoWithUpgrade)group[i - 1]);
        }
    }

    private Proto registerElevatedModule(ProtoRegistrator registrator, ProtosDb db, TrainStationModuleProto v,
        TrainTrackTrajectoryData trajectory, ImmutableArray<ToolbarEntryData> categoryArray)
    {
        var id = new StaticEntityProto.ID("ElevationPP_Elev_" + v.Id);
        Proto.Str strings = Proto.CreateStrFromLocalized(id, ELEVATED_PREFIX.Format(v.Strings.Name), v.Strings.DescShort);
        EntityLayout layout = registrator.LayoutParser.ParseLayoutOrThrow(
            new EntityLayoutParams(customPlacementRange: new ThicknessIRange(0, PLACEMENT_HEIGHT_MAX),
                tokenPostProcesssor: toUsingPillar),
            moduleFootprint(v));
        var gfx = (TrainStationModuleProto.Gfx)cloneGfxWithCategory(v.Graphics, categoryArray);
        bool electrified = v.ElectrificationType != ElectrificationType.None;

        var proto = new ElevatedStationModuleProto(
            id, strings, layout, v.Costs, trajectory, v.ProductType, v.Capacity, v.TransferPeriod,
            v.TransferQuantity, v.ConnectionCompletionPerStepWhenLoading, v.PowerConsumption,
            ((IProtoWithAnimation)v).AnimationParams, gfx, null, null, null, cannotBeReflected: false, null, electrified);
        return db.Add(proto);
    }

    private Proto registerElevatedRoot(ProtoRegistrator registrator, ProtosDb db, TrainStationRootProto v,
        TrainTrackTrajectoryData trajectory, ImmutableArray<ToolbarEntryData> categoryArray)
    {
        var id = new StaticEntityProto.ID("ElevationPP_Elev_" + v.Id);
        Proto.Str strings = Proto.CreateStrFromLocalized(id, ELEVATED_PREFIX.Format(v.Strings.Name), v.Strings.DescShort);
        EntityLayout layout = registrator.LayoutParser.ParseLayoutOrThrow(
            new EntityLayoutParams(customPlacementRange: new ThicknessIRange(0, PLACEMENT_HEIGHT_MAX),
                tokenPostProcesssor: toUsingPillar),
            "[5][5]", "[5][5]", "[5][5]", "[5][5]", "[5][5]", "[5][5]", "[5][5]");
        var gfx = (EntityWithTrainTrackBaseProto.Gfx)cloneGfxWithCategory(v.Graphics, categoryArray);
        bool electrified = v.ElectrificationType != ElectrificationType.None;

        var proto = new ElevatedStationRootProto(
            id, strings, layout, v.Costs, trajectory, v.PowerConsumption, gfx,
            null, null, null, cannotBeReflected: false, electrified);
        return db.Add(proto);
    }

    /// <summary>
    /// The building footprint for a cargo module: a 5x7 grid of [5] tiles, with the two cargo ports
    /// in the first row using the proto's own port symbol (# conveyor, ~ loose, @ pipe, molten, …).
    /// Modules with no ports (the empty module) use the vanilla empty footprint.
    /// </summary>
    private static string[] moduleFootprint(TrainStationModuleProto v)
    {
        if (v.Ports.IsNotEmpty)
        {
            char c = v.Ports[0].Shape.LayoutChar;
            return new[]
            {
                "   +A" + c + "   +B" + c + "   ",
                "[5][5][5][5][5]", "[5][5][5][5][5]", "[5][5][5][5][5]", "[5][5][5][5][5]",
                "[5][5][5][5][5]", "[5][5][5][5][5]", "[5][5][5][5][5]",
            };
        }
        return new[]
        {
            "[2][2][2][2][2]", "[2][2][2][2][2]", "[2][2][2][2][2]", "[2][2][2][2][2]", "[2][2][2][2][2]",
            "[4][4][4][4][4]", "[4][4][4][4][4]",
        };
    }

    private TrainTrackTrajectoryData makeElevatedTrajectory(ProtoRegistrator registrator,
        Vector2f p0, Vector2f p1, Vector2f p2, Vector2f p3)
    {
        var curve = new CubicBezierCurve2f(ImmutableArray.Create(p0, p1, p2, p3));
        if (!TrainTrackTrajectoryData.TryCreateFromCurve(0, curve, isElevated: true,
                registrator.LayoutParser.SomeOption(), out var traj, out _, out var error,
                default(ThicknessTilesF), default(ThicknessTilesF), TrainTrackGradeFactor.G0,
                TrainTrackGradeFactor.G0, null, null, new RelTile2f(0, -2), default(RelTile2f), null, false))
        {
            Log.Error("Elevation++: failed to create elevated station trajectory: " + error);
            return null;
        }
        return traj;
    }

    /// <summary>
    /// Shallow-clones a proto Gfx, retargets its toolbar category to the new tab, and marks the icon
    /// as custom so the proto's Initialize keeps the source proto's (already-resolved) icon path
    /// instead of regenerating a missing one for the new id. The vanilla proto is untouched (it keeps
    /// its own Gfx instance).
    /// </summary>
    private static object cloneGfxWithCategory(object vanillaGfx, ImmutableArray<ToolbarEntryData> categoryArray)
    {
        object clone = typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(vanillaGfx, null);
        Type t = clone.GetType();
        setField(t, clone, "<Categories>k__BackingField", categoryArray);
        // Keep the vanilla IconPath the clone already copied: mark it custom so Initialize won't
        // overwrite it with a generated path for our (icon-less) new id.
        if (!setField(t, clone, "IconIsCustom", true))
        {
            Log.Warning("Elevation++: Gfx IconIsCustom field not found; elevated station icons may be missing.");
        }
        return clone;
    }

    private static bool setField(Type type, object target, string name, object value)
    {
        for (Type t = type; t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                f.SetValue(target, value);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Converts each non-port footprint tile from a ground-anchored tile to a UsingPillar tile
    /// (dropping the terrain-height constraint that triggers "terrain too low" when raised).
    /// </summary>
    private static LayoutTokenSpec toUsingPillar(RelTile2i coord, LayoutTokenSpec spec)
    {
        if (spec.IsPort)
        {
            return spec;
        }
        return new LayoutTokenSpec(spec.HeightFrom.Value, spec.HeightToExcl.Value,
            LayoutTileConstraint.UsingPillar);
    }
}
