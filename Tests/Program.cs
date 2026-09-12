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
        SourceAndSinkRecipesKeepTheIndustrialGraphLive();
        NeedPricesExposeStockPressureCostsAndPossibleLosses();
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
        LiveStockTransportHasNoOfferOrCargoReservation();
        CargoTagsPersistAndExpireByChosenLifetime();
        PreloadedTaggedCargoJoinsTransportWithoutDoubleStockDebit();
        DispatchCargoTagsAreGuarded();
        TimedProductionRunsFullToEightyThenSlowsToFullStop();
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

    private static void LiveStockTransportHasNoOfferOrCargoReservation()
    {
        var state = State("w038-live-stock", 0); Stocks(state, 20m, 0m, 20m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        Tag(state, wagon, CargoTagLifetime.Permanent);
        var engine = Engine(state);
        engine.ConfigureTransportPolicy("live-policy", "logs-live", "ORIGIN", "DEST", "Logs", 10m, 20m, 100, 100,
            10, 10, 0, Requirement(10m), true, 100, 150);
        var firstQuote = engine.CurrentTransportNeed("logs-live", 1) ?? throw new InvalidOperationException("Expected live stock projection.");
        var movement = engine.StartStockTransport("live-start", "p", "logs-live", 10m, AccountRef.Player("p"), AssetOwnerRef.Player("p"), new[] { wagon.AssetId }, 1);
        Check(state.IndustrialTransportNeeds.Count == 0 && state.IndustrialStocks.All(value => value.ReservedOutbound == 0m && (value.FacilityId != "DEST" || value.ReservedInbound == 0m)) && movement.StockDriven,
            "a live stock movement starts without publishing an offer or reserving source/destination stock");
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(state), "w038-live-stock");
        Check(restored.IndustrialContracts.Single().StockDriven && restored.IndustrialContracts.Single().TransportPolicyId == "logs-live" && restored.IndustrialContracts.Single().AssignedWagons.Single().AssetId == wagon.AssetId,
            "the chosen stock movement and exact operator wagon survive save and reload");
        engine.Activate("live-activate", movement.ContractId, 1);
        engine.RecordLoading("live-load", movement.ContractId, wagon.AssetId, 10m, 1);
        Check(state.IndustrialStocks.Single(value => value.FacilityId == "ORIGIN").OnHand == 10m && state.IndustrialStocks.Single(value => value.FacilityId == "ORIGIN").ReservedInbound == 10m,
            "only observed physical loading removes stock and records cargo in transit");
        state.IndustrialStocks.Single(value => value.FacilityId == "DEST").OnHand = 10m;
        var lowerQuote = engine.CurrentTransportNeed("logs-live", 2) ?? throw new InvalidOperationException("Expected remaining live demand.");
        engine.RecordUnloading("live-unload", movement.ContractId, wagon.AssetId, 10m, 2);
        Check(movement.State == IndustrialContractState.Completed && movement.PaidAmount > 0 && movement.PaidAmount < firstQuote.BaseReward + firstQuote.ScarcityBonus &&
              lowerQuote.EstimatedNetMargin < firstQuote.EstimatedNetMargin && state.IndustrialStocks.Single(value => value.FacilityId == "DEST").OnHand == 20m,
            "delivery pays from current stock pressure and can earn less than the earlier estimate");

        var race = State("w038-live-race", 0); Stocks(race, 10m, 0m, 30m); var firstWagon = AddWagon(race, "wagon.box", AssetOwnerRef.Player("p")); var secondWagon = AddWagon(race, "wagon.box", AssetOwnerRef.Player("p")); Tag(race, firstWagon, CargoTagLifetime.Permanent); Tag(race, secondWagon, CargoTagLifetime.Permanent); var raceEngine = Engine(race);
        raceEngine.ConfigureTransportPolicy("race-policy", "race", "ORIGIN", "DEST", "Logs", 10m, 30m, 100, 0, 10, 10, 0, Requirement(10m), true, 100, 10);
        var firstMovement = raceEngine.StartStockTransport("race-first", "p", "race", 10m, AccountRef.Player("p"), AssetOwnerRef.Player("p"), new[] { firstWagon.AssetId }, 1);
        var secondMovement = raceEngine.StartStockTransport("race-second", "p", "race", 10m, AccountRef.Player("p"), AssetOwnerRef.Player("p"), new[] { secondWagon.AssetId }, 1);
        raceEngine.Activate("race-first-active", firstMovement.ContractId, 1); raceEngine.Activate("race-second-active", secondMovement.ContractId, 1); raceEngine.RecordLoading("race-first-load", firstMovement.ContractId, firstWagon.AssetId, 10m, 1);
        var staleLoadRefused = false; try { raceEngine.RecordLoading("race-second-load", secondMovement.ContractId, secondWagon.AssetId, 10m, 1); } catch (InvalidOperationException) { staleLoadRefused = true; }
        raceEngine.Cancel("race-second-cancel", secondMovement.ContractId);
        Check(staleLoadRefused && secondMovement.State == IndustrialContractState.Cancelled && race.Fleet.Single(value => value.AssetId == secondWagon.AssetId).OperationalState == FleetOperationalState.Available,
            "competing movements recheck stock at physical loading and the losing movement can be cancelled without deadlock");
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

    private static void SourceAndSinkRecipesKeepTheIndustrialGraphLive()
    {
        var state = State("w038-source-sink", 0);
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "MINE", CargoId = "IronOre", OnHand = 0m, Capacity = 40m, Version = 1 });
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "PORT", CargoId = "IronOre", OnHand = 30m, Capacity = 40m, Version = 1 });
        var engine = Engine(state);
        var source = engine.ConfigureRecipe("source-config", "iron-mine", "MINE", "", 0m, "IronOre", 10m, 5, 4);
        var sink = engine.ConfigureRecipe("sink-config", "iron-export", "PORT", "IronOre", 10m, "", 0m, 5, 4);
        var sourceCycles = engine.AdvanceProduction("source-tick", source.RecipeId, 20);
        var sinkCycles = engine.AdvanceProduction("sink-tick", sink.RecipeId, 20);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(state), state.CheckpointId);
        Check(sourceCycles == 4 && sinkCycles == 3 && restored.IndustrialStocks.Single(x => x.FacilityId == "MINE").OnHand == 40m && restored.IndustrialStocks.Single(x => x.FacilityId == "PORT").OnHand == 0m,
            "input-free sources replenish bounded stock while output-free sinks release destination capacity without inventing transportable cargo");
        Check(restored.IndustrialRecipes.Count == 2 && restored.IndustrialRecipes.All(x => x.CompletedCycles > 0),
            "source and sink recipes remain valid and persistent across reload");
    }

    private static void NeedPricesExposeStockPressureCostsAndPossibleLosses()
    {
        var state = State("w038-dynamic-need-price", 0);
        Stocks(state, 100m, 0m, 100m);
        var engine = Engine(state);
        var policy = engine.ConfigureTransportPolicy("price-policy", "priced-flow", "ORIGIN", "DEST", "Logs", 20m, 50m, 1000, 200, 100, 20, 0, Requirement(20m), true, 100, 1500);
        var need = engine.PublishTransportNeed("price-publish", policy.PolicyId, 1) ?? throw new InvalidOperationException("Expected priced need.");
        var later = engine.CurrentTransportNeed(policy.PolicyId, 80) ?? throw new InvalidOperationException("Expected later priced need.");
        Check(need.PriceFactor >= 0.55m && need.PriceFactor <= 1.45m && later.PriceFactor >= 0.55m && later.PriceFactor <= 1.45m && later.PriceFactor != need.PriceFactor,
            "market value is bounded and continues to move with authoritative time even without a cargo movement");
        var costly = engine.ConfigureTransportPolicy("loss-policy", "loss-flow", "ORIGIN", "DEST", "Logs", 10m, 50m, 500, 0, 100, 20, 0, Requirement(10m), true, 100, 2000);
        var loss = engine.PublishTransportNeed("loss-publish", costly.PolicyId, 2) ?? throw new InvalidOperationException("Expected loss-making need.");
        Check(loss.EstimatedNetMargin < 0, "a coherent but poor equipment/cost choice can be visibly loss-making");
    }

    private static void CargoTagsPersistAndExpireByChosenLifetime()
    {
        var state = State("w038-cargo-tags", 0); Stocks(state, 30m, 0m, 30m);
        var oneShot = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var untilEmpty = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var untagged = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        Tag(state, oneShot, CargoTagLifetime.NextLoading);
        var engine = Engine(state);
        engine.ConfigureTransportPolicy("tag-policy", "tag-flow", "ORIGIN", "DEST", "Logs", 10m, 30m, 100, 0, 10, 10, 0, Requirement(10m), true, 100, 0);
        var untaggedRefused = false; try { engine.StartStockTransport("tag-missing", "p", "tag-flow", 10m, AccountRef.Player("p"), AssetOwnerRef.Player("p"), new[] { untagged.AssetId }, 1); } catch (InvalidOperationException) { untaggedRefused = true; }
        Check(untaggedRefused && state.IndustrialContracts.Count == 0 && state.IndustrialCargoTags.All(value => value.AssetId != untagged.AssetId), "a dossier cannot create a missing wagon tag implicitly");
        var first = engine.StartStockTransport("tag-once", "p", "tag-flow", 10m, AccountRef.Player("p"), AssetOwnerRef.Player("p"), new[] { oneShot.AssetId }, 1);
        engine.Activate("tag-once-active", first.ContractId, 1); engine.RecordLoading("tag-once-load", first.ContractId, oneShot.AssetId, 10m, 1);
        Check(state.IndustrialCargoTags.All(value => value.AssetId != oneShot.AssetId), "one-shot wagon cargo tag clears after the first observed loading");
        RollingStockTags.SetCargo(state, "p", untilEmpty.AssetId, state.Fleet.Single(value => value.AssetId == untilEmpty.AssetId).Version, "ORIGIN", "Logs", CargoTagLifetime.UntilEmpty, true, new Compatibility(), (facility, cargo) => facility == "ORIGIN" && cargo == "Logs");
        var second = engine.StartStockTransport("tag-empty", "p", "tag-flow", 10m, AccountRef.Player("p"), AssetOwnerRef.Player("p"), new[] { untilEmpty.AssetId }, 2);
        Check(state.IndustrialCargoTags.Single(value => value.AssetId == untilEmpty.AssetId).Lifetime == CargoTagLifetime.UntilEmpty, "dossier preserves the independently selected wagon tag duration");
        engine.Activate("tag-empty-active", second.ContractId, 2); engine.RecordLoading("tag-empty-load", second.ContractId, untilEmpty.AssetId, 10m, 2);
        Check(state.IndustrialCargoTags.Single(value => value.AssetId == untilEmpty.AssetId).Lifetime == CargoTagLifetime.UntilEmpty, "until-empty cargo tag survives loading and save state");
        engine.RecordUnloading("tag-empty-unload", second.ContractId, untilEmpty.AssetId, 10m, 2);
        Check(state.IndustrialCargoTags.All(value => value.AssetId != untilEmpty.AssetId), "until-empty wagon cargo tag clears only after complete physical unloading");
    }

    private static void PreloadedTaggedCargoJoinsTransportWithoutDoubleStockDebit()
    {
        var state = State("w038-preloaded-tagged-cargo", 0); Stocks(state, 10m, 0m, 30m);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p")); Tag(state, wagon, CargoTagLifetime.UntilEmpty);
        var engine = Engine(state);
        engine.ConfigureTransportPolicy("preloaded-policy", "preloaded-flow", "ORIGIN", "DEST", "Logs", 10m, 30m, 100, 0, 10, 10, 0, Requirement(10m), true, 100, 0);
        var contract = engine.StartStockTransport("preloaded-start", "p", "preloaded-flow", 10m, AccountRef.Player("p"), AssetOwnerRef.Player("p"), new[] { wagon.AssetId }, 1,
            new Dictionary<string, decimal> { [wagon.AssetId] = 10m });
        Check(state.IndustrialStocks.Single(value => value.FacilityId == "ORIGIN").OnHand == 10m && contract.Manifests.Single().OnBoardQuantity == 10m,
            "a dossier adopts cargo already loaded by its valid tag without debiting source stock twice");
        engine.Activate("preloaded-active", contract.ContractId, 1);
        engine.RecordUnloading("preloaded-unload", contract.ContractId, wagon.AssetId, 10m, 1);
        Check(contract.State == IndustrialContractState.Completed && state.IndustrialStocks.Single(value => value.FacilityId == "DEST").OnHand == 10m,
            "preloaded tagged cargo can complete and pay through the normal transport manifest");
    }

    private static void TimedProductionRunsFullToEightyThenSlowsToFullStop()
    {
        var state = State("w038-production-curve", 0);
        state.IndustrialStocks.Add(new IndustrialStock { FacilityId = "MINE", CargoId = "Ore", OnHand = 80m, Capacity = 100m, Version = 1 });
        var engine = Engine(state); var recipe = engine.ConfigureRecipe("curve-recipe", "mine", "MINE", "", 0m, "Ore", 5m, 1, 20);
        var slowed = engine.AdvanceProduction("curve-advance", recipe.RecipeId, 4);
        Check(slowed > 0 && slowed < 4 && state.IndustrialStocks.Single().OnHand < 100m, "timed production runs below full speed after output reaches eighty percent");
        state.IndustrialStocks.Single().OnHand = 100m;
        var stopped = engine.AdvanceProduction("curve-stop", recipe.RecipeId, 24);
        Check(stopped == 0 && state.IndustrialStocks.Single().OnHand == 100m, "timed production stops at one hundred percent output stock");
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
        var sameTick = Engine(restored).AdvanceProduction("tick-30-resume", "mill", 30);
        var resumed = Engine(restored).AdvanceProduction("tick-60-resume", "mill", 60);
        var recipe = restored.IndustrialRecipes.Single();
        Check(blocked == 0 && blockedReplay == 0 && sameTick == 0 && resumed == 2 && recipe.PendingCycles == 0 && recipe.CompletedCycles == 2, "output saturation stops production without banking a catch-up burst, then resumes only as new clock intervals elapse");
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

    private static void DispatchCargoTagsAreGuarded()
    {
        var state = State("dispatch-tags", 0);
        var wagon = AddWagon(state, "wagon.box", AssetOwnerRef.Player("p"));
        var fleet = state.Fleet.Single();
        void Set(string cargo, bool empty = true, long? version = null, string actor = "p", CargoTagLifetime lifetime = CargoTagLifetime.Permanent)
            => RollingStockTags.SetCargo(state, actor, wagon.AssetId, version ?? fleet.Version, cargo.Length == 0 ? "" : "ORIGIN", cargo, lifetime, empty, new Compatibility(), (facility, providedCargo) => facility == "ORIGIN" && providedCargo == "Logs");
        void Refuses(Action action, string label)
        {
            var before = VehicleAcquisitionPersistence.Serialize(state); var refused = false;
            try { action(); } catch (InvalidOperationException) { refused = true; } catch (UnauthorizedAccessException) { refused = true; } catch (ArgumentException) { refused = true; }
            Check(refused && before == VehicleAcquisitionPersistence.Serialize(state), label);
        }
        var initialVersion = fleet.Version;
        Set("Logs");
        Check(state.IndustrialCargoTags.Single().CargoId == "Logs" && fleet.Version > initialVersion, "dispatch assigns compatible cargo independently of a dossier");
        Refuses(() => Set("Logs", version: initialVersion), "stale browser tag update is rejected without mutation");
        Refuses(() => Set("Coal"), "incompatible cargo tag is refused atomically");
        Refuses(() => RollingStockTags.SetCargo(state, "p", wagon.AssetId, fleet.Version, "DEST", "Logs", CargoTagLifetime.UntilEmpty, true, new Compatibility(), (facility, cargo) => facility == "ORIGIN" && cargo == "Logs"), "cargo not supplied by the selected industry is refused atomically");
        Refuses(() => Set("", false), "loaded wagon tag cannot be removed");
        Refuses(() => Set("Logs", lifetime: (CargoTagLifetime)99), "undefined cargo tag duration is rejected");
        new CompanyEconomyEngine(state.Economy).EnsurePlayer("other", 0);
        Refuses(() => Set("Logs", actor: "other"), "another player cannot tag owned stock");
        fleet.OperationalState = FleetOperationalState.Stored;
        Refuses(() => Set("Logs"), "stored stock cannot receive cargo assignment");
        fleet.OperationalState = FleetOperationalState.Maintenance;
        Refuses(() => Set("Logs"), "maintenance stock cannot receive cargo assignment");
        Set("");
        Check(state.IndustrialCargoTags.Count == 0, "an empty stored or maintenance wagon can clear its cargo tag");
        fleet.OperationalState = FleetOperationalState.Available;
        Set("Logs");
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(state), "dispatch-tags");
        Check(restored.IndustrialCargoTags.Single().Lifetime == CargoTagLifetime.Permanent && restored.IndustrialCargoTags.Single().SourceFacilityId == "ORIGIN", "standalone dispatch tag and source industry persist across reload");
        state.IndustrialContracts.Add(new IndustrialContract { ContractId = "active", State = IndustrialContractState.Active, AssignedWagons = new List<ContractWagonAssignment> { new ContractWagonAssignment { AssetId = wagon.AssetId } } });
        var locked = false; var versionBefore = fleet.Version;
        try { Set("Logs"); } catch (InvalidOperationException) { locked = true; }
        Check(locked && fleet.Version == versionBefore, "an active dossier locks standalone tag edits even if fleet state is available");
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
    private static void Tag(VehicleAcquisitionSnapshot state, FleetAsset wagon, CargoTagLifetime lifetime) => state.IndustrialCargoTags.Add(new IndustrialCargoTag { AssetId = wagon.AssetId, SourceFacilityId = "ORIGIN", CargoId = "Logs", Lifetime = lifetime });
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
