using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum IndustrialContractState { Offered, Reserved, Active, Completed, Cancelled, DeliveryPending, Expired }
public enum IndustrialNeedState { Available, Accepted, Expired }

[DataContract]
public sealed class IndustrialStock
{
    [DataMember(Name = "facilityId", Order = 1)] public string FacilityId { get; set; } = "";
    [DataMember(Name = "cargoId", Order = 2)] public string CargoId { get; set; } = "";
    [DataMember(Name = "onHand", Order = 3)] public decimal OnHand { get; set; }
    [DataMember(Name = "capacity", Order = 4)] public decimal Capacity { get; set; }
    [DataMember(Name = "reservedOutbound", Order = 5)] public decimal ReservedOutbound { get; set; }
    [DataMember(Name = "reservedInbound", Order = 6)] public decimal ReservedInbound { get; set; }
    [DataMember(Name = "version", Order = 7)] public long Version { get; set; }
    public string Key => FacilityId + ":" + CargoId;
}

[DataContract]
public sealed class IndustrialRecipe
{
    [DataMember(Name = "recipeId", Order = 1)] public string RecipeId { get; set; } = "";
    [DataMember(Name = "facilityId", Order = 2)] public string FacilityId { get; set; } = "";
    [DataMember(Name = "inputCargoId", Order = 3)] public string InputCargoId { get; set; } = "";
    [DataMember(Name = "inputQuantity", Order = 4)] public decimal InputQuantity { get; set; }
    [DataMember(Name = "outputCargoId", Order = 5)] public string OutputCargoId { get; set; } = "";
    [DataMember(Name = "outputQuantity", Order = 6)] public decimal OutputQuantity { get; set; }
    [DataMember(Name = "version", Order = 7)] public long Version { get; set; } = 1;
    [DataMember(Name = "cadenceTicks", Order = 8)] public long CadenceTicks { get; set; } = 1;
    [DataMember(Name = "lastProductionTick", Order = 9)] public long LastProductionTick { get; set; }
    [DataMember(Name = "pendingCycles", Order = 10)] public int PendingCycles { get; set; }
    [DataMember(Name = "maximumBacklogCycles", Order = 11)] public int MaximumBacklogCycles { get; set; } = 128;
    [DataMember(Name = "completedCycles", Order = 12)] public long CompletedCycles { get; set; }
}

[DataContract]
public sealed class IndustrialTransportPolicy
{
    [DataMember(Name = "policyId", Order = 1)] public string PolicyId { get; set; } = "";
    [DataMember(Name = "originFacilityId", Order = 2)] public string OriginFacilityId { get; set; } = "";
    [DataMember(Name = "destinationFacilityId", Order = 3)] public string DestinationFacilityId { get; set; } = "";
    [DataMember(Name = "cargoId", Order = 4)] public string CargoId { get; set; } = "";
    [DataMember(Name = "batchQuantity", Order = 5)] public decimal BatchQuantity { get; set; }
    [DataMember(Name = "destinationTargetQuantity", Order = 6)] public decimal DestinationTargetQuantity { get; set; }
    [DataMember(Name = "baseReward", Order = 7)] public long BaseReward { get; set; }
    [DataMember(Name = "maximumScarcityBonus", Order = 8)] public long MaximumScarcityBonus { get; set; }
    [DataMember(Name = "offerLifetimeTicks", Order = 9)] public long OfferLifetimeTicks { get; set; }
    [DataMember(Name = "preparationDurationTicks", Order = 10)] public long PreparationDurationTicks { get; set; }
    [DataMember(Name = "preparationPenalty", Order = 11)] public long PreparationPenalty { get; set; }
    [DataMember(Name = "wagonRequirement", Order = 12)] public WagonRequirement WagonRequirement { get; set; } = new WagonRequirement();
    [DataMember(Name = "nextNeedSequence", Order = 13)] public long NextNeedSequence { get; set; } = 1;
    [DataMember(Name = "enabled", Order = 14)] public bool Enabled { get; set; } = true;
    [DataMember(Name = "version", Order = 15)] public long Version { get; set; } = 1;
    [DataMember(Name = "deliveryDurationTicks", Order = 16)] public long DeliveryDurationTicks { get; set; }
}

[DataContract]
public sealed class IndustrialTransportNeed
{
    [DataMember(Name = "needId", Order = 1)] public string NeedId { get; set; } = "";
    [DataMember(Name = "policyId", Order = 2)] public string PolicyId { get; set; } = "";
    [DataMember(Name = "originFacilityId", Order = 3)] public string OriginFacilityId { get; set; } = "";
    [DataMember(Name = "destinationFacilityId", Order = 4)] public string DestinationFacilityId { get; set; } = "";
    [DataMember(Name = "cargoId", Order = 5)] public string CargoId { get; set; } = "";
    [DataMember(Name = "quantity", Order = 6)] public decimal Quantity { get; set; }
    [DataMember(Name = "baseReward", Order = 7)] public long BaseReward { get; set; }
    [DataMember(Name = "scarcityBonus", Order = 8)] public long ScarcityBonus { get; set; }
    [DataMember(Name = "publishedTick", Order = 9)] public long PublishedTick { get; set; }
    [DataMember(Name = "expiresTick", Order = 10)] public long ExpiresTick { get; set; }
    [DataMember(Name = "preparationDurationTicks", Order = 11)] public long PreparationDurationTicks { get; set; }
    [DataMember(Name = "preparationPenalty", Order = 12)] public long PreparationPenalty { get; set; }
    [DataMember(Name = "wagonRequirement", Order = 13)] public WagonRequirement WagonRequirement { get; set; } = new WagonRequirement();
    [DataMember(Name = "state", Order = 14)] public IndustrialNeedState State { get; set; }
    [DataMember(Name = "acceptedContractId", Order = 15)] public string? AcceptedContractId { get; set; }
    [DataMember(Name = "publishedCommandId", Order = 16)] public string PublishedCommandId { get; set; } = "";
    [DataMember(Name = "version", Order = 17)] public long Version { get; set; } = 1;
    [DataMember(Name = "deliveryDurationTicks", Order = 18)] public long DeliveryDurationTicks { get; set; }
}

