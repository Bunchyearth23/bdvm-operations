using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum OperatingCostState { Open, Settled, Rejected }
public enum ExternalSettlementState { NotRequired, Pending, Applied, Conflict }

[DataContract]
public sealed class OperatingCostRecord
{
    [DataMember(Name = "sessionId", Order = 1)] public string SessionId { get; set; } = "";
    [DataMember(Name = "fingerprint", Order = 2)] public string Fingerprint { get; set; } = "";
    [DataMember(Name = "requesterId", Order = 3)] public string RequesterId { get; set; } = "";
    [DataMember(Name = "assetId", Order = 4)] public string AssetId { get; set; } = "";
    [DataMember(Name = "action", Order = 5)] public MaintenanceAction Action { get; set; }
    [DataMember(Name = "payer", Order = 6)] public AccountRef Payer { get; set; } = new AccountRef();
    [DataMember(Name = "maximumAuthorizedCost", Order = 7)] public long MaximumAuthorizedCost { get; set; }
    [DataMember(Name = "vanillaBalanceBefore", Order = 8)] public long VanillaBalanceBefore { get; set; }
    [DataMember(Name = "vanillaBalanceAfter", Order = 9)] public long VanillaBalanceAfter { get; set; }
    [DataMember(Name = "actualCost", Order = 10)] public long ActualCost { get; set; }
    [DataMember(Name = "conditionBefore", Order = 11)] public decimal ConditionBefore { get; set; }
    [DataMember(Name = "conditionAfter", Order = 12)] public decimal ConditionAfter { get; set; }
    [DataMember(Name = "tripId", Order = 13)] public string? TripId { get; set; }
    [DataMember(Name = "state", Order = 14)] public OperatingCostState State { get; set; }
    [DataMember(Name = "resultCode", Order = 15)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "externalReimbursement", Order = 16)] public long ExternalReimbursement { get; set; }
    [DataMember(Name = "externalSettlement", Order = 17)] public ExternalSettlementState ExternalSettlement { get; set; }
}

