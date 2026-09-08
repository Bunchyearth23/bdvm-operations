using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum TriageAssistanceLevel { PlanningOnly, LogisticsCommand, AutonomousDriving }
public enum TriagePlanState { Planned, ExecutionPending, Completed, Cancelled, Rejected }

public interface ITriageLogisticsPort
{
    bool Available { get; }
    AssetReleaseInspection InspectTrack(string trackId);
    WorldOwnershipOutcome Apply(string operationId, IReadOnlyList<string> persistentCarGuids, IReadOnlyList<string> orderedTrackIds);
    WorldOwnershipOutcome Inspect(string operationId, IReadOnlyList<string> persistentCarGuids, IReadOnlyList<string> orderedTrackIds);
}

public sealed class DisabledSelfShuntTriagePort : ITriageLogisticsPort
{
    public bool Available => false;
    public AssetReleaseInspection InspectTrack(string trackId) => new AssetReleaseInspection { Status = AssetReleaseStatus.Unknown, Detail = "selfshunt-logistics-adapter-disabled-until-public-hook-is-proven" };
    public WorldOwnershipOutcome Apply(string operationId, IReadOnlyList<string> persistentCarGuids, IReadOnlyList<string> orderedTrackIds) => WorldOwnershipOutcome.NotApplied;
    public WorldOwnershipOutcome Inspect(string operationId, IReadOnlyList<string> persistentCarGuids, IReadOnlyList<string> orderedTrackIds) => WorldOwnershipOutcome.Unknown;
}

[DataContract]
public sealed class TriagePlan
{
    [DataMember(Name = "planId", Order = 1)] public string PlanId { get; set; } = "";
    [DataMember(Name = "assignmentId", Order = 2)] public string AssignmentId { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 3)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "level", Order = 4)] public TriageAssistanceLevel Level { get; set; }
    [DataMember(Name = "assetIds", Order = 5)] public List<string> AssetIds { get; set; } = new List<string>();
    [DataMember(Name = "orderedTrackIds", Order = 6)] public List<string> OrderedTrackIds { get; set; } = new List<string>();
    [DataMember(Name = "state", Order = 7)] public TriagePlanState State { get; set; }
    [DataMember(Name = "resultCode", Order = 8)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "operationId", Order = 9)] public string? OperationId { get; set; }
    [DataMember(Name = "version", Order = 10)] public long Version { get; set; }
}

[DataContract]
public sealed class TriageAssistanceState
{
    [DataMember(Name = "plans", Order = 1)] public List<TriagePlan> Plans { get; set; } = new List<TriagePlan>();
    [DataMember(Name = "commands", Order = 2)] public List<MissionAssignmentCommand> Commands { get; set; } = new List<MissionAssignmentCommand>();
}