[DataContract]
public sealed class WagonRequirement
{
    [DataMember(Name = "cargoId", Order = 1)] public string CargoId { get; set; } = "";
    [DataMember(Name = "minimumWagonCount", Order = 2)] public int MinimumWagonCount { get; set; } = 1;
    [DataMember(Name = "minimumTotalCapacity", Order = 3)] public decimal MinimumTotalCapacity { get; set; }
    [DataMember(Name = "allowedDefinitionIds", Order = 4)] public List<string> AllowedDefinitionIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class ContractWagonAssignment
{
    [DataMember(Name = "assetId", Order = 1)] public string AssetId { get; set; } = "";
    [DataMember(Name = "definitionId", Order = 2)] public string DefinitionId { get; set; } = "";
    [DataMember(Name = "capacity", Order = 3)] public decimal Capacity { get; set; }
    [DataMember(Name = "leaseId", Order = 4)] public string? LeaseId { get; set; }
    [DataMember(Name = "operator", Order = 5)] public AssetOwnerRef Operator { get; set; } = new AssetOwnerRef();
}

[DataContract]
public sealed class CargoManifest
{
    [DataMember(Name = "assetId", Order = 1)] public string AssetId { get; set; } = "";
    [DataMember(Name = "cargoId", Order = 2)] public string CargoId { get; set; } = "";
    [DataMember(Name = "loadedQuantity", Order = 3)] public decimal LoadedQuantity { get; set; }
    [DataMember(Name = "unloadedQuantity", Order = 4)] public decimal UnloadedQuantity { get; set; }
    [DataMember(Name = "loadOperationIds", Order = 5)] public List<string> LoadOperationIds { get; set; } = new List<string>();
    [DataMember(Name = "unloadOperationIds", Order = 6)] public List<string> UnloadOperationIds { get; set; } = new List<string>();
    public decimal OnBoardQuantity => LoadedQuantity - UnloadedQuantity;
}

// A transport contract never supplies rolling stock. AssignedWagons only records
// equipment explicitly selected from assets controlled by the operator.
[DataContract]
public class TransportContract
{
    public const int CurrentSchemaVersion = 2;
    [DataMember(Name = "contractId", Order = 1)] public string ContractId { get; set; } = "";
    [DataMember(Name = "originFacilityId", Order = 2)] public string OriginFacilityId { get; set; } = "";
    [DataMember(Name = "destinationFacilityId", Order = 3)] public string DestinationFacilityId { get; set; } = "";
    [DataMember(Name = "cargoId", Order = 4)] public string CargoId { get; set; } = "";
    [DataMember(Name = "quantity", Order = 5)] public decimal Quantity { get; set; }
    [DataMember(Name = "deliveredQuantity", Order = 6)] public decimal DeliveredQuantity { get; set; }
    [DataMember(Name = "beneficiary", Order = 7)] public AccountRef Beneficiary { get; set; } = new AccountRef();
    [DataMember(Name = "baseReward", Order = 8)] public long BaseReward { get; set; }
    [DataMember(Name = "scarcityBonus", Order = 9)] public long ScarcityBonus { get; set; }
    [DataMember(Name = "paidAmount", Order = 10)] public long PaidAmount { get; set; }
    [DataMember(Name = "state", Order = 11)] public IndustrialContractState State { get; set; }
    [DataMember(Name = "version", Order = 12)] public long Version { get; set; }
    [DataMember(Name = "deliveryOperationIds", Order = 13)] public List<string> DeliveryOperationIds { get; set; } = new List<string>();
    [DataMember(Name = "schemaVersion", Order = 14)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [DataMember(Name = "createdTick", Order = 15)] public long CreatedTick { get; set; }
    [DataMember(Name = "deliveryDeadlineTick", Order = 16)] public long DeliveryDeadlineTick { get; set; }
    [DataMember(Name = "preparationExpiresTick", Order = 17)] public long PreparationExpiresTick { get; set; }
    [DataMember(Name = "preparationPenalty", Order = 18)] public long PreparationPenalty { get; set; }
    [DataMember(Name = "penaltyPayer", Order = 19)] public AccountRef? PenaltyPayer { get; set; }
    [DataMember(Name = "operator", Order = 20)] public AssetOwnerRef? Operator { get; set; }
    [DataMember(Name = "wagonRequirement", Order = 21)] public WagonRequirement? WagonRequirement { get; set; }
    [DataMember(Name = "assignedWagons", Order = 22)] public List<ContractWagonAssignment> AssignedWagons { get; set; } = new List<ContractWagonAssignment>();
    [DataMember(Name = "manifests", Order = 23)] public List<CargoManifest> Manifests { get; set; } = new List<CargoManifest>();
    [DataMember(Name = "reservationsReleased", Order = 24)] public bool ReservationsReleased { get; set; }
    [DataMember(Name = "penaltyApplied", Order = 25)] public long PenaltyApplied { get; set; }
}

[DataContract]
public sealed class IndustrialContract : TransportContract { }

public sealed class WagonCompatibility
{
    public bool Compatible { get; set; }
    public decimal Capacity { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class CompatibleWagonOption
{
    public string AssetId { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public decimal Capacity { get; set; }
    public string? LeaseId { get; set; }
}

public interface IWagonCompatibilityPort { WagonCompatibility Inspect(string assetId, string definitionId, string cargoId); }
public interface ICargoTransferObservationPort
{
    WorldOwnershipOutcome InspectLoading(string operationId, string contractId, string assetId, decimal cumulativeQuantity);
    WorldOwnershipOutcome InspectUnloading(string operationId, string contractId, string assetId, decimal cumulativeQuantity);
}
public interface IIndustrialExecutionPort { bool Available { get; } WorldOwnershipOutcome InspectDelivery(string operationId, string contractId, decimal cumulativeQuantity); }
public interface ICompetingGeneratorControl { bool CanSuspendNewGeneration { get; } bool TrySuspendNewGeneration(string operationId); }
public interface ITransportGeneratorAdapter
{
    string GeneratorId { get; }
    bool CanSuppressNewConsists { get; }
    bool TrySetNewConsistsSuppressed(string operationId, bool suppressed);
    IReadOnlyList<string> ReadExistingOpenJobIds();
}

public sealed class StrictGeneratorActivationReport
{
    public bool Applied { get; set; }
    public string ResultCode { get; set; } = "strict-generator-not-applied";
    public IReadOnlyList<string> PreservedOpenJobIds { get; set; } = Array.Empty<string>();
}

public sealed class DisabledIndustrialExecutionPort : IIndustrialExecutionPort
{
    public bool Available => false;
    public WorldOwnershipOutcome InspectDelivery(string operationId, string contractId, decimal cumulativeQuantity) => WorldOwnershipOutcome.NotApplied;
}
public sealed class DisabledCargoTransferObservationPort : ICargoTransferObservationPort
{
    public WorldOwnershipOutcome InspectLoading(string operationId, string contractId, string assetId, decimal cumulativeQuantity) => WorldOwnershipOutcome.NotApplied;
    public WorldOwnershipOutcome InspectUnloading(string operationId, string contractId, string assetId, decimal cumulativeQuantity) => WorldOwnershipOutcome.NotApplied;
}

public static class IndustrialRuntimeGate
{
    public static bool TryEnable(string operationId, IIndustrialExecutionPort execution, ICompetingGeneratorControl generator)
        => execution != null && execution.Available && generator != null && generator.CanSuspendNewGeneration && generator.TrySuspendNewGeneration(operationId);

    public static bool TryEnableStrict(string operationId, IEnumerable<ITransportGeneratorAdapter> generators)
        => TryEnableStrictWithReport(operationId, generators).Applied;

    public static StrictGeneratorActivationReport TryEnableStrictWithReport(string operationId, IEnumerable<ITransportGeneratorAdapter> generators)
    {
        var controls = (generators ?? Array.Empty<ITransportGeneratorAdapter>()).ToArray();
        if (string.IsNullOrWhiteSpace(operationId) || controls.Length == 0 || controls.Any(x => x == null || string.IsNullOrWhiteSpace(x.GeneratorId) || !x.CanSuppressNewConsists) || controls.GroupBy(x => x.GeneratorId, StringComparer.Ordinal).Any(x => x.Count() != 1))
            return Report(false, "strict-generator-control-unavailable", Array.Empty<string>());
        IReadOnlyList<string>[] before;
        try { before = controls.Select(ReadStableOpenJobs).ToArray(); }
        catch { return Report(false, "strict-existing-jobs-unreadable", Array.Empty<string>()); }
        var changed = new List<ITransportGeneratorAdapter>();
        for (var index = 0; index < controls.Length; index++)
        {
            var control = controls[index];
            var applied = false;
            try { applied = control.TrySetNewConsistsSuppressed(operationId + ":" + control.GeneratorId, true); }
            catch { applied = false; }
            if (applied) { changed.Add(control); continue; }
            RollBack(operationId, changed);
            return Report(false, "strict-generator-suspension-failed:" + control.GeneratorId, Array.Empty<string>());
        }
        try
        {
            var after = controls.Select(ReadStableOpenJobs).ToArray();
            for (var index = 0; index < controls.Length; index++)
                if (before[index].Except(after[index], StringComparer.Ordinal).Any())
                {
                    RollBack(operationId, changed);
                    return Report(false, "strict-existing-jobs-mutated:" + controls[index].GeneratorId, Array.Empty<string>());
                }
                else if (after[index].Except(before[index], StringComparer.Ordinal).Any())
                {
                    RollBack(operationId, changed);
                    return Report(false, "strict-new-jobs-generated:" + controls[index].GeneratorId, Array.Empty<string>());
                }
            return Report(true, "strict-generator-control-active", before.SelectMany(x => x).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
        catch
        {
            RollBack(operationId, changed);
            return Report(false, "strict-existing-jobs-unreadable-after-suspension", Array.Empty<string>());
        }
    }

    private static IReadOnlyList<string> ReadStableOpenJobs(ITransportGeneratorAdapter control)
    {
        var ids = control.ReadExistingOpenJobIds() ?? throw new InvalidOperationException("Generator returned no migration inventory.");
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw new InvalidOperationException("Generator returned an invalid migration inventory.");
        return ids.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static void RollBack(string operationId, IEnumerable<ITransportGeneratorAdapter> changed)
    {
        foreach (var rollback in changed.Reverse())
            try { rollback.TrySetNewConsistsSuppressed(operationId + ":rollback:" + rollback.GeneratorId, false); }
            catch { /* Best-effort rollback; the caller still receives a fail-closed result. */ }
    }

    private static StrictGeneratorActivationReport Report(bool applied, string code, IReadOnlyList<string> jobs) =>
        new StrictGeneratorActivationReport { Applied = applied, ResultCode = code, PreservedOpenJobIds = jobs };
}

public sealed class IndustrialEconomyEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly IIndustrialExecutionPort execution;
    private readonly ICargoTransferObservationPort transfers;
    private readonly IWagonCompatibilityPort? compatibility;

    public IndustrialEconomyEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IIndustrialExecutionPort execution)
        : this(state, authority, execution, new DisabledCargoTransferObservationPort(), null) { }

    public IndustrialEconomyEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IIndustrialExecutionPort execution,
        ICargoTransferObservationPort transfers, IWagonCompatibilityPort? compatibility)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state)); this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.execution = execution ?? throw new ArgumentNullException(nameof(execution)); this.transfers = transfers ?? throw new ArgumentNullException(nameof(transfers)); this.compatibility = compatibility;
        VehicleAcquisitionPersistence.Validate(state);
    }

