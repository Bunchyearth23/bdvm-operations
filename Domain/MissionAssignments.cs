using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum MissionAssignmentKind { Freight, Passenger }
public enum MissionAssignmentState { Reserved, Active, CompletionPending, Completed, Cancelled, Rejected }

[DataContract]
public sealed class MissionAssignment
{
    [DataMember(Name = "assignmentId", Order = 1)] public string AssignmentId { get; set; } = "";
    [DataMember(Name = "missionId", Order = 2)] public string MissionId { get; set; } = "";
    [DataMember(Name = "kind", Order = 3)] public MissionAssignmentKind Kind { get; set; }
    [DataMember(Name = "assetIds", Order = 4)] public List<string> AssetIds { get; set; } = new List<string>();
    [DataMember(Name = "operator", Order = 5)] public AssetOwnerRef Operator { get; set; } = new AssetOwnerRef();
    [DataMember(Name = "requestedBy", Order = 6)] public string RequestedBy { get; set; } = "";
    [DataMember(Name = "maximumExpectedRevenue", Order = 7)] public long MaximumExpectedRevenue { get; set; }
    [DataMember(Name = "state", Order = 8)] public MissionAssignmentState State { get; set; }
    [DataMember(Name = "version", Order = 9)] public long Version { get; set; }
    [DataMember(Name = "vanillaBalanceBefore", Order = 10)] public long VanillaBalanceBefore { get; set; }
    [DataMember(Name = "vanillaBalanceAfter", Order = 11)] public long VanillaBalanceAfter { get; set; }
    [DataMember(Name = "actualRevenue", Order = 12)] public long ActualRevenue { get; set; }
    [DataMember(Name = "externalSettlement", Order = 13)] public ExternalSettlementState ExternalSettlement { get; set; }
    [DataMember(Name = "expectedVanillaBalance", Order = 14)] public long ExpectedVanillaBalance { get; set; }
    [DataMember(Name = "resultCode", Order = 15)] public string ResultCode { get; set; } = "";
}

[DataContract]
public sealed class MissionAssignmentCommand
{
    [DataMember(Name = "commandId", Order = 1)] public string CommandId { get; set; } = "";
    [DataMember(Name = "fingerprint", Order = 2)] public string Fingerprint { get; set; } = "";
    [DataMember(Name = "assignmentId", Order = 3)] public string AssignmentId { get; set; } = "";
    [DataMember(Name = "resultCode", Order = 4)] public string ResultCode { get; set; } = "";
}

public interface IMissionCompletionPort
{
    WorldOwnershipOutcome Inspect(string missionId, IReadOnlyList<string> persistentCarGuids);
}

public sealed class ManualMissionCompletionPort : IMissionCompletionPort
{
    public WorldOwnershipOutcome Inspect(string missionId, IReadOnlyList<string> persistentCarGuids) => WorldOwnershipOutcome.Applied;
}