public sealed class OperatingCostEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;

    public OperatingCostEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        VehicleAcquisitionPersistence.Validate(state);
    }

    public OperatingCostRecord Begin(ManualMaintenanceRequest request, long vanillaBalanceBefore, decimal conditionBefore, string? tripId = null)
    {
        lock (gate)
        {
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) throw new InvalidOperationException("Host authority is required.");
            if (!ManualMaintenancePolicy.Validate(request, out var reason)) throw new InvalidOperationException(reason);
            if (vanillaBalanceBefore < 0 || conditionBefore < 0m || conditionBefore > 1m) throw new ArgumentOutOfRangeException(nameof(vanillaBalanceBefore));
            var fingerprint = string.Join("|", request.RequesterId, request.AssetId, request.Action, request.Payer!.Key, request.MaximumAuthorizedCost, vanillaBalanceBefore, conditionBefore, tripId ?? "");
            var known = state.OperatingCosts.SingleOrDefault(x => x.SessionId == request.CommandId);
            if (known != null)
            {
                if (known.Fingerprint != fingerprint) throw new InvalidOperationException("An operating-cost session ID cannot be reused with another payload.");
                return known;
            }
            var player = state.Economy.Players.Single(x => x.PlayerId == request.RequesterId);
            var ownership = state.Ownership.Single(x => x.AssetId == request.AssetId);
            if (!CanOperate(player, ownership.Owner)) throw new InvalidOperationException("The requester cannot maintain this asset.");
            AuthorizePayer(player, ownership.Owner, request.Payer);
            var personal = state.Economy.Wallets.Single(x => x.Account.Key == AccountRef.Player(player.PlayerId).Key);
            if (personal.Balance != vanillaBalanceBefore) throw new InvalidOperationException("The personal wallet must be synchronized before opening a cost session.");
            var record = new OperatingCostRecord
            {
                SessionId = request.CommandId, Fingerprint = fingerprint, RequesterId = player.PlayerId, AssetId = request.AssetId,
                Action = request.Action, Payer = request.Payer, MaximumAuthorizedCost = request.MaximumAuthorizedCost,
                VanillaBalanceBefore = vanillaBalanceBefore, VanillaBalanceAfter = vanillaBalanceBefore,
                ConditionBefore = conditionBefore, ConditionAfter = conditionBefore, TripId = string.IsNullOrWhiteSpace(tripId) ? null : tripId,
                State = OperatingCostState.Open, ResultCode = "manual-session-open", ExternalSettlement = ExternalSettlementState.NotRequired
            };
            state.OperatingCosts.Add(record);
            return record;
        }
    }

    public OperatingCostRecord Complete(string sessionId, long vanillaBalanceAfter, decimal conditionAfter)
    {
        lock (gate)
        {
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) throw new InvalidOperationException("Host authority is required.");
            var record = state.OperatingCosts.Single(x => x.SessionId == sessionId);
            if (record.State != OperatingCostState.Open) return record;
            if (vanillaBalanceAfter < 0 || vanillaBalanceAfter > record.VanillaBalanceBefore) return Reject(record, "ambiguous-vanilla-balance-change");
            if (conditionAfter < 0m || conditionAfter > 1m) return Reject(record, "invalid-condition");
            var cost = record.VanillaBalanceBefore - vanillaBalanceAfter;
            if (cost > record.MaximumAuthorizedCost) return Reject(record, "authorized-cost-exceeded");
            var personal = state.Economy.Wallets.Single(x => x.Account.Key == AccountRef.Player(record.RequesterId).Key);
            if (personal.Balance != record.VanillaBalanceBefore) return Reject(record, "wallet-version-context-changed");
            var payer = state.Economy.Wallets.Single(x => x.Account.Key == record.Payer.Key);
            if (record.Payer.Kind == AccountKind.Company && payer.Balance < cost) return Reject(record, "company-funds-insufficient");
            if (record.Payer.Kind == AccountKind.Player)
            {
                personal.Balance = vanillaBalanceAfter; personal.Version++;
            }
            else
            {
                payer.Balance -= cost; payer.Version++;
                record.ExternalReimbursement = cost;
                record.ExternalSettlement = cost == 0 ? ExternalSettlementState.NotRequired : ExternalSettlementState.Pending;
            }
            record.VanillaBalanceAfter = vanillaBalanceAfter; record.ActualCost = cost; record.ConditionAfter = conditionAfter;
            record.State = OperatingCostState.Settled; record.ResultCode = "operating-cost-recorded";
            var entryId = record.SessionId + ":cost";
            if (!state.Economy.Ledger.Any(x => x.EntryId == entryId)) state.Economy.Ledger.Add(new LedgerEntry
            {
                EntryId = entryId, CommandId = record.SessionId, Kind = LedgerEntryKind.OperatingCost, Debit = record.Payer, Amount = cost,
                Detail = "asset=" + record.AssetId + ";action=" + record.Action + ";conditionBefore=" + record.ConditionBefore + ";conditionAfter=" + record.ConditionAfter + ";trip=" + (record.TripId ?? "none") + ";source=observed-vanilla-settlement"
            });
            return record;
        }
    }

    public OperatingCostRecord MarkExternalSettlement(string sessionId, long observedVanillaBalance)
    {
        lock (gate)
        {
            var record = state.OperatingCosts.Single(x => x.SessionId == sessionId);
            if (record.ExternalSettlement != ExternalSettlementState.Pending) return record;
            var expected = checked(record.VanillaBalanceAfter + record.ExternalReimbursement);
            record.ExternalSettlement = observedVanillaBalance == expected ? ExternalSettlementState.Applied : ExternalSettlementState.Conflict;
            record.ResultCode = record.ExternalSettlement == ExternalSettlementState.Applied ? "operating-cost-settled" : "external-wallet-conflict";
            return record;
        }
    }

    private bool CanOperate(PlayerEconomicState player, AssetOwnerRef owner)
    {
        if (owner.Kind == AssetOwnerKind.Player) return owner.OwnerId == player.PlayerId;
        if (owner.Kind != AssetOwnerKind.Company || player.CompanyId != owner.OwnerId) return false;
        var company = state.Economy.Companies.Single(x => x.CompanyId == owner.OwnerId);
        return company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) && rights.Contains(CompanyPermission.ManageFleet));
    }

    private void AuthorizePayer(PlayerEconomicState player, AssetOwnerRef owner, AccountRef payer)
    {
        if (payer.Kind == AccountKind.Player && payer.OwnerId == player.PlayerId) return;
        if (payer.Kind != AccountKind.Company || owner.Kind != AssetOwnerKind.Company || payer.OwnerId != owner.OwnerId || player.CompanyId != payer.OwnerId) throw new InvalidOperationException("The explicit payer does not match the asset operation.");
        var company = state.Economy.Companies.Single(x => x.CompanyId == payer.OwnerId);
        if (company.LeaderId != player.PlayerId && (!company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) || !rights.Contains(CompanyPermission.ManageFunds))) throw new InvalidOperationException("ManageFunds permission is required for company-paid service.");
    }

    private static OperatingCostRecord Reject(OperatingCostRecord record, string code) { record.State = OperatingCostState.Rejected; record.ResultCode = code; return record; }
}