    public IndustrialStock ConfigureStock(string commandId, string facilityId, string cargoId, decimal onHand, decimal capacity)
    {
        lock (gate)
        {
            RequireHost();
            var fingerprint = string.Join("|", "stock", facilityId, cargoId, onHand, capacity);
            var replay = Command(commandId, fingerprint);
            if (replay != null) return Stock(facilityId, cargoId);
            if (string.IsNullOrWhiteSpace(facilityId) || string.IsNullOrWhiteSpace(cargoId) || onHand < 0m || capacity < onHand)
                throw new ArgumentException("Invalid industrial stock configuration.");
            var stock = state.IndustrialStocks.SingleOrDefault(value => value.FacilityId == facilityId && value.CargoId == cargoId);
            if (stock == null)
            {
                stock = new IndustrialStock { FacilityId = facilityId, CargoId = cargoId, OnHand = onHand, Capacity = capacity, Version = 1 };
                state.IndustrialStocks.Add(stock);
            }
            else
            {
                if (stock.ReservedInbound != 0m || stock.ReservedOutbound != 0m)
                    throw new InvalidOperationException("Reserved industrial stock cannot be reconfigured.");
                stock.OnHand = onHand; stock.Capacity = capacity; stock.Version++;
            }
            Record(commandId, fingerprint, facilityId + "|" + cargoId, "stock-configured");
            return stock;
        }
    }

    public IndustrialRecipe ConfigureRecipe(string commandId, string recipeId, string facilityId, string inputCargoId, decimal inputQuantity,
        string outputCargoId, decimal outputQuantity, long cadenceTicks, int maximumBacklogCycles)
    {
        lock (gate)
        {
            RequireHost();
            var fingerprint = string.Join("|", "recipe", recipeId, facilityId, inputCargoId, inputQuantity, outputCargoId, outputQuantity, cadenceTicks, maximumBacklogCycles);
            var replay = Command(commandId, fingerprint);
            if (replay != null) return state.IndustrialRecipes.Single(value => value.RecipeId == recipeId);
            if (string.IsNullOrWhiteSpace(recipeId) || string.IsNullOrWhiteSpace(facilityId) || string.IsNullOrWhiteSpace(inputCargoId) ||
                string.IsNullOrWhiteSpace(outputCargoId) || string.Equals(inputCargoId, outputCargoId, StringComparison.Ordinal) || inputQuantity <= 0m ||
                outputQuantity <= 0m || cadenceTicks <= 0 || maximumBacklogCycles <= 0 ||
                !state.IndustrialStocks.Any(value => value.FacilityId == facilityId && value.CargoId == inputCargoId) ||
                !state.IndustrialStocks.Any(value => value.FacilityId == facilityId && value.CargoId == outputCargoId))
                throw new ArgumentException("Invalid industrial recipe configuration.");
            var recipe = state.IndustrialRecipes.SingleOrDefault(value => value.RecipeId == recipeId);
            if (recipe == null)
            {
                recipe = new IndustrialRecipe { RecipeId = recipeId, FacilityId = facilityId, InputCargoId = inputCargoId, InputQuantity = inputQuantity,
                    OutputCargoId = outputCargoId, OutputQuantity = outputQuantity, CadenceTicks = cadenceTicks, MaximumBacklogCycles = maximumBacklogCycles, Version = 1 };
                state.IndustrialRecipes.Add(recipe);
            }
            else
            {
                recipe.FacilityId = facilityId; recipe.InputCargoId = inputCargoId; recipe.InputQuantity = inputQuantity;
                recipe.OutputCargoId = outputCargoId; recipe.OutputQuantity = outputQuantity; recipe.CadenceTicks = cadenceTicks;
                recipe.MaximumBacklogCycles = maximumBacklogCycles; recipe.PendingCycles = Math.Min(recipe.PendingCycles, maximumBacklogCycles); recipe.Version++;
            }
            Record(commandId, fingerprint, recipeId, "recipe-configured");
            return recipe;
        }
    }

    public IndustrialTransportPolicy ConfigureTransportPolicy(string commandId, string policyId, string originFacilityId, string destinationFacilityId,
        string cargoId, decimal batchQuantity, decimal destinationTargetQuantity, long baseReward, long maximumScarcityBonus,
        long offerLifetimeTicks, long preparationDurationTicks, long preparationPenalty, WagonRequirement wagonRequirement, bool enabled = true,
        long deliveryDurationTicks = 0)
    {
        lock (gate)
        {
            RequireHost();
            if (wagonRequirement == null) throw new ArgumentNullException(nameof(wagonRequirement));
            var fingerprint = string.Join("|", "transport-policy", policyId, originFacilityId, destinationFacilityId, cargoId, batchQuantity,
                destinationTargetQuantity, baseReward, maximumScarcityBonus, offerLifetimeTicks, preparationDurationTicks, preparationPenalty,
                wagonRequirement.MinimumWagonCount, wagonRequirement.MinimumTotalCapacity,
                string.Join(",", wagonRequirement.AllowedDefinitionIds?.OrderBy(value => value, StringComparer.Ordinal) ?? Enumerable.Empty<string>()), enabled, deliveryDurationTicks);
            var replay = Command(commandId, fingerprint);
            if (replay != null) return state.IndustrialTransportPolicies.Single(value => value.PolicyId == policyId);
            ValidateTransportPolicy(policyId, originFacilityId, destinationFacilityId, cargoId, batchQuantity, destinationTargetQuantity,
                baseReward, maximumScarcityBonus, offerLifetimeTicks, preparationDurationTicks, preparationPenalty, wagonRequirement,
                deliveryDurationTicks <= 0 ? offerLifetimeTicks : deliveryDurationTicks);
            var policy = state.IndustrialTransportPolicies.SingleOrDefault(value => value.PolicyId == policyId);
            if (policy == null)
            {
                policy = new IndustrialTransportPolicy { PolicyId = policyId, NextNeedSequence = 1, Version = 1 };
                state.IndustrialTransportPolicies.Add(policy);
            }
            else
            {
                if (state.IndustrialTransportNeeds.Any(value => value.PolicyId == policyId && value.State == IndustrialNeedState.Available))
                    throw new InvalidOperationException("A transport policy with an available published need cannot be changed.");
                policy.Version++;
            }
            policy.OriginFacilityId = originFacilityId; policy.DestinationFacilityId = destinationFacilityId; policy.CargoId = cargoId;
            policy.BatchQuantity = batchQuantity; policy.DestinationTargetQuantity = destinationTargetQuantity; policy.BaseReward = baseReward;
            policy.MaximumScarcityBonus = maximumScarcityBonus; policy.OfferLifetimeTicks = offerLifetimeTicks;
            policy.PreparationDurationTicks = preparationDurationTicks; policy.PreparationPenalty = preparationPenalty;
            policy.WagonRequirement = Clone(wagonRequirement)!; policy.Enabled = enabled;
            policy.DeliveryDurationTicks = deliveryDurationTicks <= 0 ? offerLifetimeTicks : deliveryDurationTicks;
            Record(commandId, fingerprint, policyId, "transport-policy-configured");
            return policy;
        }
    }

