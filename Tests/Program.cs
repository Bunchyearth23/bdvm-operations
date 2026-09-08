using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;

internal static class Program
{
    private static int checks;

    private static void Main()
    {
        ReservationIsAtomicAndExpiresOnce();
        OperatorWagonsAndManifestsSurviveReload();
        LeasedWagonExpiryAndMissingWagonFailClosed();
        ProductionBackpressureRetainsBoundedBacklog();
        StrictGeneratorActivationRollsBack();
        Console.WriteLine("W-038 offline checks passed: " + checks);
    }

    private static void ReservationIsAtomicAndExpiresOnce()
    {
        var state = State("w038-reservation", 100);
        Stocks(state, 10m, 0m, 20m);
        var engine = Engine(state);
        var first = engine.CreateTransportOffer("first", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 0, 5, 100, Requirement(10m), 30);
        var second = engine.CreateTransportOffer("second", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 0, 5, 100, Requirement(10m), 30);
        engine.Accept("accept-first", first.ContractId, first.Version, 10, 5, AccountRef.Player("p"));
        var refused = false;
        try { engine.Accept("accept-second", second.ContractId, second.Version, 10, 5, AccountRef.Player("p")); } catch (InvalidOperationException) { refused = true; }
        var expired = engine.ExpirePreparation("expire-first", first.ContractId, 15);
        var replay = engine.ExpirePreparation("expire-first", first.ContractId, 15);
        Check(refused && ReferenceEquals(expired, replay), "cargo and destination reservations are atomic and expiry is idempotent");
        Check(state.IndustrialStocks.All(x => x.ReservedInbound == 0m && x.ReservedOutbound == 0m) && state.Economy.Wallets.Single().Balance == 70 && expired.PenaltyApplied == 30, "expiry releases reservations and applies one bounded penalty");
    }

