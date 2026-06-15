using System;
using System.Reflection;
using Mafi;
using Mafi.Core;
using Mafi.Core.Economy;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Trains;
using Mafi.Base.Prototypes.Trains;

namespace ElevationPP.Stations;

/// <summary>
/// Elevated variant of the root <see cref="TrainStationRootProto"/> — the base building that
/// establishes a train station and that loading modules attach to. Reuses the vanilla root model
/// and config but with an elevated track trajectory, a widened placement-height range, and a
/// UsingPillar footprint, so it can be raised onto pillars like the elevated loading module.
///
/// The root entity (<c>TrainStationRoot</c>) is sealed, so unlike the loading module this proto
/// cannot supply an <see cref="ITrainTrackMayBeElevatedFriend"/> entity. That is fine: every
/// friend access in the engine is guarded by an <c>is</c> check, so the root simply tracks no
/// pillars of its own and is never collapse-checked by TrainTracksCollapseHelper. It is held up by
/// its footprint pillars via <see cref="ILayoutEntityProtoWithElevation"/>.
/// </summary>
public class ElevatedStationRootProto : TrainStationRootProto, ITrainTrackMayBeElevatedProto, ILayoutEntityProtoWithElevation
{
    // EntityType is inherited as typeof(TrainStationRoot) — the sealed vanilla entity is reused.

    public Option<ITrainTrackMayBeElevatedProto> ElevationFlippedProto => Option<ITrainTrackMayBeElevatedProto>.None;

    public bool CanBeElevated => true;
    public bool CanPillarsPassThrough => true;

    public ElevatedStationRootProto(ID id, Proto.Str strings, EntityLayout layout, EntityCosts costs,
        TrainTrackTrajectoryData trajectoryData, Electricity powerConsumption,
        EntityWithTrainTrackBaseProto.Gfx graphics, RelTile1f? maxSpeedTilesPerTick,
        Duration? constructionDurationPerProduct, Upoints? boostCost, bool cannotBeReflected, bool isElectrified)
        : base(id, strings, layout, costs, trajectoryData, powerConsumption, graphics,
            maxSpeedTilesPerTick, constructionDurationPerProduct, boostCost, cannotBeReflected, isElectrified)
    {
        // Defensive: exempt the track from the missing-support collapse check (see the module proto).
        // The root is not a friend so the check never runs for it, but set it anyway for consistency.
        FieldInfo backing = typeof(EntityWithTrainTrackBaseProto).GetField(
            "<IgnoreMissingSupport>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        backing?.SetValue(this, true);
    }

    public T SelectBetweenElevatedAndGround<T>(TerrainManager terrainManager, TileTransform transform)
        where T : class, ITrainTrackMayBeElevatedProto
    {
        return this as T;
    }
}