    public IndustrialTransportNeed? PublishTransportNeed(string commandId, string policyId, long tick)
    {
        lock (gate)
        {
            RequireHost();
            var fingerprint = "publish-transport-need|" + policyId;
            var replay = Command(commandId, fingerprint);
            if (replay != null)
                return state.IndustrialTransportNeeds.SingleOrDefault(value => value.PublishedCommandId == commandId);
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            var policy = state.IndustrialTransportPolicies.Single(value => value.PolicyId == policyId);
            foreach (var expired in state.IndustrialTransportNeeds.Where(value => value.PolicyId == policyId && value.State == IndustrialNeedState.Available && value.ExpiresTick <= tick).ToArray())
            { expired.State = IndustrialNeedState.Expired; expired.Version++; }
            var existing = state.IndustrialTransportNeeds.SingleOrDefault(value => value.PolicyId == policyId && value.State == IndustrialNeedState.Available);
            if (!policy.Enabled || existing != null)
            {
                Record(commandId, fingerprint, policyId, existing == null ? "transport-need-not-required" : "transport-need-already-available");
                return existing;
            }
            var source = Stock(policy.OriginFacilityId, policy.CargoId);
            var destination = Stock(policy.DestinationFacilityId, policy.CargoId);
            var available = Math.Max(0m, source.OnHand - source.ReservedOutbound);
            var freeCapacity = Math.Max(0m, destination.Capacity - destination.OnHand - destination.ReservedInbound);
            var shortage = Math.Max(0m, policy.DestinationTargetQuantity - destination.OnHand - destination.ReservedInbound);
            var quantity = Math.Min(policy.BatchQuantity, Math.Min(available, Math.Min(freeCapacity, shortage)));
            if (quantity <= 0m)
            {
                Record(commandId, fingerprint, policyId, "transport-need-not-required");
                return null;
            }
            var scarcityRatio = policy.DestinationTargetQuantity <= 0m ? 0m : Math.Min(1m, shortage / policy.DestinationTargetQuantity);
            var scarcityBonus = decimal.ToInt64(decimal.Floor(policy.MaximumScarcityBonus * scarcityRatio));
            var need = new IndustrialTransportNeed
            {
                NeedId = policy.PolicyId + ":need:" + policy.NextNeedSequence, PolicyId = policy.PolicyId,
                OriginFacilityId = policy.OriginFacilityId, DestinationFacilityId = policy.DestinationFacilityId, CargoId = policy.CargoId,
                Quantity = quantity, BaseReward = policy.BaseReward, ScarcityBonus = scarcityBonus, PublishedTick = tick,
                ExpiresTick = checked(tick + policy.OfferLifetimeTicks), PreparationDurationTicks = policy.PreparationDurationTicks,
                PreparationPenalty = policy.PreparationPenalty, WagonRequirement = Clone(policy.WagonRequirement)!, State = IndustrialNeedState.Available,
                PublishedCommandId = commandId, Version = 1, DeliveryDurationTicks = policy.DeliveryDurationTicks
            };
            policy.NextNeedSequence++; policy.Version++; state.IndustrialTransportNeeds.Add(need);
            Record(commandId, fingerprint, need.NeedId, "transport-need-published");
            return need;
        }
    }

    public IndustrialContract AcceptTransportNeed(string commandId, string needId, long expectedNeedVersion, AccountRef beneficiary, AccountRef? penaltyPayer, long tick)
    {
        lock (gate)
        {
            RequireHost();
            var fingerprint = "accept-transport-need|" + needId + "|" + expectedNeedVersion + "|" + beneficiary?.Key + "|" + (penaltyPayer?.Key ?? "");
            var replay = Command(commandId, fingerprint);
            if (replay != null) return Contract(replay.AssignmentId);
            var need = state.IndustrialTransportNeeds.Single(value => value.NeedId == needId);
            if (beneficiary == null || !state.Economy.Wallets.Any(value => value.Account.Key == beneficiary.Key) || need.State != IndustrialNeedState.Available || need.Version != expectedNeedVersion || tick < need.PublishedTick || tick >= need.ExpiresTick)
                throw new InvalidOperationException("Transport need is unavailable, stale or has no valid beneficiary.");
            var contractId = need.NeedId + ":contract";
            var deliveryDeadline = checked(tick + need.DeliveryDurationTicks);
            var contract = CreateTransportOffer(contractId, need.OriginFacilityId, need.DestinationFacilityId, need.CargoId, need.Quantity,
                beneficiary, need.BaseReward, need.ScarcityBonus, tick, deliveryDeadline, need.WagonRequirement, need.PreparationPenalty);
            try
            {
                contract = Accept(commandId + ":reserve", contract.ContractId, contract.Version, tick, need.PreparationDurationTicks, penaltyPayer);
            }
            catch
            {
                state.IndustrialContracts.Remove(contract);
                state.IndustrialCommands.RemoveAll(value => value.CommandId == commandId + ":reserve");
                throw;
            }
            need.State = IndustrialNeedState.Accepted; need.AcceptedContractId = contract.ContractId; need.Version++;
            Record(commandId, fingerprint, contract.ContractId, "transport-need-accepted");
            return contract;
        }
    }

    public IndustrialContract CreateOffer(string contractId, string origin, string destination, string cargo, decimal quantity, AccountRef beneficiary, long baseReward, long scarcityBonus)
        => CreateTransportOffer(contractId, origin, destination, cargo, quantity, beneficiary, baseReward, scarcityBonus, 0, 0, null, 0);

    public IndustrialContract CreateTransportOffer(string contractId, string origin, string destination, string cargo, decimal quantity,
        AccountRef beneficiary, long baseReward, long scarcityBonus, long createdTick, long deadlineTick, WagonRequirement? wagonRequirement, long preparationPenalty)
    {
        lock (gate)
        {
            RequireHost(); var known = state.IndustrialContracts.SingleOrDefault(x => x.ContractId == contractId); if (known != null) return known;
            if (string.IsNullOrWhiteSpace(contractId) || string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination) || origin == destination || string.IsNullOrWhiteSpace(cargo) || quantity <= 0m || baseReward < 0 || scarcityBonus < 0 || createdTick < 0 || deadlineTick < 0 || (deadlineTick > 0 && deadlineTick <= createdTick) || preparationPenalty < 0 || beneficiary == null || !state.Economy.Wallets.Any(x => x.Account.Key == beneficiary.Key)) throw new ArgumentException("Invalid transport contract offer.");
            ValidateRequirement(wagonRequirement, cargo, quantity);
            var contract = new IndustrialContract { ContractId = contractId, OriginFacilityId = origin, DestinationFacilityId = destination, CargoId = cargo, Quantity = quantity, Beneficiary = Clone(beneficiary), BaseReward = baseReward, ScarcityBonus = scarcityBonus, CreatedTick = createdTick, DeliveryDeadlineTick = deadlineTick, WagonRequirement = Clone(wagonRequirement), PreparationPenalty = preparationPenalty, State = IndustrialContractState.Offered, Version = 1 };
            state.IndustrialContracts.Add(contract); return contract;
        }
    }

