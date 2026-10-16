# Local PostgreSQL Afterthought probe

Synthetic scenarios use real application integration/signal services. Exits nonzero on failure. Set WOVEN_QA_APPLICATION_SHA to the tested full commit. Set WOVEN_QA_CASES=consolidated and WOVEN_QA_EXTENDED=1 for the current 54-case review. This excludes two historical transaction-specific diagnostics. Ordinary storage uses a five-entity projection (matches, afterthoughts, signals, users, blocks); extended safety checks invoke real TrustService with an audit stub. Endpoint checks invoke handlers directly with synthetic identity, not HTTP authentication middleware. Workers/learners are not started.

Use a separate disposable PostgreSQL 16 database/user named woven_qa_atomic_0f5dc4b / woven_qa on 127.0.0.1:55439. Program refuses other targets and checks effective SQL identity before EnsureCreated. Never use an application database. Set WOVEN_QA_ATOMIC_CONNECTION locally; do not commit credentials. Optional WOVEN_QA_ATOMIC_REPORT selects local JSON output.

```powershell
dotnet run --project qa/probes/afterthought-postgres/AfterthoughtPostgres.csproj --verbosity quiet
```

Unique actors permit reruns without truncation. The prefix case uses a deliberately distant fabricated metadata ID to avoid colliding with later scenarios. The legacy SKIP LOCKED name now tests atomic reclaim using gated overlapping scopes. Some original controls disable retries; extended cases enable them. Set WovenBackendProject as an MSBuild property to test a separate pinned application checkout.

The extended matrix also creates a separate disposable database named woven_qa_migrations_consolidated_20261016 after verifying the loopback connection/user, then runs the full real migration chain. It never resets or drops a database. A repeat resumes the previously partial migration chain; the evidence identifies the fresh first attempt. Do not use an application database or production settings.
