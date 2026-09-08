# W-038 integration surfaces

This inventory separates proven source surfaces from work that still requires a live Derail Valley session. It does not grant domain authority to any generator.

| Provider | Generation and consist surface | Loading, unloading and payment surface | BDVM boundary |
| --- | --- | --- | --- |
| Vanilla | `StationProceduralJobsController.TryToGenerateJobs`, intercepted before generation by the Full population adapter | `WarehouseMachineController` observations and the authoritative vanilla wallet delta require a Unity adapter | New generation must be suppressed in strict mode; existing jobs remain cancellable |
| SelfShunt fork | `SS/SelfShunt.cs` patches `TryToGenerateJobs`; `SelfShunt.API/IntegrationContracts.cs` exposes host-only generation and natural-population controls | `SS/JobMechanics.cs` observes `StartLoadSequence` and registers generated jobs; the API reserves integration event kinds for external jobs and loading | The bridge may control and observe SelfShunt, never delegate BDVM stock, reservation or payment authority |
| PassengerJobs fork | `PassengerJobs/Generation/PassengerJobGenerator.cs` owns automatic generation and consist spawn; API 1.1 exposes host-only suspension before consist creation | Platform state machines expose transfer observations; API lifecycle observations expose completed/abandoned state and observed payout | Passenger services remain separate from industrial stock; their consist generator must be controlled when strict population requires it |

## Stable domain ports

- `ITransportGeneratorAdapter` suppresses only new consists and exposes existing open job IDs. `IndustrialRuntimeGate.TryEnableStrict` is transactional and rolls back prior controls on failure.
- `IWagonCompatibilityPort` resolves the real cargo capacity of an owned or actively leased freight wagon.
- `ICargoTransferObservationPort` confirms cumulative loading and unloading before stock or payment mutation.
- `IIndustrialExecutionPort` remains as the legacy W-027 aggregate-delivery compatibility path.

## Runtime validation still required

- Bind vanilla warehouse observations to persistent `AssetId` values and prove partial transfers and retry behavior.
- Prove all required generator controls together on solo host and Multiplayer host/client, including preservation and cancellation of jobs opened before activation.
- Prove lease expiry, destroyed or temporarily missing rolling stock, save/reload and payout reconciliation against live objects.
- Confirm zero free freight wagons and multi-contract reuse of purchased or leased wagons on a new non-tutorial BDVM career.

No game was launched while producing this inventory or the offline test suite.