    public IndustrialContract Accept(string commandId, string contractId, long expectedContractVersion) => Accept(commandId, contractId, expectedContractVersion, 0, 1, null);
    public IndustrialContract Accept(string commandId, string contractId, long expectedContractVersion, long tick, long preparationDurationTicks, AccountRef? penaltyPayer)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = "accept|" + contractId + "|" + preparationDurationTicks + "|" + (penaltyPayer?.Key ?? ""); var replay = Command(commandId, fingerprint); if (replay != null) return Contract(replay.AssignmentId);
            var contract = Contract(contractId); if (contract.State != IndustrialContractState.Offered || contract.Version != expectedContractVersion) throw new InvalidOperationException("Transport contract is unavailable or stale.");
            if (tick < 0 || preparationDurationTicks <= 0 || (contract.DeliveryDeadlineTick > 0 && tick >= contract.DeliveryDeadlineTick)) throw new InvalidOperationException("Transport contract timing is invalid.");
            if (penaltyPayer != null && !state.Economy.Wallets.Any(x => x.Account.Key == penaltyPayer.Key)) throw new InvalidOperationException("Penalty payer is unknown.");
            Reserve(contract); var preparationExpiry = checked(tick + preparationDurationTicks); contract.PreparationExpiresTick = contract.DeliveryDeadlineTick > 0 ? Math.Min(preparationExpiry, contract.DeliveryDeadlineTick) : preparationExpiry; contract.PenaltyPayer = penaltyPayer == null ? null : Clone(penaltyPayer); contract.State = IndustrialContractState.Reserved; contract.Version++;
            Record(commandId, fingerprint, contractId); return contract;
        }
    }

    public IndustrialContract AssignWagons(string commandId, string requesterId, string contractId, long expectedVersion, AssetOwnerRef operatorRef, IReadOnlyList<string> assetIds)
        => AssignWagons(commandId, requesterId, contractId, expectedVersion, operatorRef, assetIds, 0);

    public IndustrialContract AssignWagons(string commandId, string requesterId, string contractId, long expectedVersion, AssetOwnerRef operatorRef, IReadOnlyList<string> assetIds, long authoritativeTick)
    {
        lock (gate)
        {
            RequireHost(); if (compatibility == null) throw new InvalidOperationException("A wagon compatibility adapter is required.");
            if (operatorRef == null) throw new ArgumentNullException(nameof(operatorRef));
            if (authoritativeTick < 0) throw new ArgumentOutOfRangeException(nameof(authoritativeTick));
            var ids = (assetIds ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(); var fingerprint = string.Join("|", "wagons", requesterId, contractId, operatorRef.Key, string.Join(",", ids)); var replay = Command(commandId, fingerprint); if (replay != null) return Contract(replay.AssignmentId);
            var contract = Contract(contractId); if (contract.State != IndustrialContractState.Reserved || contract.Version != expectedVersion || ids.Length == 0) throw new InvalidOperationException("Contract cannot accept wagon assignment.");
            var player = state.Economy.Players.Single(x => x.PlayerId == requesterId); if (!ControlsOperator(player, operatorRef)) throw new InvalidOperationException("Operator permission is required.");
            var assignments = new List<ContractWagonAssignment>();
            foreach (var id in ids)
            {
                var fleet = state.Fleet.Single(x => x.AssetId == id); var asset = state.Assets.Assets.Single(x => x.AssetId == id); var ownership = state.Ownership.Single(x => x.AssetId == id);
                if (fleet.Kind != FleetVehicleKind.FreightWagon || fleet.OperationalState != FleetOperationalState.Available || state.IndustrialContracts.Any(x => x.ContractId != contractId && !Terminal(x.State) && x.AssignedWagons.Any(w => w.AssetId == id))) throw new InvalidOperationException("Every wagon must be freight rolling stock, available and unreserved.");
                var lease = ActiveLease(id, operatorRef, authoritativeTick); if (ownership.Owner.Key != operatorRef.Key && lease == null) throw new InvalidOperationException("Every wagon must be owned or actively leased by the operator.");
                var result = compatibility.Inspect(id, asset.DefinitionId, contract.CargoId); if (result == null || !result.Compatible || result.Capacity <= 0m || (contract.WagonRequirement != null && contract.WagonRequirement.AllowedDefinitionIds.Count > 0 && !contract.WagonRequirement.AllowedDefinitionIds.Contains(asset.DefinitionId))) throw new InvalidOperationException("A wagon is incompatible with the contract cargo.");
                assignments.Add(new ContractWagonAssignment { AssetId = id, DefinitionId = asset.DefinitionId, Capacity = result.Capacity, LeaseId = lease?.LeaseId, Operator = Clone(operatorRef) });
            }
            if (contract.WagonRequirement != null && (assignments.Count < contract.WagonRequirement.MinimumWagonCount || assignments.Sum(x => x.Capacity) < contract.WagonRequirement.MinimumTotalCapacity)) throw new InvalidOperationException("Assigned wagons do not meet contract capacity requirements.");
            ReleaseWagons(contract); contract.Operator = Clone(operatorRef); contract.AssignedWagons = assignments; contract.Manifests = assignments.Select(x => new CargoManifest { AssetId = x.AssetId, CargoId = contract.CargoId }).ToList();
            foreach (var assignment in assignments) { var fleet = state.Fleet.Single(x => x.AssetId == assignment.AssetId); fleet.OperationalState = FleetOperationalState.Reserved; fleet.Operator = Clone(operatorRef); fleet.Version++; }
            contract.Version++; Record(commandId, fingerprint, contractId, "wagons-assigned"); return contract;
        }
    }

    public IReadOnlyList<CompatibleWagonOption> FindCompatibleWagons(string requesterId, string contractId, AssetOwnerRef operatorRef, long authoritativeTick)
    {
        lock (gate)
        {
            RequireHost();
            if (compatibility == null) throw new InvalidOperationException("A wagon compatibility adapter is required.");
            if (operatorRef == null || authoritativeTick < 0) throw new ArgumentException("A valid operator and authoritative tick are required.");
            var player = state.Economy.Players.Single(value => value.PlayerId == requesterId);
            if (!ControlsOperator(player, operatorRef)) throw new InvalidOperationException("Operator permission is required.");
            var contract = Contract(contractId);
            if (contract.State != IndustrialContractState.Reserved) return Array.Empty<CompatibleWagonOption>();
            var result = new List<CompatibleWagonOption>();
            foreach (var fleet in state.Fleet.Where(value => value.Kind == FleetVehicleKind.FreightWagon &&
                         (value.OperationalState == FleetOperationalState.Available || contract.AssignedWagons.Any(assigned => assigned.AssetId == value.AssetId))).OrderBy(value => value.DisplayName).ThenBy(value => value.AssetId))
            {
                if (state.IndustrialContracts.Any(value => value.ContractId != contractId && !Terminal(value.State) && value.AssignedWagons.Any(assigned => assigned.AssetId == fleet.AssetId))) continue;
                var asset = state.Assets.Assets.Single(value => value.AssetId == fleet.AssetId);
                var ownership = state.Ownership.Single(value => value.AssetId == fleet.AssetId);
                var lease = ActiveLease(fleet.AssetId, operatorRef, authoritativeTick);
                if (ownership.Owner.Key != operatorRef.Key && lease == null) continue;
                var observed = compatibility.Inspect(fleet.AssetId, asset.DefinitionId, contract.CargoId);
                if (observed == null || !observed.Compatible || observed.Capacity <= 0m ||
                    (contract.WagonRequirement != null && contract.WagonRequirement.AllowedDefinitionIds.Count > 0 && !contract.WagonRequirement.AllowedDefinitionIds.Contains(asset.DefinitionId))) continue;
                result.Add(new CompatibleWagonOption { AssetId = fleet.AssetId, DefinitionId = asset.DefinitionId, Capacity = observed.Capacity, LeaseId = lease?.LeaseId });
            }
            return result;
        }
    }

    public IndustrialContract Activate(string commandId, string contractId) => Activate(commandId, contractId, 0);
    public IndustrialContract Activate(string commandId, string contractId, long tick)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = "activate|" + contractId; var replay = Command(commandId, fingerprint); if (replay != null) return Contract(replay.AssignmentId);
            var contract = Contract(contractId); if (contract.State != IndustrialContractState.Reserved || (contract.PreparationExpiresTick > 0 && tick >= contract.PreparationExpiresTick)) throw new InvalidOperationException("Transport contract is not prepared or has expired.");
            if (contract.WagonRequirement != null && (contract.AssignedWagons.Count < contract.WagonRequirement.MinimumWagonCount || contract.AssignedWagons.Sum(x => x.Capacity) < contract.WagonRequirement.MinimumTotalCapacity)) throw new InvalidOperationException("Compatible operator wagons are required before loading.");
            if (contract.AssignedWagons.Any(x => !string.IsNullOrWhiteSpace(x.LeaseId) && !LeaseStillActive(x.LeaseId!, tick))) throw new InvalidOperationException("An assigned wagon lease expired before activation.");
            foreach (var wagon in contract.AssignedWagons) { var fleet = state.Fleet.Single(x => x.AssetId == wagon.AssetId); fleet.OperationalState = FleetOperationalState.InService; fleet.Version++; }
            contract.State = IndustrialContractState.Active; contract.Version++; Record(commandId, fingerprint, contractId); return contract;
        }
    }

    public IndustrialContract RecordLoading(string operationId, string contractId, string assetId, decimal cumulative)
        => RecordLoading(operationId, contractId, assetId, cumulative, state.LeaseClock.ActiveTick);

    public IndustrialContract RecordLoading(string operationId, string contractId, string assetId, decimal cumulative, long authoritativeTick)
    {
        lock (gate)
        {
            RequireHost();
            var fingerprint = string.Join("|", "loading", contractId, assetId, cumulative);
            var replay = Command(operationId, fingerprint);
            if (replay != null && replay.ResultCode != "pending-transfer") return Contract(replay.AssignmentId);
            var contract = Contract(contractId); var manifest = contract.Manifests.Single(x => x.AssetId == assetId);
            // Old checkpoints only carried the manifest operation list. Preserve their replay behavior
            // without inventing a payload fingerprint that was never persisted.
            if (replay == null && manifest.LoadOperationIds.Contains(operationId)) return contract;
            if (contract.State != IndustrialContractState.Active && contract.State != IndustrialContractState.DeliveryPending) throw new InvalidOperationException("Transport contract is not active.");
            if (authoritativeTick < 0) throw new ArgumentOutOfRangeException(nameof(authoritativeTick));
            EnsureAssignedWagonUsable(contract, assetId, authoritativeTick);
            if (string.IsNullOrWhiteSpace(operationId) || cumulative < manifest.LoadedQuantity || cumulative > contract.AssignedWagons.Single(x => x.AssetId == assetId).Capacity) throw new ArgumentException("Invalid cumulative load observation.");
            if (replay == null) { Record(operationId, fingerprint, contractId, "pending-transfer"); replay = Command(operationId, fingerprint); }
            if (transfers.InspectLoading(operationId, contractId, assetId, cumulative) != WorldOwnershipOutcome.Applied)
            {
                if (contract.State != IndustrialContractState.DeliveryPending) { contract.State = IndustrialContractState.DeliveryPending; contract.Version++; }
                return contract;
            }
            var delta = cumulative - manifest.LoadedQuantity; if (delta > 0m) { var source = Stock(contract.OriginFacilityId, contract.CargoId); if (source.OnHand < delta || source.ReservedOutbound < delta || contract.Manifests.Sum(x => x.LoadedQuantity) + delta > contract.Quantity) throw new InvalidOperationException("Source reservation conflicts with observed loading."); source.OnHand -= delta; source.ReservedOutbound -= delta; source.ReservedInbound += delta; source.Version++; manifest.LoadedQuantity = cumulative; }
            manifest.LoadOperationIds.Add(operationId); replay!.ResultCode = "ok"; contract.State = IndustrialContractState.Active; contract.Version++; return contract;
        }
    }

    public IndustrialContract RecordUnloading(string operationId, string contractId, string assetId, decimal cumulative)
        => RecordUnloading(operationId, contractId, assetId, cumulative, state.LeaseClock.ActiveTick);

    public IndustrialContract RecordUnloading(string operationId, string contractId, string assetId, decimal cumulative, long authoritativeTick)
    {
        lock (gate)
        {
            RequireHost();
            var fingerprint = string.Join("|", "unloading", contractId, assetId, cumulative);
            var replay = Command(operationId, fingerprint);
            if (replay != null && replay.ResultCode != "pending-transfer") return Contract(replay.AssignmentId);
            var contract = Contract(contractId); var manifest = contract.Manifests.Single(x => x.AssetId == assetId);
            if (replay == null && manifest.UnloadOperationIds.Contains(operationId)) return contract;
            if (contract.State != IndustrialContractState.Active && contract.State != IndustrialContractState.DeliveryPending) throw new InvalidOperationException("Transport contract is not active.");
            if (authoritativeTick < 0) throw new ArgumentOutOfRangeException(nameof(authoritativeTick));
            EnsureAssignedWagonUsable(contract, assetId, authoritativeTick);
            if (string.IsNullOrWhiteSpace(operationId) || cumulative < manifest.UnloadedQuantity || cumulative > manifest.LoadedQuantity) throw new ArgumentException("Invalid cumulative unload observation.");
            if (replay == null) { Record(operationId, fingerprint, contractId, "pending-transfer"); replay = Command(operationId, fingerprint); }
            if (transfers.InspectUnloading(operationId, contractId, assetId, cumulative) != WorldOwnershipOutcome.Applied)
            {
                if (contract.State != IndustrialContractState.DeliveryPending) { contract.State = IndustrialContractState.DeliveryPending; contract.Version++; }
                return contract;
            }
            var delta = cumulative - manifest.UnloadedQuantity; if (delta > 0m) { var source = Stock(contract.OriginFacilityId, contract.CargoId); var destination = Stock(contract.DestinationFacilityId, contract.CargoId); if (source.ReservedInbound < delta || destination.ReservedInbound < delta || destination.OnHand + delta > destination.Capacity) throw new InvalidOperationException("Destination reservation conflicts with observed unloading."); source.ReservedInbound -= delta; source.Version++; destination.OnHand += delta; destination.ReservedInbound -= delta; destination.Version++; manifest.UnloadedQuantity = cumulative; contract.DeliveredQuantity += delta; Pay(contract, operationId, delta); }
            manifest.UnloadOperationIds.Add(operationId); contract.DeliveryOperationIds.Add(operationId); replay!.ResultCode = "ok"; contract.State = contract.DeliveredQuantity == contract.Quantity ? IndustrialContractState.Completed : IndustrialContractState.Active;
            if (contract.State == IndustrialContractState.Completed) { contract.ReservationsReleased = true; ReleaseWagons(contract); } contract.Version++; return contract;
        }
    }

    // Legacy aggregate observer retained for W-027 checkpoint compatibility.
    public IndustrialContract RecognizeDelivery(string operationId, string contractId, decimal cumulative)
    {
        lock (gate)
        {
            RequireHost(); var contract = Contract(contractId); if (contract.DeliveryOperationIds.Contains(operationId)) return contract;
            if (contract.State != IndustrialContractState.Active && contract.State != IndustrialContractState.DeliveryPending) throw new InvalidOperationException("Industrial contract is not active.");
            if (contract.Manifests.Count > 0) throw new InvalidOperationException("Aggregate delivery is unavailable for wagon-manifest contracts; reconcile each physical wagon instead.");
            if (string.IsNullOrWhiteSpace(operationId) || cumulative < contract.DeliveredQuantity || cumulative > contract.Quantity) throw new ArgumentException("Invalid cumulative delivery.");
            if (execution.InspectDelivery(operationId, contractId, cumulative) != WorldOwnershipOutcome.Applied) { contract.State = IndustrialContractState.DeliveryPending; contract.Version++; return contract; }
            var delta = cumulative - contract.DeliveredQuantity; if (delta > 0m) { var source = Stock(contract.OriginFacilityId, contract.CargoId); var destination = Stock(contract.DestinationFacilityId, contract.CargoId); if (source.ReservedOutbound < delta || source.OnHand < delta || destination.ReservedInbound < delta || destination.OnHand + delta > destination.Capacity) throw new InvalidOperationException("Industrial reservation state conflicts with delivery."); source.OnHand -= delta; source.ReservedOutbound -= delta; source.Version++; destination.OnHand += delta; destination.ReservedInbound -= delta; destination.Version++; contract.DeliveredQuantity = cumulative; Pay(contract, operationId, delta); }
            contract.DeliveryOperationIds.Add(operationId); contract.State = cumulative == contract.Quantity ? IndustrialContractState.Completed : IndustrialContractState.Active; if (contract.State == IndustrialContractState.Completed) { contract.ReservationsReleased = true; ReleaseWagons(contract); } contract.Version++; return contract;
        }
    }

    public IndustrialContract ExpirePreparation(string commandId, string contractId, long tick)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = "expire|" + contractId; var replay = Command(commandId, fingerprint); if (replay != null) return Contract(replay.AssignmentId);
            var contract = Contract(contractId); if (contract.State != IndustrialContractState.Reserved || contract.PreparationExpiresTick <= 0 || tick < contract.PreparationExpiresTick) throw new InvalidOperationException("Preparation reservation has not expired.");
            ReleaseReservations(contract); ReleaseWagons(contract);
            if (contract.PreparationPenalty > 0 && contract.PenaltyPayer != null) { var wallet = state.Economy.Wallets.Single(x => x.Account.Key == contract.PenaltyPayer.Key); var charged = Math.Min(wallet.Balance, contract.PreparationPenalty); if (charged > 0) { wallet.Balance -= charged; wallet.Version++; contract.PenaltyApplied = charged; state.Economy.Ledger.Add(new LedgerEntry { EntryId = contract.ContractId + ":preparation-expiry", CommandId = commandId, Kind = LedgerEntryKind.PenaltyPayment, Debit = Clone(contract.PenaltyPayer), Amount = charged, Detail = "transport-preparation-expired;contract=" + contract.ContractId }); } }
            contract.State = IndustrialContractState.Expired; contract.Version++; Record(commandId, fingerprint, contractId, "preparation-expired"); return contract;
        }
    }

    public IndustrialContract Cancel(string commandId, string contractId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = "cancel|" + contractId; var replay = Command(commandId, fingerprint); if (replay != null) return Contract(replay.AssignmentId); var contract = Contract(contractId);
            if (Terminal(contract.State)) throw new InvalidOperationException("Transport contract is terminal."); ReleaseReservations(contract); ReleaseWagons(contract); contract.State = IndustrialContractState.Cancelled; contract.Version++; Record(commandId, fingerprint, contractId); return contract;
        }
    }

    public int RunRecipe(string commandId, string recipeId, int maximumCycles)
    {
        lock (gate) { RequireHost(); var fingerprint = "recipe|" + recipeId + "|" + maximumCycles; var replay = Command(commandId, fingerprint); if (replay != null) return int.Parse(replay.ResultCode); if (maximumCycles < 0) throw new ArgumentOutOfRangeException(nameof(maximumCycles)); var recipe = state.IndustrialRecipes.Single(x => x.RecipeId == recipeId); var cycles = ExecuteCycles(recipe, maximumCycles); Record(commandId, fingerprint, recipeId, cycles.ToString()); return cycles; }
    }

    public int AdvanceProduction(string commandId, string recipeId, long tick)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = "advance|" + recipeId; var replay = Command(commandId, fingerprint); if (replay != null) return int.Parse(replay.ResultCode); var recipe = state.IndustrialRecipes.Single(x => x.RecipeId == recipeId);
            if (tick < recipe.LastProductionTick || recipe.CadenceTicks <= 0 || recipe.MaximumBacklogCycles <= 0) throw new InvalidOperationException("Invalid production clock.");
            var due = (tick - recipe.LastProductionTick) / recipe.CadenceTicks; if (due > 0) { recipe.PendingCycles = Math.Min(recipe.MaximumBacklogCycles, checked(recipe.PendingCycles + (int)Math.Min(due, int.MaxValue))); recipe.LastProductionTick = checked(recipe.LastProductionTick + due * recipe.CadenceTicks); }
            var completed = ExecuteCycles(recipe, recipe.PendingCycles); recipe.PendingCycles -= completed; recipe.Version++; Record(commandId, fingerprint, recipeId, completed.ToString()); return completed;
        }
    }

    private int ExecuteCycles(IndustrialRecipe recipe, int max)
    {
        var input = Stock(recipe.FacilityId, recipe.InputCargoId); var output = Stock(recipe.FacilityId, recipe.OutputCargoId); var cycles = 0;
        while (cycles < max && input.OnHand - input.ReservedOutbound >= recipe.InputQuantity && output.Capacity - output.OnHand - output.ReservedInbound >= recipe.OutputQuantity) { input.OnHand -= recipe.InputQuantity; output.OnHand += recipe.OutputQuantity; cycles++; }
        if (cycles > 0) { input.Version++; output.Version++; recipe.CompletedCycles = checked(recipe.CompletedCycles + cycles); } return cycles;
    }

    private void Reserve(IndustrialContract c) { var source = Stock(c.OriginFacilityId, c.CargoId); var destination = Stock(c.DestinationFacilityId, c.CargoId); if (source.OnHand - source.ReservedOutbound < c.Quantity) throw new InvalidOperationException("Industrial source stock is insufficient."); if (destination.Capacity - destination.OnHand - destination.ReservedInbound < c.Quantity) throw new InvalidOperationException("Industrial destination capacity is insufficient."); source.ReservedOutbound += c.Quantity; source.Version++; destination.ReservedInbound += c.Quantity; destination.Version++; }
    private void ReleaseReservations(IndustrialContract c) { if (c.ReservationsReleased || c.State == IndustrialContractState.Offered) return; var loaded = c.Manifests.Sum(x => x.LoadedQuantity); var onBoard = c.Manifests.Sum(x => Math.Max(0m, x.LoadedQuantity - x.UnloadedQuantity)); var source = Stock(c.OriginFacilityId, c.CargoId); var destination = Stock(c.DestinationFacilityId, c.CargoId); source.ReservedOutbound -= Math.Min(source.ReservedOutbound, Math.Max(0m, c.Quantity - loaded)); if (source.ReservedInbound < onBoard) throw new InvalidOperationException("In-transit return capacity reservation is missing."); source.ReservedInbound -= onBoard; source.OnHand += onBoard; if (source.OnHand > source.Capacity) throw new InvalidOperationException("Cancelled in-transit cargo cannot be restored within source capacity."); source.Version++; destination.ReservedInbound -= Math.Min(destination.ReservedInbound, Math.Max(0m, c.Quantity - c.DeliveredQuantity)); destination.Version++; c.ReservationsReleased = true; }
    private void ReleaseWagons(IndustrialContract c) { foreach (var wagon in c.AssignedWagons) { var fleet = state.Fleet.SingleOrDefault(x => x.AssetId == wagon.AssetId); if (fleet != null && (fleet.OperationalState == FleetOperationalState.Reserved || fleet.OperationalState == FleetOperationalState.InService)) { fleet.OperationalState = FleetOperationalState.Available; fleet.Version++; } } }
    private void Pay(IndustrialContract c, string operationId, decimal delta) { var total = checked(c.BaseReward + c.ScarcityBonus); var cumulative = decimal.ToInt64(decimal.Floor(total * c.DeliveredQuantity / c.Quantity)); var payment = cumulative - c.PaidAmount; if (payment <= 0) return; var wallet = state.Economy.Wallets.Single(x => x.Account.Key == c.Beneficiary.Key); wallet.Balance = checked(wallet.Balance + payment); wallet.Version++; state.Economy.Ledger.Add(new LedgerEntry { EntryId = c.ContractId + ":delivery:" + operationId, CommandId = operationId, Kind = LedgerEntryKind.IndustrialRevenue, Credit = Clone(c.Beneficiary), Amount = payment, Detail = "transport-delivery;contract=" + c.ContractId + ";delta=" + delta + ";cumulative=" + c.DeliveredQuantity }); c.PaidAmount += payment; }
    private IndustrialContract Contract(string id) => state.IndustrialContracts.Single(x => x.ContractId == id);
    private IndustrialContract Active(string id) { var c = Contract(id); if (c.State != IndustrialContractState.Active && c.State != IndustrialContractState.DeliveryPending) throw new InvalidOperationException("Transport contract is not active."); return c; }
    private IndustrialStock Stock(string facility, string cargo) => state.IndustrialStocks.Single(x => x.FacilityId == facility && x.CargoId == cargo);
    private LeaseContract? ActiveLease(string id, AssetOwnerRef op, long tick) => state.Leases.SingleOrDefault(x => x.AssetIds.Contains(id) && (x.State == LeaseState.Active || x.State == LeaseState.Delinquent) && x.Lessee != null && x.Lessee.Key == op.Key && (x.EndTick <= 0 || tick < x.EndTick));
    private bool LeaseStillActive(string leaseId, long tick) { var lease = state.Leases.SingleOrDefault(x => x.LeaseId == leaseId); return lease != null && (lease.State == LeaseState.Active || lease.State == LeaseState.Delinquent) && (lease.EndTick <= 0 || tick < lease.EndTick); }
    private void EnsureAssignedWagonUsable(IndustrialContract contract, string assetId, long authoritativeTick)
    {
        var assigned = contract.AssignedWagons.Single(x => x.AssetId == assetId);
        var fleet = state.Fleet.SingleOrDefault(x => x.AssetId == assetId);
        if (fleet == null || fleet.OperationalState != FleetOperationalState.InService) throw new InvalidOperationException("Assigned wagon is unavailable; no replacement will be created.");
        var ownership = state.Ownership.SingleOrDefault(x => x.AssetId == assetId);
        if (ownership == null || contract.Operator == null || (ownership.Owner.Key != contract.Operator.Key && (string.IsNullOrWhiteSpace(assigned.LeaseId) || !LeaseStillActive(assigned.LeaseId!, authoritativeTick)))) throw new InvalidOperationException("Assigned wagon is no longer controlled by the operator.");
    }
    private bool ControlsOperator(PlayerEconomicState p, AssetOwnerRef op) { if (op == null) return false; if (op.Kind == AssetOwnerKind.Player) return op.OwnerId == p.PlayerId; if (op.Kind != AssetOwnerKind.Company || p.CompanyId != op.OwnerId) return false; var c = state.Economy.Companies.SingleOrDefault(x => x.CompanyId == op.OwnerId); return c != null && !c.Liquidating && (c.LeaderId == p.PlayerId || (c.DelegatedPermissions.TryGetValue(p.PlayerId, out var rights) && rights.Contains(CompanyPermission.ManageFleet))); }
    private void ValidateTransportPolicy(string policyId, string origin, string destination, string cargo, decimal batchQuantity, decimal destinationTargetQuantity,
        long baseReward, long maximumScarcityBonus, long offerLifetimeTicks, long preparationDurationTicks, long preparationPenalty, WagonRequirement requirement,
        long deliveryDurationTicks)
    {
        if (string.IsNullOrWhiteSpace(policyId) || string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination) || origin == destination ||
            string.IsNullOrWhiteSpace(cargo) || batchQuantity <= 0m || destinationTargetQuantity <= 0m || baseReward < 0 || maximumScarcityBonus < 0 ||
            offerLifetimeTicks <= 0 || preparationDurationTicks <= 0 || deliveryDurationTicks <= 0 || preparationPenalty < 0 ||
            !state.IndustrialStocks.Any(value => value.FacilityId == origin && value.CargoId == cargo) ||
            !state.IndustrialStocks.Any(value => value.FacilityId == destination && value.CargoId == cargo))
            throw new ArgumentException("Invalid industrial transport policy.");
        ValidateRequirement(requirement, cargo, batchQuantity);
    }
    private MissionAssignmentCommand? Command(string id, string fingerprint) { if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A command ID is required."); var r = state.IndustrialCommands.SingleOrDefault(x => x.CommandId == id); if (r != null && r.Fingerprint != fingerprint) throw new InvalidOperationException("Industrial command ID payload conflict."); return r; }
    private void Record(string id, string fingerprint, string contract, string result = "ok") => state.IndustrialCommands.Add(new MissionAssignmentCommand { CommandId = id, Fingerprint = fingerprint, AssignmentId = contract, ResultCode = result });
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
    private static bool Terminal(IndustrialContractState value) => value == IndustrialContractState.Completed || value == IndustrialContractState.Cancelled || value == IndustrialContractState.Expired;
    private static AccountRef Clone(AccountRef x) => new AccountRef { Kind = x.Kind, OwnerId = x.OwnerId };
    private static AssetOwnerRef Clone(AssetOwnerRef x) => new AssetOwnerRef { Kind = x.Kind, OwnerId = x.OwnerId };
    private static WagonRequirement? Clone(WagonRequirement? x) => x == null ? null : new WagonRequirement { CargoId = x.CargoId, MinimumWagonCount = x.MinimumWagonCount, MinimumTotalCapacity = x.MinimumTotalCapacity, AllowedDefinitionIds = x.AllowedDefinitionIds?.Distinct(StringComparer.Ordinal).ToList() ?? new List<string>() };
    private static void ValidateRequirement(WagonRequirement? r, string cargo, decimal quantity) { if (r != null && (!string.Equals(r.CargoId, cargo, StringComparison.Ordinal) || r.MinimumWagonCount <= 0 || r.MinimumTotalCapacity < quantity || r.AllowedDefinitionIds == null || r.AllowedDefinitionIds.Any(string.IsNullOrWhiteSpace))) throw new ArgumentException("Invalid wagon requirement."); }
}