public sealed class MissionAssignmentEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly IMissionCompletionPort completion;
    public MissionAssignmentEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IMissionCompletionPort completion) { this.state = state; this.authority = authority; this.completion = completion; VehicleAcquisitionPersistence.Validate(state); }

    public MissionAssignment Reserve(string commandId, string requesterId, string assignmentId, string missionId, MissionAssignmentKind kind,
        IReadOnlyList<string> assetIds, AssetOwnerRef operatorRef, long maximumExpectedRevenue)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = string.Join("|", requesterId, assignmentId, missionId, kind, string.Join(",", assetIds.OrderBy(x => x)), operatorRef.Key, maximumExpectedRevenue);
            var previous = Known(commandId, fingerprint); if (previous != null) return state.Assignments.Single(x => x.AssignmentId == previous.AssignmentId);
            var ids = assetIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (string.IsNullOrWhiteSpace(assignmentId) || string.IsNullOrWhiteSpace(missionId) || ids.Count == 0 || maximumExpectedRevenue < 0 || state.Assignments.Any(x => x.AssignmentId == assignmentId)) throw new ArgumentException("Invalid or duplicate mission assignment.");
            var player = state.Economy.Players.Single(x => x.PlayerId == requesterId); if (!ControlsOperator(player, operatorRef)) throw new InvalidOperationException("Operator permission is required.");
            foreach (var id in ids)
            {
                var fleet = state.Fleet.Single(x => x.AssetId == id); var owner = state.Ownership.Single(x => x.AssetId == id).Owner;
                if (fleet.OperationalState != FleetOperationalState.Available || state.Assignments.Any(x => x.AssetIds.Contains(id) && (x.State == MissionAssignmentState.Reserved || x.State == MissionAssignmentState.Active || x.State == MissionAssignmentState.CompletionPending))) throw new InvalidOperationException("Every assigned asset must be available and unreserved.");
                if (owner.Key != operatorRef.Key && fleet.Operator?.Key != operatorRef.Key) throw new InvalidOperationException("Assignment operator does not own or lease every asset.");
            }
            var assignment = new MissionAssignment { AssignmentId = assignmentId, MissionId = missionId, Kind = kind, AssetIds = ids, Operator = Clone(operatorRef), RequestedBy = requesterId, MaximumExpectedRevenue = maximumExpectedRevenue, State = MissionAssignmentState.Reserved, Version = 1, ResultCode = "assignment-reserved" };
            state.Assignments.Add(assignment); foreach (var id in ids) { var f = state.Fleet.Single(x => x.AssetId == id); f.OperationalState = FleetOperationalState.Reserved; f.Operator = Clone(operatorRef); f.Version++; }
            Record(commandId, fingerprint, assignmentId, "assignment-reserved"); return assignment;
        }
    }

    public MissionAssignment Start(string commandId, string requesterId, string assignmentId, long authoritativeVanillaBalance)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = requesterId + "|" + assignmentId + "|start|" + authoritativeVanillaBalance; var previous = Known(commandId, fingerprint); if (previous != null) return state.Assignments.Single(x => x.AssignmentId == previous.AssignmentId);
            var assignment = state.Assignments.Single(x => x.AssignmentId == assignmentId); if (assignment.State != MissionAssignmentState.Reserved || !ControlsOperator(state.Economy.Players.Single(x => x.PlayerId == requesterId), assignment.Operator) || authoritativeVanillaBalance < 0) throw new InvalidOperationException("Assignment cannot start.");
            assignment.VanillaBalanceBefore = authoritativeVanillaBalance; assignment.State = MissionAssignmentState.Active; assignment.Version++; assignment.ResultCode = "assignment-active";
            foreach (var id in assignment.AssetIds) { var f = state.Fleet.Single(x => x.AssetId == id); f.OperationalState = FleetOperationalState.InService; f.Version++; }
            Record(commandId, fingerprint, assignmentId, assignment.ResultCode); return assignment;
        }
    }

    public MissionAssignment Complete(string commandId, string requesterId, string assignmentId, long authoritativeVanillaBalance,
        IReadOnlyList<string> arrivedAssetIds)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = requesterId + "|" + assignmentId + "|complete|" + authoritativeVanillaBalance + "|" + string.Join(",", arrivedAssetIds.OrderBy(x => x)); var previous = Known(commandId, fingerprint); if (previous != null) return state.Assignments.Single(x => x.AssignmentId == previous.AssignmentId);
            var assignment = state.Assignments.Single(x => x.AssignmentId == assignmentId); if (assignment.State != MissionAssignmentState.Active && assignment.State != MissionAssignmentState.CompletionPending) throw new InvalidOperationException("Assignment is not active.");
            if (!ControlsOperator(state.Economy.Players.Single(x => x.PlayerId == requesterId), assignment.Operator)) throw new InvalidOperationException("Operator permission is required.");
            if (!assignment.AssetIds.OrderBy(x => x).SequenceEqual(arrivedAssetIds.Distinct().OrderBy(x => x))) throw new InvalidOperationException("Partial arrival cannot complete the assignment.");
            var guids = assignment.AssetIds.Select(id => state.Assets.Assets.Single(x => x.AssetId == id).GameLink.Value!).ToArray(); var observed = completion.Inspect(assignment.MissionId, guids);
            if (observed != WorldOwnershipOutcome.Applied) { assignment.State = MissionAssignmentState.CompletionPending; assignment.Version++; assignment.ResultCode = observed == WorldOwnershipOutcome.Unknown ? "destination-pending" : "destination-blocked"; Record(commandId, fingerprint, assignmentId, assignment.ResultCode); return assignment; }
            if (authoritativeVanillaBalance < assignment.VanillaBalanceBefore) throw new InvalidOperationException("Observed mission balance decreased.");
            var revenue = authoritativeVanillaBalance - assignment.VanillaBalanceBefore; if (revenue > assignment.MaximumExpectedRevenue) throw new InvalidOperationException("Observed mission revenue exceeds the authorized maximum.");
            assignment.VanillaBalanceAfter = authoritativeVanillaBalance; assignment.ActualRevenue = revenue;
            AccountRef beneficiary;
            if (assignment.Operator.Kind == AssetOwnerKind.Player)
            {
                beneficiary = AccountRef.Player(assignment.Operator.OwnerId); var wallet = state.Economy.Wallets.Single(x => x.Account.Key == beneficiary.Key); wallet.Balance = authoritativeVanillaBalance; wallet.Version++; assignment.ExternalSettlement = ExternalSettlementState.NotRequired; assignment.ExpectedVanillaBalance = authoritativeVanillaBalance;
            }
            else
            {
                beneficiary = AccountRef.Company(assignment.Operator.OwnerId); var wallet = state.Economy.Wallets.Single(x => x.Account.Key == beneficiary.Key); wallet.Balance = checked(wallet.Balance + revenue); wallet.Version++; assignment.ExternalSettlement = revenue == 0 ? ExternalSettlementState.NotRequired : ExternalSettlementState.Pending; assignment.ExpectedVanillaBalance = checked(authoritativeVanillaBalance - revenue);
            }
            if (revenue > 0 && !state.Economy.Ledger.Any(x => x.EntryId == assignment.AssignmentId + ":mission-revenue")) state.Economy.Ledger.Add(new LedgerEntry { EntryId = assignment.AssignmentId + ":mission-revenue", CommandId = commandId, Kind = LedgerEntryKind.MissionRevenue, Credit = beneficiary, Amount = revenue, Detail = "mission=" + assignment.MissionId + ";kind=" + assignment.Kind + ";operator=" + assignment.Operator.Key });
            assignment.State = MissionAssignmentState.Completed; assignment.Version++; assignment.ResultCode = "assignment-completed"; foreach (var id in assignment.AssetIds) { var f = state.Fleet.Single(x => x.AssetId == id); f.OperationalState = FleetOperationalState.Available; f.Version++; }
            Record(commandId, fingerprint, assignmentId, assignment.ResultCode); return assignment;
        }
    }

    public MissionAssignment MarkExternalSettlement(string assignmentId, long observedVanillaBalance)
    {
        lock (gate)
        {
            RequireHost(); var assignment = state.Assignments.Single(x => x.AssignmentId == assignmentId); if (assignment.ExternalSettlement != ExternalSettlementState.Pending) return assignment;
            if (observedVanillaBalance != assignment.ExpectedVanillaBalance) { assignment.ExternalSettlement = ExternalSettlementState.Conflict; assignment.ResultCode = "mission-settlement-conflict"; }
            else { assignment.ExternalSettlement = ExternalSettlementState.Applied; assignment.ResultCode = "mission-settlement-applied"; }
            assignment.Version++; return assignment;
        }
    }

    public MissionAssignment Cancel(string commandId, string requesterId, string assignmentId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = requesterId + "|" + assignmentId + "|cancel"; var previous = Known(commandId, fingerprint); if (previous != null) return state.Assignments.Single(x => x.AssignmentId == previous.AssignmentId);
            var assignment = state.Assignments.Single(x => x.AssignmentId == assignmentId); if (assignment.State == MissionAssignmentState.Completed || assignment.State == MissionAssignmentState.Cancelled) throw new InvalidOperationException("Assignment is already terminal.");
            if (!ControlsOperator(state.Economy.Players.Single(x => x.PlayerId == requesterId), assignment.Operator)) throw new InvalidOperationException("Operator permission is required.");
            assignment.State = MissionAssignmentState.Cancelled; assignment.Version++; assignment.ResultCode = "assignment-cancelled"; foreach (var id in assignment.AssetIds) { var f = state.Fleet.Single(x => x.AssetId == id); f.OperationalState = FleetOperationalState.Available; f.Version++; }
            Record(commandId, fingerprint, assignmentId, assignment.ResultCode); return assignment;
        }
    }

    private bool ControlsOperator(PlayerEconomicState player, AssetOwnerRef op)
    {
        if (op.Kind == AssetOwnerKind.Player) return op.OwnerId == player.PlayerId;
        if (op.Kind != AssetOwnerKind.Company || player.CompanyId != op.OwnerId) return false; var company = state.Economy.Companies.SingleOrDefault(x => x.CompanyId == op.OwnerId);
        return company != null && (company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) && rights.Contains(CompanyPermission.ManageFleet)));
    }
    private MissionAssignmentCommand? Known(string id, string fingerprint) { var r = state.AssignmentCommands.SingleOrDefault(x => x.CommandId == id); if (r != null && r.Fingerprint != fingerprint) throw new InvalidOperationException("Assignment command ID payload conflict."); return r; }
    private void Record(string id, string fingerprint, string assignment, string result) { state.AssignmentCommands.Add(new MissionAssignmentCommand { CommandId = id, Fingerprint = fingerprint, AssignmentId = assignment, ResultCode = result }); }
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
    private static AssetOwnerRef Clone(AssetOwnerRef x) => new AssetOwnerRef { Kind = x.Kind, OwnerId = x.OwnerId };
}

public static class MissionAssignmentValidation
{
    public static void Validate(VehicleAcquisitionSnapshot state)
    {
        if (state.Assignments.GroupBy(x => x.AssignmentId).Any(x => x.Count() != 1) || state.AssignmentCommands.GroupBy(x => x.CommandId).Any(x => x.Count() != 1)) throw new InvalidOperationException("Duplicate mission assignment identity.");
        foreach (var a in state.Assignments) if (string.IsNullOrWhiteSpace(a.AssignmentId) || string.IsNullOrWhiteSpace(a.MissionId) || string.IsNullOrWhiteSpace(a.RequestedBy) || a.Operator == null || a.Operator.Kind == AssetOwnerKind.Merchant || a.AssetIds.Count == 0 || a.AssetIds.Any(id => !state.Fleet.Any(f => f.AssetId == id)) || a.MaximumExpectedRevenue < 0 || a.VanillaBalanceBefore < 0 || a.VanillaBalanceAfter < 0 || a.ActualRevenue < 0 || a.ExpectedVanillaBalance < 0) throw new InvalidOperationException("Invalid mission assignment.");
    }
}
