# Local PostgreSQL Afterthought probe

Synthetic scenarios use real application integration/signal services. Exits nonzero on failure. Minimal three-table model, not application migrations; no safety/trust or worker validation. Set WOVEN_QA_APPLICATION_SHA to the tested full application commit for result metadata. Optional WOVEN_QA_CASES selects comma-separated scenario names; omit it to run all cases. Some cases target an earlier transaction design; use the selections in the relevant evidence report when the implementation changes.

Use a separate disposable PostgreSQL 16 database/user named woven_qa_atomic_0f5dc4b / woven_qa on 127.0.0.1:55439. Program refuses other targets and checks effective SQL identity before EnsureCreated. Never use an application database. Set WOVEN_QA_ATOMIC_CONNECTION locally; do not commit credentials. Optional WOVEN_QA_ATOMIC_REPORT selects local JSON output.

```powershell
dotnet run --project qa/probes/afterthought-postgres/AfterthoughtPostgres.csproj --verbosity quiet
```

Unique synthetic actor IDs permit reruns without truncation. The prefix case seeds a distinct historical signal for the same actor pair. Configured-retry case matches Program.cs; other controls disable retries except the external-wrapper diagnostic. Injected marker-save failure follows the real signal SaveChanges, inside the real application transaction.
