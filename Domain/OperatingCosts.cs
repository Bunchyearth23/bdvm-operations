using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum OperatingCostState { Open, Settled, Rejected }
public enum ExternalSettlementState { NotRequired, Pending, Applied, Conflict }
public enum OperatingCostSettlementMode { PersonalExternalWallet, SharedExternalWallet }

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
    [DataMember(Name = "reservedAmount", Order = 18)] public long ReservedAmount { get; set; }
    [DataMember(Name = "reservationReleased", Order = 19)] public bool ReservationReleased { get; set; }
    [DataMember(Name = "settlementMode", Order = 20)] public OperatingCostSettlementMode SettlementMode { get; set; }
    [DataMember(Name = "subsidizedExcess", Order = 21)] public long SubsidizedExcess { get; set; }
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

    public OperatingCostRecord Begin(ManualMaintenanceRequest request, long vanillaBalanceBefore, decimal conditionBefore, string? tripId = null,
        OperatingCostSettlementMode settlementMode = OperatingCostSettlementMode.PersonalExternalWallet)
    {
        lock (gate)
        {
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) throw new InvalidOperationException("Host authority is required.");
            if (!ManualMaintenancePolicy.Validate(request, out var reason)) throw new InvalidOperationException(reason);
            if (vanillaBalanceBefore < 0 || conditionBefore < 0m || conditionBefore > 1m) throw new ArgumentOutOfRangeException(nameof(vanillaBalanceBefore));
            if (!Enum.IsDefined(typeof(OperatingCostSettlementMode), settlementMode)) throw new ArgumentOutOfRangeException(nameof(settlementMode));
            var fingerprint = string.Join("|", request.RequesterId, request.AssetId, request.Action, request.Payer!.Key, request.MaximumAuthorizedCost, vanillaBalanceBefore, conditionBefore, tripId ?? "", settlementMode);
            var known = state.OperatingCosts.SingleOrDefault(x => x.SessionId == request.CommandId);
            if (known != null)
            {
                if (known.Fingerprint != fingerprint) throw new InvalidOperationException("An operating-cost session ID cannot be reused with another payload.");
                return known;
            }
            if (state.OperatingCosts.Any(x => x.State == OperatingCostState.Open || x.ExternalSettlement == ExternalSettlementState.Pending || x.ExternalSettlement == ExternalSettlementState.Conflict))
                throw new InvalidOperationException("Another manual operating-cost session is unresolved; vanilla wallet deltas cannot be attributed safely.");
            var player = state.Economy.Players.Single(x => x.PlayerId == request.RequesterId);
            var ownership = state.Ownership.Single(x => x.AssetId == request.AssetId);
            var operatingOwner = ResolveOperatingOwner(request.AssetId, ownership.Owner);
            if (!CanOperate(player, operatingOwner)) throw new InvalidOperationException("The requester cannot maintain this asset.");
            AuthorizePayer(player, operatingOwner, request.Payer);
            var personal = state.Economy.Wallets.Single(x => x.Account.Key == AccountRef.Player(player.PlayerId).Key);
            if (settlementMode == OperatingCostSettlementMode.PersonalExternalWallet && personal.Balance != vanillaBalanceBefore) throw new InvalidOperationException("The personal wallet must be synchronized before opening a cost session.");
            var payer = state.Economy.Wallets.Single(x => x.Account.Key == request.Payer.Key);
            var reservation = request.Payer.Kind == AccountKind.Company || settlementMode == OperatingCostSettlementMode.SharedExternalWallet ? request.MaximumAuthorizedCost : 0;
            if (payer.Balance < reservation) throw new InvalidOperationException("The selected payer cannot reserve the maximum authorized cost.");
            if (reservation > 0) { payer.Balance -= reservation; payer.Version++; }
            var record = new OperatingCostRecord
            {
                SessionId = request.CommandId, Fingerprint = fingerprint, RequesterId = player.PlayerId, AssetId = request.AssetId,
                Action = request.Action, Payer = Clone(request.Payer), MaximumAuthorizedCost = request.MaximumAuthorizedCost,
                VanillaBalanceBefore = vanillaBalanceBefore, VanillaBalanceAfter = vanillaBalanceBefore,
                ConditionBefore = conditionBefore, ConditionAfter = conditionBefore, TripId = string.IsNullOrWhiteSpace(tripId) ? null : tripId,
                State = OperatingCostState.Open, ResultCode = "manual-session-open", ExternalSettlement = ExternalSettlementState.NotRequired,
                ReservedAmount = reservation, ReservationReleased = reservation == 0, SettlementMode = settlementMode
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
            if (vanillaBalanceAfter < 0 || vanillaBalanceAfter > record.VanillaBalanceBefore) return RejectAndRelease(record, "ambiguous-vanilla-balance-change");
            if (conditionAfter < 0m || conditionAfter > 1m) return RejectAndRelease(record, "invalid-condition", vanillaBalanceAfter, conditionAfter);
            var cost = record.VanillaBalanceBefore - vanillaBalanceAfter;
            if (cost > record.MaximumAuthorizedCost)
            {
                if (record.SettlementMode == OperatingCostSettlementMode.SharedExternalWallet)
                    return RecoverSharedAuthorizedCostExceeded(record, cost, vanillaBalanceAfter, conditionAfter);
                return RejectAndRelease(record, "authorized-cost-exceeded", vanillaBalanceAfter, conditionAfter);
            }
            var personal = state.Economy.Wallets.Single(x => x.Account.Key == AccountRef.Player(record.RequesterId).Key);
            if (record.SettlementMode == OperatingCostSettlementMode.SharedExternalWallet)
            {
                if (record.ReservedAmount < cost) return RejectAndRelease(record, "invalid-payer-reservation", vanillaBalanceAfter, conditionAfter);
                ReleaseReservation(record, cost);
                record.ExternalReimbursement = cost;
                record.ExternalSettlement = cost == 0 ? ExternalSettlementState.NotRequired : ExternalSettlementState.Pending;
            }
            else if (personal.Balance != record.VanillaBalanceBefore) return RejectAndRelease(record, "wallet-version-context-changed");
            else if (record.Payer.Kind == AccountKind.Player)
            {
                personal.Balance = vanillaBalanceAfter; personal.Version++;
            }
            else
            {
                if (record.ReservedAmount < cost) return RejectAndRelease(record, "invalid-company-reservation", vanillaBalanceAfter, conditionAfter);
                ReleaseReservation(record, cost);
                record.ExternalReimbursement = cost;
                record.ExternalSettlement = cost == 0 ? ExternalSettlementState.NotRequired : ExternalSettlementState.Pending;
            }
            record.VanillaBalanceAfter = vanillaBalanceAfter; record.ActualCost = cost; record.ConditionAfter = conditionAfter;
            record.State = OperatingCostState.Settled; record.ResultCode = "operating-cost-recorded";
            var entryId = record.SessionId + ":cost";
            if (cost > 0 && !state.Economy.Ledger.Any(x => x.EntryId == entryId)) state.Economy.Ledger.Add(new LedgerEntry
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
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) throw new InvalidOperationException("Host authority is required.");
            var record = state.OperatingCosts.Single(x => x.SessionId == sessionId);
            if (record.ExternalSettlement == ExternalSettlementState.Applied || record.ExternalSettlement == ExternalSettlementState.NotRequired) return record;
            var expected = checked(record.VanillaBalanceAfter + record.ExternalReimbursement);
            record.ExternalSettlement = observedVanillaBalance == expected ? ExternalSettlementState.Applied : ExternalSettlementState.Conflict;
            if (record.ExternalSettlement == ExternalSettlementState.Applied)
                record.ResultCode = record.State == OperatingCostState.Rejected ? "authorized-cost-exceeded-reimbursed" : "operating-cost-settled";
            else record.ResultCode = "external-wallet-conflict";
            return record;
        }
    }

    public OperatingCostRecord Cancel(string sessionId, long observedVanillaBalance)
    {
        lock (gate)
        {
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) throw new InvalidOperationException("Host authority is required.");
            var record = state.OperatingCosts.Single(x => x.SessionId == sessionId);
            if (record.State != OperatingCostState.Open) return record;
            if (observedVanillaBalance != record.VanillaBalanceBefore) throw new InvalidOperationException("A manual cost session cannot be cancelled after the vanilla wallet changed.");
            ReleaseReservation(record, 0);
            record.State = OperatingCostState.Rejected;
            record.ResultCode = "manual-session-cancelled";
            return record;
        }
    }

    private bool CanOperate(PlayerEconomicState player, AssetOwnerRef owner)
    {
        if (owner.Kind == AssetOwnerKind.Player) return owner.OwnerId == player.PlayerId;
        if (owner.Kind != AssetOwnerKind.Company || player.CompanyId != owner.OwnerId) return false;
        var company = state.Economy.Companies.Single(x => x.CompanyId == owner.OwnerId);
        return !company.Liquidating && (company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) && rights.Contains(CompanyPermission.ManageFleet)));
    }

    private void AuthorizePayer(PlayerEconomicState player, AssetOwnerRef owner, AccountRef payer)
    {
        if (payer.Kind == AccountKind.Player && payer.OwnerId == player.PlayerId) return;
        if (payer.Kind != AccountKind.Company || owner.Kind != AssetOwnerKind.Company || payer.OwnerId != owner.OwnerId || player.CompanyId != payer.OwnerId) throw new InvalidOperationException("The explicit payer does not match the asset operation.");
        var company = state.Economy.Companies.Single(x => x.CompanyId == payer.OwnerId);
        if (company.Liquidating) throw new InvalidOperationException("A liquidating company cannot authorize operating costs.");
        if (company.LeaderId != player.PlayerId && (!company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) || !rights.Contains(CompanyPermission.ManageFunds))) throw new InvalidOperationException("ManageFunds permission is required for company-paid service.");
    }

    private AssetOwnerRef ResolveOperatingOwner(string assetId, AssetOwnerRef owner)
    {
        var activeLease = state.Leases.SingleOrDefault(x => x.AssetIds.Contains(assetId) && (x.State == LeaseState.Active || x.State == LeaseState.Delinquent));
        return activeLease?.Lessee ?? owner;
    }

    private OperatingCostRecord RejectAndRelease(OperatingCostRecord record, string code, long? observedVanillaBalance = null, decimal? conditionAfter = null)
    {
        if (observedVanillaBalance.HasValue && observedVanillaBalance.Value >= 0 && observedVanillaBalance.Value <= record.VanillaBalanceBefore)
        {
            record.VanillaBalanceAfter = observedVanillaBalance.Value;
            record.ActualCost = record.VanillaBalanceBefore - observedVanillaBalance.Value;
            var personal = state.Economy.Wallets.Single(x => x.Account.Key == AccountRef.Player(record.RequesterId).Key);
            if (record.SettlementMode == OperatingCostSettlementMode.PersonalExternalWallet && personal.Balance == record.VanillaBalanceBefore) { personal.Balance = observedVanillaBalance.Value; personal.Version++; }
        }
        if (conditionAfter.HasValue && conditionAfter.Value >= 0m && conditionAfter.Value <= 1m) record.ConditionAfter = conditionAfter.Value;
        ReleaseReservation(record, 0);
        record.State = OperatingCostState.Rejected;
        record.ResultCode = code;
        return record;
    }

    private OperatingCostRecord RecoverSharedAuthorizedCostExceeded(OperatingCostRecord record, long observedCost, long vanillaBalanceAfter, decimal conditionAfter)
    {
        var chargedCost = Math.Min(record.ReservedAmount, record.MaximumAuthorizedCost);
        ReleaseReservation(record, chargedCost);
        record.VanillaBalanceAfter = vanillaBalanceAfter;
        record.ActualCost = observedCost;
        record.ConditionAfter = conditionAfter;
        record.ExternalReimbursement = observedCost;
        record.ExternalSettlement = observedCost == 0 ? ExternalSettlementState.NotRequired : ExternalSettlementState.Pending;
        record.SubsidizedExcess = observedCost - chargedCost;
        record.State = OperatingCostState.Rejected;
        record.ResultCode = "authorized-cost-exceeded";
        var entryId = record.SessionId + ":cost";
        if (chargedCost > 0 && !state.Economy.Ledger.Any(x => x.EntryId == entryId)) state.Economy.Ledger.Add(new LedgerEntry
        {
            EntryId = entryId, CommandId = record.SessionId, Kind = LedgerEntryKind.OperatingCost, Debit = record.Payer, Amount = chargedCost,
            Detail = "asset=" + record.AssetId + ";action=" + record.Action + ";externalCost=" + observedCost + ";authorizedCharge=" + chargedCost + ";subsidizedExcess=" + record.SubsidizedExcess + ";source=shared-wallet-overrun-recovery"
        });
        return record;
    }

    private void ReleaseReservation(OperatingCostRecord record, long retainedCost)
    {
        if (record.ReservationReleased) return;
        if (retainedCost < 0 || retainedCost > record.ReservedAmount) throw new InvalidOperationException("Invalid operating-cost reservation settlement.");
        var refund = record.ReservedAmount - retainedCost;
        if (refund > 0)
        {
            var payer = state.Economy.Wallets.Single(x => x.Account.Key == record.Payer.Key);
            payer.Balance = checked(payer.Balance + refund); payer.Version++;
        }
        record.ReservationReleased = true;
    }

    private static AccountRef Clone(AccountRef value) => new AccountRef { Kind = value.Kind, OwnerId = value.OwnerId };
}
