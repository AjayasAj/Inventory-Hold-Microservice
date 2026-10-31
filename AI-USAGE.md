# AI Usage

## 1. AI strategy

### Tools and roles

| Phase                  | Tool                                               | Role                                                                                                                          |
| ---------------------- | -------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| First implementation   | GitHub Copilot (Visual Studio, chat + inline)      | Scaffolding the five-project solution, MongoDB/Redis/RabbitMQ adapters, Compose file, first NUnit suite, first React UI       |
| Review and remediation | Claude Code (CLI agent with file and shell access) | Grading the repo against the assignment, then a structured rework of the weak areas, with build and test runs after each step |

I used them differently on purpose. Copilot was fast at generating code for a well-bounded task, but it only saw the file or prompt in front of it. For the second pass I wanted a tool that could read the **whole** repository plus the assignment text at once and reason about cross-cutting issues: atomicity across documents, cache races, failure modes. That is where the first pass was weakest.

### Managing context

- **The assignment as the spec.** In both phases I pasted the full assignment brief rather than paraphrasing it. The required folder layout (`Contracts / Domain/Services / Domain/Repositories / Infrastructure / WebApi / UnitTests`) was given verbatim, so generated code landed in the right project from the start.
- **Bounded prompts in phase 1.** One concern per prompt (e.g. "MongoDB atomic stock decrement against `IInventoryRepository`"), always naming the existing interface to implement, so Copilot extended the design instead of inventing parallel abstractions.
- **Review before rewrite in phase 2.** I first asked Claude Code to _grade_ the repo out of 100 against the brief and list concrete defects with file references. Only then did I ask it to fix them. This gave an explicit, reviewable list of problems instead of an open-ended "improve this", and every change below traces back to a finding in that review.
- **Constraints stated up front:** keep the required layering, keep the domain free of infrastructure types, tests must mock repository/cache/messaging, and nothing gets claimed as verified unless it was actually run.

## 2. Human audit — accepted, rejected, corrected

### Accepted

- **Conditional `$inc` for reservations** (`AvailableQuantity >= q` in the filter). This is the correct primitive for "check and decrement" with no read-modify-write gap. Kept from phase 1 and turned into `findOneAndUpdate` so the reserved product's name comes back in the same round trip.
- **Compare-and-set status transitions** (`updateOne({_id, Status: "Active"}, …)`). Kept, because they guarantee stock is restored only once when release, read-time expiry and the sweep race.
- **Version-keyed cache invalidation.** Proposed in phase 2 to close the "reader writes stale data back after invalidation" race. I accepted it over plain `DEL` because the cost is one extra `GET` per read, and it makes the 30 s TTL a safety net instead of the thing correctness depends on.
- **TanStack Query for frontend state.** Accepted because the app's state is almost entirely server state. Invalidating `inventory` and `holds` in one hook is exactly the "stay in sync after mutations" requirement.

### Rejected or corrected

1. **Hand-written compensation instead of transactions (phase 1 output, replaced).** The first `HoldService` reserved items one by one and, on failure, looped back with `IncrementAsync` inside an empty `catch {}`. That leaks stock on a crash mid-request, and silently on a failed compensation. Two options came up in review: (a) embed the outbox in the hold document to get single-document atomicity, or (b) run MongoDB as a single-node replica set and use real multi-document transactions. **I chose (b):** it removes all compensation code, makes multi-item holds all-or-nothing, and lets the outbox be a normal collection. The cost is a replica-set init in `docker-compose.yml`, which is a small, well-understood price.
2. **Catch-all exception mapping (phase 1 output, rejected).** The controller mapped _every_ `InvalidOperationException` to 409, which is also what the Mongo and Rabbit drivers throw. An infrastructure fault would have reached clients as "Conflict". Replaced with typed `DomainException`s and one `IExceptionHandler`; everything else is a generic 500.
3. **Per-message channel and topology declaration in the RabbitMQ publisher (phase 1 output, rejected).** It opened a channel and re-declared the exchange, queue and binding for every event, without persistent delivery or publisher confirms. So "published" did not mean "stored by the broker". Replaced with one long-lived confirming channel and persistent messages; the outbox is marked published only after the confirm.
4. **"Revert the hold to Active if restocking fails" (phase 1 output, rejected).** Once an event is queued, flipping state back produces an event stream that contradicts the data. With transactions the whole release either commits or doesn't, so the revert path is unnecessary.
5. **Lazy-only expiry (gap found in review).** Expiring only on read meant a hold nobody looked at kept its stock forever (the UI's polling was hiding this). Added `HoldExpirationWorker`, reusing the same transition, so lazy and proactive expiry can't double-restock.
6. **`"latest"` for every npm dependency, and a committed `tsconfig.tsbuildinfo` (phase 1 output, rejected).** Builds weren't reproducible. Pinned exact versions; build artifacts are git-ignored.
7. **Leftover AI text in README (phase 1 output, removed).** The README ended with an instruction clearly aimed at the author ("Do not claim tests or Docker startup passed until you run them") and called itself a "starter implementation".
8. **Running the Docker verification for me (phase 2, declined).** During the rework the agent started `docker compose up --build` itself. I stopped it and kept end-to-end verification as my own step (section 3), so the person submitting is the one confirming the stack really starts.

## 3. Verification

### How tests were produced

Test _scenarios_ came from the review findings, not from "generate tests for this class". Each defect above has a test that would have failed against the phase 1 code:

- partial-failure create: asserts **no** compensating restock calls and no outbox event (the transaction does the rollback);
- lost transition race: asserts the winner's state is returned and stock is not restored twice;
- overdue release: asserts 410 **and** that the expiry was committed (stock returned, `HoldExpired` queued) first;
- broker failure in the relay: asserts it stops at the first failure so events can't be reordered;
- every domain exception → expected HTTP status and `code`.

Mocks are `MockBehavior.Strict` for the repositories and outbox, so an unexpected repository call fails the test instead of passing silently. The two concurrency tests deliberately do **not** use mocks: they run the real `HoldService` against thread-safe in-memory fakes that honour the repository contract, with `Task.Yield()` to force interleaving (50 buyers for 10 units; 20 simultaneous releases).

### How generated code was checked

- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test` — **49/49 passing**. The API Dockerfile also runs the tests, so a failing test blocks the image.
- `npm run build` — `tsc -b` in strict mode, then the Vite build, both clean.
- Reading every generated change before accepting it, especially the transaction plumbing (ambient session via `AsyncLocal`, joining an outer unit of work instead of nesting) and the cache-version logic, where a subtle mistake would not show up in unit tests.
- End-to-end with `docker-compose up --build` (`/health`, the README smoke-test commands, events visible in the RabbitMQ queue, a hold with `HOLD_DURATION_MINUTES=0.5` expiring and returning stock in the UI): run by me as the final step.
