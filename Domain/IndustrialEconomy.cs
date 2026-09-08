using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum IndustrialContractState { Offered, Reserved, Active, Completed, Cancelled, DeliveryPending }

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
}

[DataContract]
public sealed class IndustrialContract
{
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
}

public interface IIndustrialExecutionPort
{
    bool Available { get; }
    WorldOwnershipOutcome InspectDelivery(string operationId, string contractId, decimal cumulativeQuantity);
}

public interface ICompetingGeneratorControl
{
    bool CanSuspendNewGeneration { get; }
    bool TrySuspendNewGeneration(string operationId);
}

public sealed class DisabledIndustrialExecutionPort : IIndustrialExecutionPort
{
    public bool Available => false;
    public WorldOwnershipOutcome InspectDelivery(string operationId, string contractId, decimal cumulativeQuantity) => WorldOwnershipOutcome.NotApplied;
}

public static class IndustrialRuntimeGate
{
    public static bool TryEnable(string operationId, IIndustrialExecutionPort execution, ICompetingGeneratorControl generator)
        => execution != null && execution.Available && generator != null && generator.CanSuspendNewGeneration && generator.TrySuspendNewGeneration(operationId);
}

public sealed class IndustrialEconomyEngine
{
    private readonly object gate = new object(); private readonly VehicleAcquisitionSnapshot state; private readonly INetworkRoleDetector authority; private readonly IIndustrialExecutionPort execution;
    public IndustrialEconomyEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IIndustrialExecutionPort execution) { this.state = state; this.authority = authority; this.execution = execution; VehicleAcquisitionPersistence.Validate(state); }

    public IndustrialContract CreateOffer(string contractId, string origin, string destination, string cargo, decimal quantity, AccountRef beneficiary, long baseReward, long scarcityBonus)
    {
        lock (gate)
        {
            RequireHost(); var known = state.IndustrialContracts.SingleOrDefault(x => x.ContractId == contractId); if (known != null) return known;
            if (string.IsNullOrWhiteSpace(contractId) || string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination) || origin == destination || string.IsNullOrWhiteSpace(cargo) || quantity <= 0m || baseReward < 0 || scarcityBonus < 0 || beneficiary == null || !state.Economy.Wallets.Any(x => x.Account.Key == beneficiary.Key)) throw new ArgumentException("Invalid industrial contract offer.");
            var contract = new IndustrialContract { ContractId = contractId, OriginFacilityId = origin, DestinationFacilityId = destination, CargoId = cargo, Quantity = quantity, Beneficiary = Clone(beneficiary), BaseReward = baseReward, ScarcityBonus = scarcityBonus, State = IndustrialContractState.Offered, Version = 1 };
            state.IndustrialContracts.Add(contract); return contract;
        }
    }

    public IndustrialContract Accept(string commandId, string contractId, long expectedContractVersion)
    {
        lock (gate)
        {
            RequireHost(); var replay = Command(commandId, "accept|" + contractId + "|" + expectedContractVersion); if (replay != null) return state.IndustrialContracts.Single(x => x.ContractId == replay.AssignmentId);
            var contract = state.IndustrialContracts.Single(x => x.ContractId == contractId); if (contract.State != IndustrialContractState.Offered || contract.Version != expectedContractVersion) throw new InvalidOperationException("Industrial contract is unavailable or stale.");
            var source = Stock(contract.OriginFacilityId, contract.CargoId); var destination = Stock(contract.DestinationFacilityId, contract.CargoId);
            if (source.OnHand - source.ReservedOutbound < contract.Quantity) throw new InvalidOperationException("Industrial source stock is insufficient.");
            if (destination.Capacity - destination.OnHand - destination.ReservedInbound < contract.Quantity) throw new InvalidOperationException("Industrial destination capacity is insufficient.");
            source.ReservedOutbound += contract.Quantity; source.Version++; destination.ReservedInbound += contract.Quantity; destination.Version++; contract.State = IndustrialContractState.Reserved; contract.Version++;
            Record(commandId, "accept|" + contractId + "|" + expectedContractVersion, contractId); return contract;
        }
    }

    public IndustrialContract Activate(string commandId, string contractId)
    {
        lock (gate) { RequireHost(); var replay = Command(commandId, "activate|" + contractId); if (replay != null) return state.IndustrialContracts.Single(x => x.ContractId == replay.AssignmentId); var c = state.IndustrialContracts.Single(x => x.ContractId == contractId); if (c.State != IndustrialContractState.Reserved) throw new InvalidOperationException("Industrial contract is not reserved."); c.State = IndustrialContractState.Active; c.Version++; Record(commandId, "activate|" + contractId, contractId); return c; }
    }

    public IndustrialContract RecognizeDelivery(string operationId, string contractId, decimal cumulativeQuantity)
    {
        lock (gate)
        {
            RequireHost(); var contract = state.IndustrialContracts.Single(x => x.ContractId == contractId); if (contract.DeliveryOperationIds.Contains(operationId)) return contract;
            if (contract.State != IndustrialContractState.Active && contract.State != IndustrialContractState.DeliveryPending) throw new InvalidOperationException("Industrial contract is not active.");
            if (string.IsNullOrWhiteSpace(operationId) || cumulativeQuantity < contract.DeliveredQuantity || cumulativeQuantity > contract.Quantity) throw new ArgumentException("Invalid cumulative delivery.");
            var observed = execution.InspectDelivery(operationId, contractId, cumulativeQuantity); if (observed != WorldOwnershipOutcome.Applied) { contract.State = IndustrialContractState.DeliveryPending; contract.Version++; return contract; }
            var delta = cumulativeQuantity - contract.DeliveredQuantity; if (delta == 0m) { contract.DeliveryOperationIds.Add(operationId); return contract; }
            var source = Stock(contract.OriginFacilityId, contract.CargoId); var destination = Stock(contract.DestinationFacilityId, contract.CargoId);
            if (source.ReservedOutbound < delta || source.OnHand < delta || destination.ReservedInbound < delta || destination.OnHand + delta > destination.Capacity) throw new InvalidOperationException("Industrial reservation state conflicts with delivery.");
            source.OnHand -= delta; source.ReservedOutbound -= delta; source.Version++; destination.OnHand += delta; destination.ReservedInbound -= delta; destination.Version++; contract.DeliveredQuantity = cumulativeQuantity;
            var totalReward = checked(contract.BaseReward + contract.ScarcityBonus); var cumulativePay = decimal.ToInt64(decimal.Floor(totalReward * cumulativeQuantity / contract.Quantity)); var payment = cumulativePay - contract.PaidAmount;
            if (payment > 0) { var wallet = state.Economy.Wallets.Single(x => x.Account.Key == contract.Beneficiary.Key); wallet.Balance = checked(wallet.Balance + payment); wallet.Version++; state.Economy.Ledger.Add(new LedgerEntry { EntryId = contract.ContractId + ":delivery:" + operationId, CommandId = operationId, Kind = LedgerEntryKind.IndustrialRevenue, Credit = Clone(contract.Beneficiary), Amount = payment, Detail = "industrial-delivery;contract=" + contract.ContractId + ";delta=" + delta + ";cumulative=" + cumulativeQuantity }); contract.PaidAmount += payment; }
            contract.DeliveryOperationIds.Add(operationId); contract.State = cumulativeQuantity == contract.Quantity ? IndustrialContractState.Completed : IndustrialContractState.Active; contract.Version++; return contract;
        }
    }

    public IndustrialContract Cancel(string commandId, string contractId)
    {
        lock (gate)
        {
            RequireHost(); var replay = Command(commandId, "cancel|" + contractId); if (replay != null) return state.IndustrialContracts.Single(x => x.ContractId == replay.AssignmentId); var c = state.IndustrialContracts.Single(x => x.ContractId == contractId);
            if (c.State == IndustrialContractState.Completed || c.State == IndustrialContractState.Cancelled) throw new InvalidOperationException("Industrial contract is terminal."); var remaining = c.Quantity - c.DeliveredQuantity;
            if (c.State != IndustrialContractState.Offered) { var source = Stock(c.OriginFacilityId, c.CargoId); var destination = Stock(c.DestinationFacilityId, c.CargoId); source.ReservedOutbound -= remaining; source.Version++; destination.ReservedInbound -= remaining; destination.Version++; }
            c.State = IndustrialContractState.Cancelled; c.Version++; Record(commandId, "cancel|" + contractId, contractId); return c;
        }
    }

    public int RunRecipe(string commandId, string recipeId, int maximumCycles)
    {
        lock (gate)
        {
            RequireHost(); var replay = Command(commandId, "recipe|" + recipeId + "|" + maximumCycles); if (replay != null) return int.Parse(replay.ResultCode);
            if (maximumCycles < 0) throw new ArgumentOutOfRangeException(nameof(maximumCycles)); var recipe = state.IndustrialRecipes.Single(x => x.RecipeId == recipeId); var input = Stock(recipe.FacilityId, recipe.InputCargoId); var output = Stock(recipe.FacilityId, recipe.OutputCargoId); var cycles = 0;
            while (cycles < maximumCycles && input.OnHand - input.ReservedOutbound >= recipe.InputQuantity && output.Capacity - output.OnHand - output.ReservedInbound >= recipe.OutputQuantity) { input.OnHand -= recipe.InputQuantity; output.OnHand += recipe.OutputQuantity; cycles++; }
            if (cycles > 0) { input.Version++; output.Version++; } Record(commandId, "recipe|" + recipeId + "|" + maximumCycles, recipeId, cycles.ToString()); return cycles;
        }
    }

    private IndustrialStock Stock(string facility, string cargo) => state.IndustrialStocks.Single(x => x.FacilityId == facility && x.CargoId == cargo);
    private MissionAssignmentCommand? Command(string id, string fingerprint) { var r = state.IndustrialCommands.SingleOrDefault(x => x.CommandId == id); if (r != null && r.Fingerprint != fingerprint) throw new InvalidOperationException("Industrial command ID payload conflict."); return r; }
    private void Record(string id, string fingerprint, string contract, string result = "ok") => state.IndustrialCommands.Add(new MissionAssignmentCommand { CommandId = id, Fingerprint = fingerprint, AssignmentId = contract, ResultCode = result });
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
    private static AccountRef Clone(AccountRef x) => new AccountRef { Kind = x.Kind, OwnerId = x.OwnerId };
}

