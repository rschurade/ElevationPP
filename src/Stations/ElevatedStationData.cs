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
using Mafi.Base.Prototypes.Trains;

namespace ElevationPP.Stations;

/// <summary>
/// Registers an "Elevated Unit Station" — a clone of the vanilla unit cargo station that uses an
/// elevated track trajectory and a widened placement-height range, so it can be raised onto pillars
/// like a balancer/lift. Everything else (model, costs, capacity, products) is taken from the
/// vanilla proto so no new art or balancing is needed.
/// </summary>
internal class ElevatedStationData : IModData
{
    public void RegisterData(ProtoRegistrator registrator)
    {
        ProtosDb db = registrator.PrototypesDb;

        if (!db.TryGetProto<TrainStationModuleProto>(Ids.TrainTracks.TrainStationUnit, out var vanilla))
        {
            Log.Error("Elevation++: vanilla unit station proto not found; elevated station not registered.");
            return;
        }

        // Recreate the station's built-in track trajectory as ELEVATED (vanilla passes isElevated:false).
        // Same curve and offset the vanilla cargo stations use.
        var curve = new CubicBezierCurve2f(ImmutableArray.Create(
            new Vector2f(0, 0), new Vector2f(2, 0), new Vector2f(3, 0), new Vector2f(5, 0)));
        var trackOffset = new RelTile2f(0, -2);
        if (!TrainTrackTrajectoryData.TryCreateFromCurve(0, curve, isElevated: true,
                registrator.LayoutParser.SomeOption(), out var trajectory, out _, out var error,
                default(ThicknessTilesF), default(ThicknessTilesF), TrainTrackGradeFactor.G0,
                TrainTrackGradeFactor.G0, null, null, trackOffset, default(RelTile2f), null, false))
        {
            Log.Error("Elevation++: failed to create elevated station trajectory: " + error);
            return;
        }

        // Building footprint — same tokens as the vanilla unit station, but with a widened
        // placement-height range so it can be lifted off the ground.
        EntityLayout layout = registrator.LayoutParser.ParseLayoutOrThrow(
            new EntityLayoutParams(customPlacementRange: new ThicknessIRange(0, 16)),
            "   +A#   +B#   ",
            "[5][5][5][5][5]", "[5][5][5][5][5]", "[5][5][5][5][5]", "[5][5][5][5][5]",
            "[5][5][5][5][5]", "[5][5][5][5][5]", "[5][5][5][5][5]");

        var id = new StaticEntityProto.ID("ElevationPP_TrainStationUnitElevated");
        Proto.Str strings = Proto.CreateStr(id, "Elevated Unit Station",
            "A unit cargo train station that can be built on elevated track, supported by pillars.");

        var proto = new ElevatedStationModuleProto(
            id, strings, layout, vanilla.Costs, trajectory, vanilla.ProductType, vanilla.Capacity,
            vanilla.TransferPeriod, vanilla.TransferQuantity, vanilla.ConnectionCompletionPerStepWhenLoading,
            vanilla.PowerConsumption, ((IProtoWithAnimation)vanilla).AnimationParams, vanilla.Graphics,
            null, null, null, cannotBeReflected: false, null, isElectrified: false);

        db.Add(proto);
        Log.Info("Elevation++: registered elevated unit station.");
    }
}
