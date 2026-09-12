using System;
using System.Linq;

namespace BDVM.Domain;

public static class RollingStockTags
{
    public static void RequireControl(VehicleAcquisitionSnapshot state, string actorId, string assetId)
    {
        var player = state.Economy.Players.Single(value => value.PlayerId == actorId);
        var owner = state.Ownership.Single(value => value.AssetId == assetId).Owner;
        if (owner.Key == AssetOwnerRef.Player(actorId).Key) return;
        var company = state.Economy.Companies.SingleOrDefault(value => AssetOwnerRef.Company(value.CompanyId).Key == owner.Key && value.CompanyId == player.CompanyId);
        if (company != null && !company.Liquidating && (company.LeaderId == actorId ||
            (company.DelegatedPermissions.TryGetValue(actorId, out var rights) && rights.Contains(CompanyPermission.ManageFleet)))) return;
        throw new UnauthorizedAccessException("Fleet-management permission is required.");
    }

    public static void RequireEditable(VehicleAcquisitionSnapshot state, string assetId, long expectedVersion)
    {
        var fleet = state.Fleet.Single(value => value.AssetId == assetId);
        if (fleet.Version != expectedVersion) throw new InvalidOperationException("The rolling stock changed. Refresh and try again.");
        if (fleet.OperationalState != FleetOperationalState.Available && fleet.OperationalState != FleetOperationalState.Stored && fleet.OperationalState != FleetOperationalState.Maintenance)
            throw new InvalidOperationException("Rolling stock is reserved, in service or awaiting reconciliation.");
        if (state.IndustrialContracts.Any(value => value.State != IndustrialContractState.Completed && value.State != IndustrialContractState.Cancelled && value.State != IndustrialContractState.Expired && value.AssignedWagons.Any(wagon => wagon.AssetId == assetId)))
            throw new InvalidOperationException("Rolling stock belongs to an active dossier.");
    }

    public static void SetCargo(VehicleAcquisitionSnapshot state, string actorId, string assetId, long expectedVersion,
        string sourceFacilityId, string cargoId, CargoTagLifetime lifetime, bool empty, IWagonCompatibilityPort compatibility,
        Func<string, string, bool> facilityProvidesCargo)
    {
        RequireControl(state, actorId, assetId);
        RequireEditable(state, assetId, expectedVersion);
        if (!empty) throw new InvalidOperationException("Unload the wagon before changing its cargo tag.");
        var fleet = state.Fleet.Single(value => value.AssetId == assetId);
        if (fleet.Kind != FleetVehicleKind.FreightWagon) throw new InvalidOperationException("Cargo tags require a freight wagon.");
        if (!Enum.IsDefined(typeof(CargoTagLifetime), lifetime)) throw new ArgumentException("Unknown cargo tag lifetime.");
        if (cargoId.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(sourceFacilityId) || !facilityProvidesCargo(sourceFacilityId, cargoId))
                throw new InvalidOperationException("The selected industry does not provide that cargo.");
            if (fleet.OperationalState != FleetOperationalState.Available) throw new InvalidOperationException("Make the wagon available before assigning a cargo tag.");
            var definition = state.Assets.Assets.Single(value => value.AssetId == assetId).DefinitionId;
            if (!compatibility.Inspect(assetId, definition, cargoId).Compatible) throw new InvalidOperationException("Cargo is incompatible with this wagon.");
        }
        var tag = state.IndustrialCargoTags.SingleOrDefault(value => value.AssetId == assetId);
        if (cargoId.Length == 0) { if (tag != null) state.IndustrialCargoTags.Remove(tag); }
        else if (tag == null) state.IndustrialCargoTags.Add(new IndustrialCargoTag { AssetId = assetId, SourceFacilityId = sourceFacilityId, CargoId = cargoId, Lifetime = lifetime });
        else { tag.SourceFacilityId = sourceFacilityId; tag.CargoId = cargoId; tag.Lifetime = lifetime; tag.DossierId = null; tag.Version++; }
        fleet.Version++;
    }
}
