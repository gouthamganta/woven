# Photo Learning protocol/worker review

Run `dotnet run --project qa/probes/photo-learning/PhotoLearning.csproj` from repository root. WOVEN_QA_PHOTO_REPORT optionally selects an existing local output directory/file. This program uses synthetic HTTP handlers for Replicate and Azure Blob Storage, a synthetic image marker stream, and an isolated EF InMemory projection. It sends **zero** external HTTP requests and uses no real API key, user or photograph.

Thirteen checks cover asynchronous embedding handling, immediate and polled attribute responses, classification output validation, JPEG scan preservation, moderation/container queue gating, worker failure isolation and missing-key behavior. The vector property is deliberately ignored in the memory model; vector dimensional constraints and actual PostgreSQL photo persistence are not certified. Real model compatibility, inference quality and confidence calibration remain unverified; provider response fixtures are not live inference evidence.

The no-urls immediate-response and mismatched-probability-count cases are defensive contract checks, not proof that the configured provider currently emits those shapes. Async mode is the documented default for the provider and the main embedding request does not request sync or poll.