public sealed class TriageAssistanceEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly ITriageLogisticsPort logistics;
    public TriageAssistanceEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, ITriageLogisticsPort logistics) { this.state = state ?? throw new ArgumentNullException(nameof(state)); this.authority = authority ?? throw new ArgumentNullException(nameof(authority)); this.logistics = logistics ?? throw new ArgumentNullException(nameof(logistics)); VehicleAcquisitionPersistence.Validate(state); }

    public TriagePlan CreatePlan(string commandId, string requesterId, string planId, string assignmentId, TriageAssistanceLevel level, IReadOnlyList<string> orderedTrackIds)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = string.Join("|", requesterId, planId, assignmentId, level, string.Join(",", orderedTrackIds ?? Array.Empty<string>())); var replay = Known(commandId, fingerprint); if (replay != null) return state.TriageAssistance.Plans.Single(x => x.PlanId == replay.AssignmentId);
            if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(assignmentId) || orderedTrackIds == null || orderedTrackIds.Count == 0 || orderedTrackIds.Any(string.IsNullOrWhiteSpace) || orderedTrackIds.Distinct(StringComparer.Ordinal).Count() != orderedTrackIds.Count || state.TriageAssistance.Plans.Any(x => x.PlanId == planId)) throw new ArgumentException("A unique plan, assignment and ordered tracks are required.");
            var assignment = state.Assignments.Single(x => x.AssignmentId == assignmentId); if (assignment.State != MissionAssignmentState.Active && assignment.State != MissionAssignmentState.Reserved) throw new InvalidOperationException("Triage planning requires an existing active assignment.");
            if (level == TriageAssistanceLevel.AutonomousDriving) return RejectPlan(commandId, fingerprint, requesterId, planId, assignmentId, "autonomous-driving-forbidden");
            EnsureOperator(requesterId, assignment); EnsureCompleteConsist(assignment.AssetIds);
            if (assignment.AssetIds.Any(id => state.Ownership.Single(x => x.AssetId == id).Owner.Kind == AssetOwnerKind.Merchant)) throw new InvalidOperationException("AI traffic or merchant rolling stock cannot become an exploitable triage consist.");
            var plan = new TriagePlan { PlanId = planId, AssignmentId = assignmentId, RequesterId = requesterId, Level = level, AssetIds = assignment.AssetIds.OrderBy(x => x, StringComparer.Ordinal).ToList(), OrderedTrackIds = orderedTrackIds.ToList(), State = TriagePlanState.Planned, ResultCode = "triage-plan-created", Version = 1 }; state.TriageAssistance.Plans.Add(plan); Record(commandId, fingerprint, planId, "triage-plan-created"); return plan;
        }
    }

    public TriagePlan Execute(string commandId, string requesterId, string planId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = $"execute|{requesterId}|{planId}"; var known = Known(commandId, fingerprint); if (known != null) return state.TriageAssistance.Plans.Single(x => x.PlanId == planId); var plan = Plan(planId);
            if (plan.State != TriagePlanState.Planned || plan.Level != TriageAssistanceLevel.LogisticsCommand) return Reject(plan, commandId, fingerprint, "triage-logistics-command-refused");
            var assignment = state.Assignments.Single(x => x.AssignmentId == plan.AssignmentId); EnsureOperator(requesterId, assignment); EnsureCompleteConsist(plan.AssetIds); if (!logistics.Available) return Reject(plan, commandId, fingerprint, "selfshunt-logistics-adapter-unavailable");
            foreach (var track in plan.OrderedTrackIds) { var inspection = logistics.InspectTrack(track); if (inspection.Status != AssetReleaseStatus.Releasable) return Reject(plan, commandId, fingerprint, inspection.Status == AssetReleaseStatus.Blocked ? "triage-track-blocked" : "triage-track-state-unknown"); }
            plan.OperationId = commandId + ":logistics"; var outcome = logistics.Apply(plan.OperationId, Links(plan.AssetIds), plan.OrderedTrackIds); if (outcome == WorldOwnershipOutcome.NotApplied) return Reject(plan, commandId, fingerprint, "triage-logistics-not-applied"); if (outcome == WorldOwnershipOutcome.Unknown) { plan.State = TriagePlanState.ExecutionPending; plan.ResultCode = "triage-logistics-pending"; plan.Version++; Record(commandId, fingerprint, planId, plan.ResultCode); return plan; }
            plan.State = TriagePlanState.Completed; plan.ResultCode = "triage-logistics-completed"; plan.Version++; Record(commandId, fingerprint, planId, plan.ResultCode); return plan;
        }
    }

    public TriagePlan Reconcile(string commandId, string planId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = $"reconcile|{planId}"; var known = Known(commandId, fingerprint); if (known != null) return Plan(planId); var plan = Plan(planId); if (plan.State != TriagePlanState.ExecutionPending || string.IsNullOrWhiteSpace(plan.OperationId)) return Reject(plan, commandId, fingerprint, "triage-plan-not-pending"); var outcome = logistics.Inspect(plan.OperationId!, Links(plan.AssetIds), plan.OrderedTrackIds); if (outcome == WorldOwnershipOutcome.Unknown) { Record(commandId, fingerprint, planId, "triage-logistics-still-pending"); return plan; } plan.State = outcome == WorldOwnershipOutcome.Applied ? TriagePlanState.Completed : TriagePlanState.Rejected; plan.ResultCode = outcome == WorldOwnershipOutcome.Applied ? "triage-logistics-completed" : "triage-logistics-not-applied"; plan.Version++; Record(commandId, fingerprint, planId, plan.ResultCode); return plan;
        }
    }

    public TriagePlan Cancel(string commandId, string requesterId, string planId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = $"cancel|{requesterId}|{planId}"; var known = Known(commandId, fingerprint); if (known != null) return Plan(planId); var plan = Plan(planId); if (plan.State == TriagePlanState.Completed || plan.State == TriagePlanState.ExecutionPending) return Reject(plan, commandId, fingerprint, "triage-plan-cannot-cancel-after-execution"); var assignment = state.Assignments.Single(x => x.AssignmentId == plan.AssignmentId); EnsureOperator(requesterId, assignment); plan.State = TriagePlanState.Cancelled; plan.ResultCode = "triage-plan-cancelled"; plan.Version++; Record(commandId, fingerprint, planId, plan.ResultCode); return plan;
        }
    }

    private void EnsureOperator(string requesterId, MissionAssignment assignment) { var player = state.Economy.Players.SingleOrDefault(x => x.PlayerId == requesterId) ?? throw new InvalidOperationException("Unknown requester."); if (assignment.Operator.Kind == AssetOwnerKind.Player) { if (assignment.Operator.OwnerId != requesterId) throw new InvalidOperationException("Assignment operator is required."); return; } var company = state.Economy.Companies.Single(x => x.CompanyId == assignment.Operator.OwnerId); if (player.CompanyId != company.CompanyId || (company.LeaderId != requesterId && !(company.DelegatedPermissions.TryGetValue(requesterId, out var rights) && rights.Contains(CompanyPermission.ManageFleet)))) throw new InvalidOperationException("Company fleet permission is required."); }
    private void EnsureCompleteConsist(IReadOnlyList<string> ids) { if (ids == null || ids.Count == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Any(id => !state.Fleet.Any(x => x.AssetId == id))) throw new InvalidOperationException("The assigned consist is incomplete."); foreach (var bundle in state.Assets.Bundles.Where(x => x.ComponentAssetIds.Any(ids.Contains))) if (bundle.ComponentAssetIds.Any(x => !ids.Contains(x))) throw new InvalidOperationException("The assigned consist omits a bundle component."); }
    private IReadOnlyList<string> Links(IEnumerable<string> ids) => ids.Select(id => state.Assets.Assets.Single(x => x.AssetId == id).GameLink.Value ?? throw new InvalidOperationException("A triage asset lacks a persistent world link.")).ToArray();
    private TriagePlan RejectPlan(string commandId, string fingerprint, string requesterId, string planId, string assignmentId, string code) { var plan = new TriagePlan { PlanId = planId, AssignmentId = assignmentId, RequesterId = requesterId, Level = TriageAssistanceLevel.AutonomousDriving, State = TriagePlanState.Rejected, ResultCode = code, Version = 1 }; state.TriageAssistance.Plans.Add(plan); Record(commandId, fingerprint, planId, code); return plan; }
    private TriagePlan Reject(TriagePlan plan, string commandId, string fingerprint, string code) { plan.State = TriagePlanState.Rejected; plan.ResultCode = code; plan.Version++; Record(commandId, fingerprint, plan.PlanId, code); return plan; }
    private TriagePlan Plan(string id) => state.TriageAssistance.Plans.Single(x => x.PlanId == id);
    private MissionAssignmentCommand? Known(string id, string fingerprint) { var known = state.TriageAssistance.Commands.SingleOrDefault(x => x.CommandId == id); if (known != null && known.Fingerprint != fingerprint) throw new InvalidOperationException("Triage command ID payload conflict."); return known; }
    private void Record(string id, string fingerprint, string planId, string code) => state.TriageAssistance.Commands.Add(new MissionAssignmentCommand { CommandId = id, Fingerprint = fingerprint, AssignmentId = planId, ResultCode = code });
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
}

public static class TriageAssistanceValidation
{
    public static void Validate(TriageAssistanceState triage, VehicleAcquisitionSnapshot state)
    {
        if (triage == null || triage.Plans.GroupBy(x => x.PlanId).Any(x => x.Count() != 1) || triage.Commands.GroupBy(x => x.CommandId).Any(x => x.Count() != 1)) throw new InvalidOperationException("Invalid or duplicate triage assistance state.");
        foreach (var plan in triage.Plans) if (string.IsNullOrWhiteSpace(plan.PlanId) || string.IsNullOrWhiteSpace(plan.AssignmentId) || plan.AssetIds == null || plan.OrderedTrackIds == null || !state.Assignments.Any(x => x.AssignmentId == plan.AssignmentId) || plan.AssetIds.Any(id => !state.Fleet.Any(x => x.AssetId == id))) throw new InvalidOperationException("Invalid triage plan.");
    }
}
