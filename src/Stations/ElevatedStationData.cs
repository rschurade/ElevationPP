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
using Mafi.Core.Prototypes;
using Mafi.Core.Trains;
using Mafi.Localization;
using Mafi.Base.Prototypes.Trains;

namespace ElevationPP.Stations;

/// <summary>
/// Registers elevated variants of the train stations. Rather than hard-coding each type, it
/// iterates every existing station proto at runtime and clones it — so electrified, molten and any
/// DLC variants are picked up automatically. Each clone gets an elevated track trajectory, a
/// UsingPillar footprint, a widened placement-height range, and is filed under a new
/// "Elevated stations" sub-tab next to Stations. The footprint is derived from each proto's own
/// ports so the right belt/pipe/molten symbol is used.
///
/// Cargo modules use the friend entity <see cref="ElevatedStationModule"/>; the root keeps the
/// vanilla sealed entity (see <see cref="ElevatedStationRootProto"/>). Fuel modules are a separate
/// follow-up (two-port layout).
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

        // 1. New "Elevated stations" sub-tab under the Trains category, next to Stations (order 1).
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

        // 2. Elevated track trajectories: cargo modules use a 5-tile curve, the root a 2-tile one.
        TrainTrackTrajectoryData moduleTraj = makeElevatedTrajectory(registrator,
            new Vector2f(0, 0), new Vector2f(2, 0), new Vector2f(3, 0), new Vector2f(5, 0));
        TrainTrackTrajectoryData rootTraj = makeElevatedTrajectory(registrator,
            new Vector2f(0, 0), new Vector2f(1, 0), new Vector2f(1, 0), new Vector2f(2, 0));
        if (moduleTraj == null || rootTraj == null)
        {
            return;
        }

        // 3. Clone every cargo module and root (materialize first so we don't iterate our own clones).
        int modules = 0, roots = 0;
        foreach (TrainStationModuleProto v in new List<TrainStationModuleProto>(db.All<TrainStationModuleProto>()))
        {
            if (v is ElevatedStationModuleProto)
            {
                continue;
            }
            registerElevatedModule(registrator, db, v, moduleTraj, categoryArray);
            modules++;
        }
        foreach (TrainStationRootProto v in new List<TrainStationRootProto>(db.All<TrainStationRootProto>()))
        {
            if (v is ElevatedStationRootProto)
            {
                continue;
            }
            registerElevatedRoot(registrator, db, v, rootTraj, categoryArray);
            roots++;
        }
        Log.Info($"Elevation++: registered elevated stations — {modules} module(s), {roots} root(s).");
    }

    private void registerElevatedModule(ProtoRegistrator registrator, ProtosDb db, TrainStationModuleProto v,
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
        db.Add(proto);
    }

    private void registerElevatedRoot(ProtoRegistrator registrator, ProtosDb db, TrainStationRootProto v,
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
        db.Add(proto);
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
    /// Shallow-clones a proto Gfx and retargets its toolbar category to the given one, so the
    /// elevated variant lands in the "Elevated stations" tab without disturbing the vanilla proto
    /// (which shares the original Gfx instance).
    /// </summary>
    private static object cloneGfxWithCategory(object vanillaGfx, ImmutableArray<ToolbarEntryData> categoryArray)
    {
        object clone = typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(vanillaGfx, null);
        FieldInfo categories = findField(clone.GetType(), "<Categories>k__BackingField");
        if (categories != null)
        {
            categories.SetValue(clone, categoryArray);
        }
        else
        {
            Log.Warning("Elevation++: Gfx Categories backing field not found; elevated station may show in the wrong tab.");
        }
        return clone;
    }

    private static FieldInfo findField(Type t, string name)
    {
        for (; t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                return f;
            }
        }
        return null;
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