    private static void OperatorWagonsAndManifestsSurviveReload()
    {
        var state = State("w038-manifest", 0);
        Stocks(state, 12m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var engine = Engine(state);
        var contract = engine.CreateTransportOffer("freight", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 20, 0, 100, Requirement(10m), 0);
        engine.Accept("accept", contract.ContractId, contract.Version, 1, 20, null);
        engine.AssignWagons("assign", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId });
        engine.Activate("activate", contract.ContractId, 2);
        engine.RecordLoading("load-a", contract.ContractId, wagon.AssetId, 6m);
        engine.RecordLoading("load-a", contract.ContractId, wagon.AssetId, 6m);
        engine.RecordUnloading("unload-a", contract.ContractId, wagon.AssetId, 4m);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(state), state.CheckpointId);
        var recovery = Engine(restored);
        recovery.RecordLoading("load-b", contract.ContractId, wagon.AssetId, 10m);
        var completed = recovery.RecordUnloading("unload-b", contract.ContractId, wagon.AssetId, 10m);
        recovery.RecordUnloading("unload-b", contract.ContractId, wagon.AssetId, 10m);
        Check(completed.State == IndustrialContractState.Completed && completed.PaidAmount == 120 && restored.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.IndustrialRevenue) == 2, "partial manifests pay cumulative reward exactly once across reload and retry");
        Check(restored.Ownership.Single(x => x.AssetId == wagon.AssetId).Owner.Key == "Player:p" && restored.Fleet.Single(x => x.AssetId == wagon.AssetId).OperationalState == FleetOperationalState.Available, "completion retains and releases the operator wagon");
        Check(restored.IndustrialStocks.Single(x => x.FacilityId == "ORIGIN").OnHand == 2m && restored.IndustrialStocks.Single(x => x.FacilityId == "DEST").OnHand == 10m, "loading and unloading conserve persistent cargo stock");
    }

    private static void ProductionBackpressureRetainsBoundedBacklog()
    {
        var state = State("w038-production", 0);
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "MILL", CargoId = "Logs", OnHand = 10m, Capacity = 10m });
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "MILL", CargoId = "Lumber", OnHand = 2m, Capacity = 2m });
        state.IndustrialRecipes.Add(new IndustrialRecipe { RecipeId = "mill", FacilityId = "MILL", InputCargoId = "Logs", InputQuantity = 2m, OutputCargoId = "Lumber", OutputQuantity = 1m, CadenceTicks = 10, MaximumBacklogCycles = 3 });
        var engine = Engine(state);
        var blocked = engine.AdvanceProduction("tick-30", "mill", 30);
        state.IndustrialStocks.Single(x => x.CargoId == "Lumber").OnHand = 0m;
        var resumed = engine.AdvanceProduction("tick-30-resume", "mill", 30);
        var recipe = state.IndustrialRecipes.Single();
        Check(blocked == 0 && resumed == 2 && recipe.PendingCycles == 1 && recipe.CompletedCycles == 2, "output saturation applies backpressure and resumes persisted bounded backlog after delivery");
    }

    private static void LeasedWagonExpiryAndMissingWagonFailClosed()
    {
        var state = State("w038-lease", 0); Stocks(state, 10m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Merchant("market"));
        state.Leases.Add(new LeaseContract { LeaseId = "lease", AssetIds = new List<string> { wagon.AssetId }, Lessee = AssetOwnerRef.Player("p"), State = LeaseState.Active, StartTick = 1, EndTick = 10, DurationTicks = 9, RentIntervalTicks = 3, ConditionAtStart = 1m, Version = 1 });
        var engine = Engine(state);
        var contract = engine.CreateTransportOffer("leased", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 10, 0, 0, 100, Requirement(10m), 0);
        engine.Accept("leased-accept", contract.ContractId, contract.Version, 1, 20, null);
        engine.AssignWagons("leased-assign", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId }, 5);
        var expiredRefused = false; try { engine.Activate("leased-activate", contract.ContractId, 10); } catch (InvalidOperationException) { expiredRefused = true; }
        state.Leases.Single().EndTick = 20;
        engine.Activate("leased-activate-ok", contract.ContractId, 9);
        state.Fleet.Single(x => x.AssetId == wagon.AssetId).OperationalState = FleetOperationalState.ReconcileRequired;
        var missingRefused = false; try { engine.RecordLoading("missing-load", contract.ContractId, wagon.AssetId, 1m); } catch (InvalidOperationException) { missingRefused = true; }
        Check(expiredRefused && missingRefused, "expired lease and missing assigned wagon fail closed without spawning a replacement");
        Check(typeof(TransportContract).GetProperties().All(x => x.Name.IndexOf("supplied", StringComparison.OrdinalIgnoreCase) < 0), "transport contract schema contains no supplied rolling-stock field");
    }

    private static void StrictGeneratorActivationRollsBack()
    {
        var first = new Generator("vanilla", true); var second = new Generator("selfshunt", false);
        var enabled = IndustrialRuntimeGate.TryEnableStrict("strict", new ITransportGeneratorAdapter[] { first, second });
        Check(!enabled && !first.Suppressed && first.Calls == 2 && second.Calls == 1, "strict activation fails closed and rolls back controls already changed");
    }

    private static VehicleAcquisitionSnapshot State(string checkpoint, long balance)
    {
        var economy = new CompanyEconomySnapshot { CheckpointId = checkpoint };
        new CompanyEconomyEngine(economy).EnsurePlayer("p", balance);
        return new VehicleAcquisitionSnapshot { CheckpointId = checkpoint, Economy = economy };
    }

    private static void Stocks(VehicleAcquisitionSnapshot state, decimal origin, decimal destination, decimal destinationCapacity)
    {
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "ORIGIN", CargoId = "Logs", OnHand = origin, Capacity = Math.Max(origin, 20m) });
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "DEST", CargoId = "Logs", OnHand = destination, Capacity = destinationCapacity });
    }

    private static FleetAsset AddWagon(VehicleAcquisitionSnapshot state, string definition, AssetOwnerRef owner)
    {
        state.Assets.Definitions.Add(new AssetDefinition { DefinitionId = definition, Origin = "test" });
        var asset = FleetAsset.Create(definition, Guid.NewGuid().ToString("D")); asset.GameLink.State = PersistentLinkState.Resolved;
        state.Assets.Assets.Add(asset); state.Ownership.Add(new AssetOwnership { AssetId = asset.AssetId, Owner = owner });
        FleetManagementEngine.EnsureAsset(state, asset.AssetId, "FreightWagon", definition, "Operator Wagon");
        return asset;
    }

    private static WagonRequirement Requirement(decimal capacity) => new WagonRequirement { CargoId = "Logs", MinimumWagonCount = 1, MinimumTotalCapacity = capacity, AllowedDefinitionIds = new List<string> { "wagon.box" } };
    private static IndustrialEconomyEngine Engine(VehicleAcquisitionSnapshot state) => new IndustrialEconomyEngine(state, new Host(), new LegacyExecution(), new Transfers(), new Compatibility());
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("FAIL: " + message); }

    private sealed class Host : INetworkRoleDetector { public NetworkRoleReport Detect() => new NetworkRoleReport { Role = NetworkRole.MultiplayerHost, HasAuthority = true, Detail = "test host" }; }
    private sealed class LegacyExecution : IIndustrialExecutionPort { public bool Available => true; public WorldOwnershipOutcome InspectDelivery(string operationId, string contractId, decimal cumulativeQuantity) => WorldOwnershipOutcome.Applied; }
    private sealed class Transfers : ICargoTransferObservationPort { public WorldOwnershipOutcome InspectLoading(string operationId, string contractId, string assetId, decimal cumulativeQuantity) => WorldOwnershipOutcome.Applied; public WorldOwnershipOutcome InspectUnloading(string operationId, string contractId, string assetId, decimal cumulativeQuantity) => WorldOwnershipOutcome.Applied; }
    private sealed class Compatibility : IWagonCompatibilityPort { public WagonCompatibility Inspect(string assetId, string definitionId, string cargoId) => new WagonCompatibility { Compatible = definitionId == "wagon.box" && cargoId == "Logs", Capacity = 10m, Detail = "test" }; }
    private sealed class Generator : ITransportGeneratorAdapter
    {
        private readonly bool succeeds; public Generator(string id, bool succeeds) { GeneratorId = id; this.succeeds = succeeds; }
        public string GeneratorId { get; } public bool CanSuppressNewConsists => true; public bool Suppressed { get; private set; } public int Calls { get; private set; }
        public bool TrySetNewConsistsSuppressed(string operationId, bool suppressed) { Calls++; if (!succeeds) return false; Suppressed = suppressed; return true; }
        public IReadOnlyList<string> ReadExistingOpenJobIds() => Array.Empty<string>();
    }
}
