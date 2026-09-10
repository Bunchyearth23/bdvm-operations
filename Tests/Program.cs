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
        StockAndRecipeConfigurationIsIdempotentAndGuarded();
        StationShortagePublishesPersistentTransportNeeds();
        TransportNeedExpiryAndCompetingAcceptanceFailClosed();
        TransportNeedRequiresRealStockCapacityAndShortage();
        OperatorWagonsAndManifestsSurviveReload();
        AggregateDeliveryCannotBypassPhysicalManifests();
        LeasedWagonExpiryAndMissingWagonFailClosed();
        ProductionBackpressureRetainsBoundedBacklog();
        StrictGeneratorActivationRollsBack();
        StrictGeneratorActivationPreservesExistingJobs();
        StrictGeneratorActivationFailsClosedOnMigrationMutation();
        StrictGeneratorActivationRejectsGenerationRace();
        StrictGeneratorActivationContainsAdapterExceptions();
        CompetingContractsCannotReserveTheSameWagon();
        WagonTypeAndCapacityFailuresAreAtomic();
        CompatibleWagonQueryIsAuthoritativeAndLeaseAware();
        PartialCancellationAndRetryConserveState();
        PendingTransferCanBeRetriedWithoutDuplicateAccounting();
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

    private static void StockAndRecipeConfigurationIsIdempotentAndGuarded()
    {
        var state = State("w038-configuration", 0);
        var engine = Engine(state);
        var input = engine.ConfigureStock("stock-input", "MILL", "Logs", 10m, 20m);
        var replay = engine.ConfigureStock("stock-input", "MILL", "Logs", 10m, 20m);
        engine.ConfigureStock("stock-output", "MILL", "Lumber", 0m, 20m);
        var recipe = engine.ConfigureRecipe("recipe", "mill", "MILL", "Logs", 2m, "Lumber", 1m, 10, 3);
        var recipeReplay = engine.ConfigureRecipe("recipe", "mill", "MILL", "Logs", 2m, "Lumber", 1m, 10, 3);
        var contract = engine.CreateTransportOffer("reserved-stock", "MILL", "DEST", "Logs", 5m, AccountRef.Player("p"), 0, 0, 0, 100, null, 0);
        engine.ConfigureStock("stock-destination", "DEST", "Logs", 0m, 20m);
        engine.Accept("reserve-stock", contract.ContractId, contract.Version, 0, 10, null);
        var reservedMutationRefused = false;
        try { engine.ConfigureStock("stock-mutate-reserved", "MILL", "Logs", 1m, 20m); }
        catch (InvalidOperationException) { reservedMutationRefused = true; }
        Check(ReferenceEquals(input, replay) && ReferenceEquals(recipe, recipeReplay) && reservedMutationRefused && state.IndustrialCommands.Count == 5,
            "stock and recipe configuration is idempotent and cannot rewrite cargo reserved by a contract");
    }

    private static void StationShortagePublishesPersistentTransportNeeds()
    {
        var state = State("w038-station-needs", 100);
        Stocks(state, 100m, 0m, 100m);
        var engine = Engine(state);
        var policy = engine.ConfigureTransportPolicy("policy", "logs-to-mill", "ORIGIN", "DEST", "Logs", 20m, 50m, 100, 50, 100, 20, 5, Requirement(20m));
        var first = engine.PublishTransportNeed("publish-1", policy.PolicyId, 5) ?? throw new InvalidOperationException("Expected a station transport need.");
        var replay = engine.PublishTransportNeed("publish-1", policy.PolicyId, 500);
        var existing = engine.PublishTransportNeed("publish-2", policy.PolicyId, 6);
        var accepted = engine.AcceptTransportNeed("accept-need", first.NeedId, first.Version, AccountRef.Player("p"), AccountRef.Player("p"), 7);
        var acceptedReplay = engine.AcceptTransportNeed("accept-need", first.NeedId, 1, AccountRef.Player("p"), AccountRef.Player("p"), 700);
        var second = engine.PublishTransportNeed("publish-3", policy.PolicyId, 8) ?? throw new InvalidOperationException("Expected a second bounded station need.");
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(state), state.CheckpointId);
        Check(ReferenceEquals(first, replay) && ReferenceEquals(first, existing) && ReferenceEquals(accepted, acceptedReplay) && first.State == IndustrialNeedState.Accepted && accepted.State == IndustrialContractState.Reserved && accepted.Quantity == 20m && accepted.ScarcityBonus == 50,
            "station shortage publishes one idempotent need and acceptance atomically reserves its cargo and destination capacity");
        Check(second.Quantity == 20m && second.ScarcityBonus == 30 && restored.IndustrialTransportPolicies.Single().NextNeedSequence == 3 && restored.IndustrialTransportNeeds.Count == 2,
            "accepted demand reduces the remaining shortage and policies, needs and sequences survive reload deterministically");
    }

    private static void TransportNeedExpiryAndCompetingAcceptanceFailClosed()
    {
        var state = State("w038-need-race", 100);
        Stocks(state, 30m, 0m, 30m);
        var engine = Engine(state);
        var policy = engine.ConfigureTransportPolicy("policy", "race", "ORIGIN", "DEST", "Logs", 10m, 20m, 100, 20, 5, 5, 0, Requirement(10m));
        var expired = engine.PublishTransportNeed("publish-old", policy.PolicyId, 1) ?? throw new InvalidOperationException("Expected expiring need.");
        var lateRefused = false;
        try { engine.AcceptTransportNeed("accept-late", expired.NeedId, expired.Version, AccountRef.Player("p"), null, 6); }
        catch (InvalidOperationException) { lateRefused = true; }
        var current = engine.PublishTransportNeed("publish-current", policy.PolicyId, 6) ?? throw new InvalidOperationException("Expected replacement need.");
        var accepted = engine.AcceptTransportNeed("accept-first", current.NeedId, current.Version, AccountRef.Player("p"), null, 7);
        var competingRefused = false;
        try { engine.AcceptTransportNeed("accept-second", current.NeedId, 1, AccountRef.Player("p"), null, 7); }
        catch (InvalidOperationException) { competingRefused = true; }
        Check(lateRefused && expired.State == IndustrialNeedState.Expired && competingRefused && current.State == IndustrialNeedState.Accepted && accepted.State == IndustrialContractState.Reserved,
            "expired and concurrently accepted station needs fail closed with exactly one winning contract");
        Check(state.IndustrialContracts.Count == 1 && state.IndustrialStocks.Single(value => value.FacilityId == "ORIGIN").ReservedOutbound == 10m && state.IndustrialStocks.Single(value => value.FacilityId == "DEST").ReservedInbound == 10m,
            "competing need acceptance reserves source cargo and destination capacity exactly once");
    }

    private static void TransportNeedRequiresRealStockCapacityAndShortage()
    {
        var state = State("w038-need-bounds", 0);
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "EMPTY", CargoId = "Logs", OnHand = 0m, Capacity = 10m, Version = 1 });
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "DEST-A", CargoId = "Logs", OnHand = 0m, Capacity = 10m, Version = 1 });
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "FULL", CargoId = "Logs", OnHand = 10m, Capacity = 10m, Version = 1 });
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "DEST-B", CargoId = "Logs", OnHand = 10m, Capacity = 10m, Version = 1 });
        var engine = Engine(state);
        var noSource = engine.ConfigureTransportPolicy("policy-empty", "empty", "EMPTY", "DEST-A", "Logs", 5m, 10m, 0, 0, 10, 5, 0, Requirement(5m));
        var noShortage = engine.ConfigureTransportPolicy("policy-full", "full", "FULL", "DEST-B", "Logs", 5m, 10m, 0, 0, 10, 5, 0, Requirement(5m));
        var assets = state.Assets.Assets.Count;
        Check(engine.PublishTransportNeed("publish-empty", noSource.PolicyId, 1) == null && engine.PublishTransportNeed("publish-full", noShortage.PolicyId, 1) == null && state.IndustrialTransportNeeds.Count == 0,
            "stations publish no transport need without source stock, destination capacity and a real target shortage");
        Check(state.Assets.Assets.Count == assets && state.Fleet.Count == 0, "transport-need publication never creates rolling stock");
    }

    private static void OperatorWagonsAndManifestsSurviveReload()
    {
        var state = State("w038-manifest", 0);
        Stocks(state, 12m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var engine = Engine(state);
        var contract = engine.CreateTransportOffer("freight", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 20, 0, 100, Requirement(10m), 0);
        engine.Accept("accept", contract.ContractId, contract.Version, 1, 20, null);
        engine.Accept("accept", contract.ContractId, 999, 999, 20, null);
        engine.AssignWagons("assign", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId });
        engine.AssignWagons("assign", "p", contract.ContractId, 999, AssetOwnerRef.Player("p"), new[] { wagon.AssetId }, 999);
        engine.Activate("activate", contract.ContractId, 2);
        engine.Activate("activate", contract.ContractId, 999);
        engine.RecordLoading("load-a", contract.ContractId, wagon.AssetId, 6m);
        engine.RecordLoading("load-a", contract.ContractId, wagon.AssetId, 6m);
        engine.RecordUnloading("unload-a", contract.ContractId, wagon.AssetId, 4m);
        var loadConflict = false; var unloadConflict = false;
        try { engine.RecordLoading("load-a", contract.ContractId, wagon.AssetId, 7m); } catch (InvalidOperationException) { loadConflict = true; }
        try { engine.RecordUnloading("unload-a", contract.ContractId, wagon.AssetId, 5m); } catch (InvalidOperationException) { unloadConflict = true; }
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(state), state.CheckpointId);
        var recovery = Engine(restored);
        recovery.RecordLoading("load-b", contract.ContractId, wagon.AssetId, 10m);
        var completed = recovery.RecordUnloading("unload-b", contract.ContractId, wagon.AssetId, 10m);
        recovery.RecordUnloading("unload-b", contract.ContractId, wagon.AssetId, 10m);
        Check(completed.State == IndustrialContractState.Completed && completed.PaidAmount == 120 && restored.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.IndustrialRevenue) == 2, "partial manifests pay cumulative reward exactly once across reload and retry");
        Check(loadConflict && unloadConflict, "a completed cargo observation rejects reuse of its operation ID with a different cumulative quantity");
        Check(restored.Ownership.Single(x => x.AssetId == wagon.AssetId).Owner.Key == "Player:p" && restored.Fleet.Single(x => x.AssetId == wagon.AssetId).OperationalState == FleetOperationalState.Available, "completion retains and releases the operator wagon");
        Check(restored.IndustrialStocks.Single(x => x.FacilityId == "ORIGIN").OnHand == 2m && restored.IndustrialStocks.Single(x => x.FacilityId == "DEST").OnHand == 10m, "loading and unloading conserve persistent cargo stock");
    }

    private static void AggregateDeliveryCannotBypassPhysicalManifests()
    {
        var state = State("w038-no-aggregate-bypass", 0);
        Stocks(state, 10m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var engine = Engine(state);
        var contract = engine.CreateTransportOffer("freight", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 0, 0, 100, Requirement(10m), 0);
        engine.Accept("accept", contract.ContractId, contract.Version, 0, 10, null);
        engine.AssignWagons("assign", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId });
        engine.Activate("activate", contract.ContractId, 1);
        var refused = false;
        try { engine.RecognizeDelivery("aggregate", contract.ContractId, 10m); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused && contract.DeliveredQuantity == 0m && state.IndustrialStocks.Single(x => x.FacilityId == "DEST").OnHand == 0m && state.Economy.Wallets.Single().Balance == 0,
            "aggregate delivery cannot bypass exact physical wagon loading and unloading observations");
    }

    private static void ProductionBackpressureRetainsBoundedBacklog()
    {
        var state = State("w038-production", 0);
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "MILL", CargoId = "Logs", OnHand = 10m, Capacity = 10m });
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "MILL", CargoId = "Lumber", OnHand = 2m, Capacity = 2m });
        state.IndustrialRecipes.Add(new IndustrialRecipe { RecipeId = "mill", FacilityId = "MILL", InputCargoId = "Logs", InputQuantity = 2m, OutputCargoId = "Lumber", OutputQuantity = 1m, CadenceTicks = 10, MaximumBacklogCycles = 3 });
        var engine = Engine(state);
        var blocked = engine.AdvanceProduction("tick-30", "mill", 30);
        var blockedReplay = engine.AdvanceProduction("tick-30", "mill", 300);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(state), state.CheckpointId);
        restored.IndustrialStocks.Single(x => x.CargoId == "Lumber").OnHand = 0m;
        var resumed = Engine(restored).AdvanceProduction("tick-30-resume", "mill", 30);
        var recipe = restored.IndustrialRecipes.Single();
        Check(blocked == 0 && blockedReplay == 0 && resumed == 2 && recipe.PendingCycles == 1 && recipe.CompletedCycles == 2, "output saturation applies backpressure and resumes persisted bounded backlog after delivery without re-advancing a retried command clock");
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
        var midTripExpiryRefused = false; try { engine.RecordLoading("expired-mid-trip", contract.ContractId, wagon.AssetId, 1m, 20); } catch (InvalidOperationException) { midTripExpiryRefused = true; }
        state.Leases.Single().EndTick = 30;
        state.Fleet.Single(x => x.AssetId == wagon.AssetId).OperationalState = FleetOperationalState.ReconcileRequired;
        var missingRefused = false; try { engine.RecordLoading("missing-load", contract.ContractId, wagon.AssetId, 1m, 20); } catch (InvalidOperationException) { missingRefused = true; }
        Check(expiredRefused && midTripExpiryRefused && missingRefused, "expired lease before or during a mission and a missing assigned wagon fail closed without spawning a replacement");
        Check(typeof(TransportContract).GetProperties().All(x => x.Name.IndexOf("supplied", StringComparison.OrdinalIgnoreCase) < 0), "transport contract schema contains no supplied rolling-stock field");
    }

    private static void StrictGeneratorActivationRollsBack()
    {
        var first = new Generator("vanilla", true); var second = new Generator("selfshunt", false);
        var enabled = IndustrialRuntimeGate.TryEnableStrict("strict", new ITransportGeneratorAdapter[] { first, second });
        Check(!enabled && !first.Suppressed && first.Calls == 2 && second.Calls == 1, "strict activation fails closed and rolls back controls already changed");
    }

    private static void StrictGeneratorActivationPreservesExistingJobs()
    {
        var vanilla = new Generator("vanilla", true, "available-job", "accepted-job");
        var report = IndustrialRuntimeGate.TryEnableStrictWithReport("strict", new[] { vanilla });
        Check(report.Applied && report.ResultCode == "strict-generator-control-active" && report.PreservedOpenJobIds.SequenceEqual(new[] { "accepted-job", "available-job" }), "strict activation inventories and preserves jobs opened before migration");
        Check(vanilla.Suppressed && vanilla.Jobs.SequenceEqual(new[] { "available-job", "accepted-job" }), "strict activation suppresses only new generation and leaves existing jobs cancellable");
    }

    private static void StrictGeneratorActivationFailsClosedOnMigrationMutation()
    {
        var vanilla = new Generator("vanilla", true, "open-job") { RemoveJobsWhenSuppressed = true };
        var report = IndustrialRuntimeGate.TryEnableStrictWithReport("strict", new[] { vanilla });
        Check(!report.Applied && report.ResultCode == "strict-existing-jobs-mutated:vanilla" && !vanilla.Suppressed, "strict activation rolls back if a generator removes an existing job during migration");
    }

    private static void StrictGeneratorActivationContainsAdapterExceptions()
    {
        var vanilla = new Generator("vanilla", true, "open-job");
        var broken = new Generator("broken", true) { ThrowWhenSetting = true };
        var report = IndustrialRuntimeGate.TryEnableStrictWithReport("strict", new ITransportGeneratorAdapter[] { vanilla, broken });
        Check(!report.Applied && report.ResultCode == "strict-generator-suspension-failed:broken" && !vanilla.Suppressed, "strict activation contains adapter exceptions and rolls back prior controls");
    }

    private static void StrictGeneratorActivationRejectsGenerationRace()
    {
        var vanilla = new Generator("vanilla", true, "open-job") { AddJobWhenSuppressed = "racing-job" };
        var report = IndustrialRuntimeGate.TryEnableStrictWithReport("strict", new[] { vanilla });
        Check(!report.Applied && report.ResultCode == "strict-new-jobs-generated:vanilla" && !vanilla.Suppressed,
            "strict activation detects a generation race and rolls back instead of accepting a free consist");
    }

    private static void CompetingContractsCannotReserveTheSameWagon()
    {
        var state = State("w038-wagon-contention", 0); Stocks(state, 20m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p")); var engine = Engine(state);
        var first = engine.CreateTransportOffer("first", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 10, 0, 0, 100, Requirement(10m), 0);
        var second = engine.CreateTransportOffer("second", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 10, 0, 0, 100, Requirement(10m), 0);
        engine.Accept("accept-first", first.ContractId, first.Version, 0, 10, null);
        engine.AssignWagons("assign-first", "p", first.ContractId, first.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId });
        engine.Accept("accept-second", second.ContractId, second.Version, 0, 10, null);
        var refused = false; try { engine.AssignWagons("assign-second", "p", second.ContractId, second.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId }); } catch (InvalidOperationException) { refused = true; }
        Check(refused && second.AssignedWagons.Count == 0 && state.Fleet.Single().OperationalState == FleetOperationalState.Reserved,
            "two contracts cannot claim one wagon and a refused assignment does not mutate either contract");
    }

    private static void WagonTypeAndCapacityFailuresAreAtomic()
    {
        var state = State("w038-compatibility", 0); Stocks(state, 10m, 0m, 20m);
        var wrong = AddWagon(state, "wagon.tank", AssetOwnerRef.Player("p"));
        var small = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var engine = Engine(state); var contract = engine.CreateTransportOffer("freight", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 10, 0, 0, 100, Requirement(20m), 0);
        engine.Accept("accept", contract.ContractId, contract.Version, 0, 10, null);
        var wrongRefused = false; try { engine.AssignWagons("wrong", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { wrong.AssetId }); } catch (InvalidOperationException) { wrongRefused = true; }
        var capacityRefused = false; try { engine.AssignWagons("small", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { small.AssetId }); } catch (InvalidOperationException) { capacityRefused = true; }
        Check(wrongRefused && capacityRefused && contract.AssignedWagons.Count == 0 && state.Fleet.All(x => x.OperationalState == FleetOperationalState.Available),
            "wrong cargo type and insufficient capacity fail atomically without reserving rolling stock");
    }

    private static void CompatibleWagonQueryIsAuthoritativeAndLeaseAware()
    {
        var state = State("w038-compatible-query", 0); Stocks(state, 20m, 0m, 20m);
        var owned = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var leased = AddWagon(state, "wagon.box", AssetOwnerRef.Merchant("market"));
        var merchant = AddWagon(state, "wagon.box", AssetOwnerRef.Merchant("market"));
        AddWagon(state, "wagon.tank", AssetOwnerRef.Player("p"));
        state.Leases.Add(new LeaseContract { LeaseId = "query-lease", AssetIds = new List<string> { leased.AssetId }, Lessee = AssetOwnerRef.Player("p"), State = LeaseState.Active, StartTick = 0, EndTick = 20, DurationTicks = 20, RentIntervalTicks = 5, ConditionAtStart = 1m, Version = 1 });
        var engine = Engine(state);
        var contract = engine.CreateTransportOffer("query", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 0, 0, 0, 100, Requirement(10m), 0);
        engine.Accept("query-accept", contract.ContractId, contract.Version, 0, 10, null);
        var options = engine.FindCompatibleWagons("p", contract.ContractId, AssetOwnerRef.Player("p"), 5);
        Check(options.Select(value => value.AssetId).OrderBy(value => value).SequenceEqual(new[] { owned.AssetId, leased.AssetId }.OrderBy(value => value)) && options.Single(value => value.AssetId == leased.AssetId).LeaseId == "query-lease",
            "host-computed compatible wagon options include owned and active leased stock but exclude merchant and incompatible stock");
        Check(!options.Any(value => value.AssetId == merchant.AssetId), "unleased merchant rolling stock is never proposed to the operator");
    }

    private static void PartialCancellationAndRetryConserveState()
    {
        var state = State("w038-partial-cancel", 0); Stocks(state, 10m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p")); var engine = Engine(state);
        var contract = engine.CreateTransportOffer("freight", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 0, 0, 100, Requirement(10m), 0);
        engine.Accept("accept", contract.ContractId, contract.Version, 0, 10, null); engine.AssignWagons("assign", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId }); engine.Activate("activate", contract.ContractId, 1);
        engine.RecordLoading("load", contract.ContractId, wagon.AssetId, 6m); engine.RecordUnloading("unload", contract.ContractId, wagon.AssetId, 4m);
        var cancelled = engine.Cancel("cancel", contract.ContractId); var replay = engine.Cancel("cancel", contract.ContractId);
        Check(ReferenceEquals(cancelled, replay) && cancelled.State == IndustrialContractState.Cancelled && cancelled.PaidAmount == 40 && state.Fleet.Single().OperationalState == FleetOperationalState.Available,
            "partial delivery cancellation is idempotent, retains earned payment and releases the operator wagon");
        Check(state.IndustrialStocks.Single(x => x.FacilityId == "ORIGIN").ReservedOutbound == 0m && state.IndustrialStocks.Single(x => x.FacilityId == "ORIGIN").OnHand == 6m && state.IndustrialStocks.Single(x => x.FacilityId == "DEST").ReservedInbound == 0m && state.IndustrialStocks.Single(x => x.FacilityId == "DEST").OnHand == 4m,
            "partial cancellation restores in-transit cargo and releases only the remaining reservations without losing material");
    }

    private static void PendingTransferCanBeRetriedWithoutDuplicateAccounting()
    {
        var state = State("w038-transfer-retry", 0); Stocks(state, 10m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p")); var transfers = new RetryTransfers(); var engine = new IndustrialEconomyEngine(state, new Host(), new LegacyExecution(), transfers, new Compatibility());
        var contract = engine.CreateTransportOffer("freight", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 0, 0, 100, Requirement(10m), 0);
        engine.Accept("accept", contract.ContractId, contract.Version, 0, 10, null); engine.AssignWagons("assign", "p", contract.ContractId, contract.Version, AssetOwnerRef.Player("p"), new[] { wagon.AssetId }); engine.Activate("activate", contract.ContractId, 1);
        var pending = engine.RecordLoading("load", contract.ContractId, wagon.AssetId, 10m);
        var pendingConflict = false; try { engine.RecordLoading("load", contract.ContractId, wagon.AssetId, 9m); } catch (InvalidOperationException) { pendingConflict = true; }
        var completedLoad = engine.RecordLoading("load", contract.ContractId, wagon.AssetId, 10m); var replay = engine.RecordLoading("load", contract.ContractId, wagon.AssetId, 10m);
        Check(pending == completedLoad && completedLoad == replay && pendingConflict && completedLoad.State == IndustrialContractState.Active && completedLoad.Manifests.Single().LoadedQuantity == 10m && state.IndustrialStocks.Single(x => x.FacilityId == "ORIGIN").OnHand == 0m,
            "an unknown load observation remains pending and the same operation can be retried exactly once");
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
        if (!state.Assets.Definitions.Any(value => value.DefinitionId == definition)) state.Assets.Definitions.Add(new AssetDefinition { DefinitionId = definition, Origin = "test" });
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
    private sealed class RetryTransfers : ICargoTransferObservationPort { private int loads; public WorldOwnershipOutcome InspectLoading(string operationId, string contractId, string assetId, decimal cumulativeQuantity) => ++loads == 1 ? WorldOwnershipOutcome.Unknown : WorldOwnershipOutcome.Applied; public WorldOwnershipOutcome InspectUnloading(string operationId, string contractId, string assetId, decimal cumulativeQuantity) => WorldOwnershipOutcome.Applied; }
    private sealed class Compatibility : IWagonCompatibilityPort { public WagonCompatibility Inspect(string assetId, string definitionId, string cargoId) => new WagonCompatibility { Compatible = definitionId == "wagon.box" && cargoId == "Logs", Capacity = 10m, Detail = "test" }; }
    private sealed class Generator : ITransportGeneratorAdapter
    {
        private readonly bool succeeds; public Generator(string id, bool succeeds, params string[] jobs) { GeneratorId = id; this.succeeds = succeeds; Jobs.AddRange(jobs); }
        public string GeneratorId { get; } public bool CanSuppressNewConsists => true; public bool Suppressed { get; private set; } public int Calls { get; private set; }
        public List<string> Jobs { get; } = new List<string>(); public bool RemoveJobsWhenSuppressed { get; set; } public bool ThrowWhenSetting { get; set; } public string? AddJobWhenSuppressed { get; set; }
        public bool TrySetNewConsistsSuppressed(string operationId, bool suppressed) { Calls++; if (ThrowWhenSetting) throw new InvalidOperationException("test adapter failure"); if (!succeeds) return false; Suppressed = suppressed; if (suppressed && RemoveJobsWhenSuppressed) Jobs.Clear(); if (suppressed && !string.IsNullOrWhiteSpace(AddJobWhenSuppressed)) Jobs.Add(AddJobWhenSuppressed!); return true; }
        public IReadOnlyList<string> ReadExistingOpenJobIds() => Jobs.ToArray();
    }
}
