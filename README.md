# BDVM - Operations

`BDVM.Operations` coordinates the work performed with the fleet: freight and passenger assignments, industrial production contracts, operating costs and manual shunting assistance.

## Status

| Property | Value |
| --- | --- |
| Module kind | Operations feature |
| Target framework | .NET Framework 4.8 (`net48`) |
| Required modules | `BDVM.Common`, `BDVM.Companies`, `BDVM.Fleet` |
| Standalone install | Not yet |
| Current runtime host | `BDVM.Full` |

## Responsibilities

- Reserve, activate, complete or cancel freight and passenger mission assignments.
- Keep mission completion behind a port so a game adapter can validate the real-world result.
- Model persistent station inventories, bounded production backlogs, preparation reservations and versioned transport contracts.
- Require compatible rolling stock owned or actively leased by the operator; contracts never supply or replace wagons.
- Reconcile partial loading and unloading through per-wagon manifests and idempotency keys, then pay cumulative recognized delivery exactly once.
- Prevent competing generators from producing contradictory jobs or free rolling stock when an integration can control them.
- Record fuel, maintenance, access and other operating costs against the correct personal or company account.
- Track external settlement conflicts instead of silently charging twice.
- Offer planning and logistics-command assistance for shunting while leaving physical driving to players.

## Key surfaces

The main services are `MissionAssignmentEngine`, `IndustrialEconomyEngine`, `OperatingCostEngine` and `TriageAssistanceEngine`. Integration points include `IMissionCompletionPort`, `IIndustrialExecutionPort`, `ICargoTransferObservationPort`, `IWagonCompatibilityPort`, `ITransportGeneratorAdapter` and `ITriageLogisticsPort`.

`TransportContract` schema 2 contains cargo, quantity, origin, destination, deadline, reward and wagon requirements. `AssignedWagons` records an operator choice after acceptance; it is not supplied equipment. Acceptance reserves source cargo and destination capacity for a bounded preparation window. Expiry or cancellation releases those reservations idempotently. Loading removes only observed cargo from source stock; unloading credits destination stock and reward only for the observed delta.

## Boundaries

Operations does not drive locomotives. Although the domain enum reserves an `AutonomousDriving` value for compatibility and explicit rejection, product policy limits assistance to planning and logistics commands. The default SelfShunt-related port is disabled. Operations also does not own wallets, vehicles or market prices.

## Dependencies and composition

The project references Common, Companies and Fleet. Passenger demand is supplied by `BDVM.Passengers`; Operations does not depend on it. `Domain/` is currently linked into `BDVM.Full`, so the standalone project compiles only the module marker during migration.

External dependencies: none. SelfShunt, Passenger Jobs and future industrial integrations must be supplied by separate bridges. The disabled default ports prevent an absent adapter from being mistaken for successful execution.

## Build

With dependency repositories placed beside this one under `src/`:

```powershell
dotnet build .\BDVM.Operations.csproj -c Release
```

Build `BDVM.Full` to compile the domain into the current game runtime.

## Testing and installation

Domain validation covers assignment lifecycles, industrial stock conservation, generator gates, cost settlement and refusal of autonomous driving. A standalone Unity Mod Manager package is not published yet; use the matching `BDVM.Full` composition.

## Compatibility

Job and contract IDs are stable persisted references. Completion, cancellation and external settlement must be idempotent. Missing generator-control integrations fail closed where duplicate world generation would damage the economy. Strict activation inventories open jobs before and after suppressing new generation, rolls back already-suspended generators if any required control fails or removes a migrated job, and reports the preserved IDs. The Full vanilla adapter leaves those jobs registered in `JobsManager`, while packaging audits that the vanilla `AbandonJob(Job)` surface still exists.

The audited runtime surfaces and remaining Unity validations are listed in [INTEGRATION_SURFACES.md](INTEGRATION_SURFACES.md).

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and the applied copyright [NOTICE](NOTICE).