public static class IndustrialEconomyValidation
{
    public static void Validate(VehicleAcquisitionSnapshot state)
    {
        if (state.IndustrialStocks.GroupBy(x => x.Key).Any(x => x.Count() != 1) || state.IndustrialRecipes.GroupBy(x => x.RecipeId).Any(x => x.Count() != 1) || state.IndustrialContracts.GroupBy(x => x.ContractId).Any(x => x.Count() != 1) || state.IndustrialCommands.GroupBy(x => x.CommandId).Any(x => x.Count() != 1) || state.IndustrialTransportPolicies.GroupBy(x => x.PolicyId).Any(x => x.Count() != 1) || state.IndustrialTransportNeeds.GroupBy(x => x.NeedId).Any(x => x.Count() != 1)) throw new InvalidOperationException("Duplicate industrial identity.");
        foreach (var s in state.IndustrialStocks) if (string.IsNullOrWhiteSpace(s.FacilityId) || string.IsNullOrWhiteSpace(s.CargoId) || s.OnHand < 0m || s.Capacity < 0m || s.OnHand > s.Capacity || s.ReservedOutbound < 0m || s.ReservedOutbound > s.OnHand || s.ReservedInbound < 0m || s.OnHand + s.ReservedInbound > s.Capacity) throw new InvalidOperationException("Invalid industrial stock.");
        foreach (var r in state.IndustrialRecipes) if (string.IsNullOrWhiteSpace(r.RecipeId) || r.InputQuantity <= 0m || r.OutputQuantity <= 0m || r.CadenceTicks <= 0 || r.PendingCycles < 0 || r.MaximumBacklogCycles <= 0 || r.PendingCycles > r.MaximumBacklogCycles || !state.IndustrialStocks.Any(x => x.FacilityId == r.FacilityId && x.CargoId == r.InputCargoId) || !state.IndustrialStocks.Any(x => x.FacilityId == r.FacilityId && x.CargoId == r.OutputCargoId)) throw new InvalidOperationException("Invalid industrial recipe.");
        foreach (var p in state.IndustrialTransportPolicies) if (string.IsNullOrWhiteSpace(p.PolicyId) || string.IsNullOrWhiteSpace(p.OriginFacilityId) || string.IsNullOrWhiteSpace(p.DestinationFacilityId) || p.OriginFacilityId == p.DestinationFacilityId || string.IsNullOrWhiteSpace(p.CargoId) || p.BatchQuantity <= 0m || p.DestinationTargetQuantity <= 0m || p.BaseReward < 0 || p.MaximumScarcityBonus < 0 || p.OfferLifetimeTicks <= 0 || p.PreparationDurationTicks <= 0 || p.DeliveryDurationTicks <= 0 || p.PreparationPenalty < 0 || p.NextNeedSequence <= 0 || p.Version <= 0 || p.WagonRequirement == null || !state.IndustrialStocks.Any(x => x.FacilityId == p.OriginFacilityId && x.CargoId == p.CargoId) || !state.IndustrialStocks.Any(x => x.FacilityId == p.DestinationFacilityId && x.CargoId == p.CargoId)) throw new InvalidOperationException("Invalid industrial transport policy.");
        foreach (var n in state.IndustrialTransportNeeds) if (string.IsNullOrWhiteSpace(n.NeedId) || string.IsNullOrWhiteSpace(n.PolicyId) || !state.IndustrialTransportPolicies.Any(p => p.PolicyId == n.PolicyId) || n.Quantity <= 0m || n.BaseReward < 0 || n.ScarcityBonus < 0 || n.PublishedTick < 0 || n.ExpiresTick <= n.PublishedTick || n.PreparationDurationTicks <= 0 || n.DeliveryDurationTicks <= 0 || n.PreparationPenalty < 0 || n.WagonRequirement == null || n.Version <= 0 || (n.State == IndustrialNeedState.Accepted && (string.IsNullOrWhiteSpace(n.AcceptedContractId) || !state.IndustrialContracts.Any(c => c.ContractId == n.AcceptedContractId)))) throw new InvalidOperationException("Invalid industrial transport need.");
        foreach (var c in state.IndustrialContracts) { c.DeliveryOperationIds = c.DeliveryOperationIds ?? new List<string>(); c.AssignedWagons = c.AssignedWagons ?? new List<ContractWagonAssignment>(); c.Manifests = c.Manifests ?? new List<CargoManifest>(); if (c.SchemaVersion == 0) c.SchemaVersion = 1; if (string.IsNullOrWhiteSpace(c.ContractId) || c.Quantity <= 0m || c.DeliveredQuantity < 0m || c.DeliveredQuantity > c.Quantity || c.PaidAmount < 0 || c.BaseReward < 0 || c.ScarcityBonus < 0 || c.Beneficiary == null || c.AssignedWagons.GroupBy(x => x.AssetId).Any(x => x.Count() > 1) || c.Manifests.GroupBy(x => x.AssetId).Any(x => x.Count() > 1) || c.Manifests.Any(x => x.UnloadedQuantity < 0m || x.LoadedQuantity < x.UnloadedQuantity)) throw new InvalidOperationException("Invalid transport contract."); }
    }
}