public static class IndustrialEconomyValidation
{
    public static void Validate(VehicleAcquisitionSnapshot state)
    {
        if (state.IndustrialStocks.GroupBy(x => x.Key).Any(x => x.Count() != 1) || state.IndustrialContracts.GroupBy(x => x.ContractId).Any(x => x.Count() != 1) || state.IndustrialCommands.GroupBy(x => x.CommandId).Any(x => x.Count() != 1)) throw new InvalidOperationException("Duplicate industrial identity.");
        foreach (var s in state.IndustrialStocks) if (string.IsNullOrWhiteSpace(s.FacilityId) || string.IsNullOrWhiteSpace(s.CargoId) || s.OnHand < 0m || s.Capacity < 0m || s.OnHand > s.Capacity || s.ReservedOutbound < 0m || s.ReservedOutbound > s.OnHand || s.ReservedInbound < 0m || s.OnHand + s.ReservedInbound > s.Capacity) throw new InvalidOperationException("Invalid industrial stock.");
        foreach (var r in state.IndustrialRecipes) if (string.IsNullOrWhiteSpace(r.RecipeId) || r.InputQuantity <= 0m || r.OutputQuantity <= 0m || !state.IndustrialStocks.Any(x => x.FacilityId == r.FacilityId && x.CargoId == r.InputCargoId) || !state.IndustrialStocks.Any(x => x.FacilityId == r.FacilityId && x.CargoId == r.OutputCargoId)) throw new InvalidOperationException("Invalid industrial recipe.");
        foreach (var c in state.IndustrialContracts) if (string.IsNullOrWhiteSpace(c.ContractId) || c.Quantity <= 0m || c.DeliveredQuantity < 0m || c.DeliveredQuantity > c.Quantity || c.PaidAmount < 0 || c.BaseReward < 0 || c.ScarcityBonus < 0 || c.Beneficiary == null) throw new InvalidOperationException("Invalid industrial contract.");
    }
}
