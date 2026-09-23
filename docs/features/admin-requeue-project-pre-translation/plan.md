# Admin-triggered project pre-translation re-run — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin re-run a started project through pre-translation from the content manager UI, with per-run control over forcing re-translation, skipping company-lead assignment, skipping the project-started notification, and limiting the run to specific resource contents — replacing the developer-only manual poison-queue replay.

**Architecture:** A new admin-guarded endpoint publishes the existing `TranslateProjectResourcesMessage` with new option fields. Those options flow through the existing durable-function orchestration, where they select the per-resource `TranslationOrigin` (`Project` vs `BasicTranslationOnly`), filter the resource-content fan-out, and make the two post-translation steps conditional. No new queue, no new orchestration, no schema change.

**Tech Stack:** C# / .NET 9, FastEndpoints, FluentValidation, EF Core, Azure Durable Functions, Newtonsoft (queue message serialization), xUnit + FluentAssertions. Web: SvelteKit 5 (runes), TypeScript, Tailwind, vitest.

**Spec:** [design.md](design.md)

**Repos:** `aquifer-server` (tasks 1–6) and `content-manager-web` (tasks 7–9). Branch `admin-requeue-project-pre-translation` exists in both.

## Global Constraints

- `POST /projects/{id}/start` behavior must be **unchanged**. It calls the same publisher with all-default options; any change that alters its behavior is a bug.
- New message/DTO fields must default to today's behavior so messages already on `translate-project-resources` and `translate-project-resources-poison` when this deploys keep working. Serialization is Newtonsoft with camelCase and no `MissingMemberHandling.Error`.
- `shouldForceRetranslation` may **only** be paired with `TranslationOrigin.BasicTranslationOnly` — `TranslateResourceCoreAsync` throws otherwise (`src/Aquifer.Jobs/Subscribers/TranslationMessageSubscriber.cs:681`). The force flag selects the origin; it is never passed alongside `TranslationOrigin.Project`.
- The endpoint must not create snapshots and must not modify `project.Started`. Forced re-translation reads from the *first* snapshot; adding snapshots on re-run corrupts that.
- Orchestrator functions are single-threaded and replay-based: no DB calls, no `DateTime.UtcNow`, no randomness inside `OrchestrateProjectResourcesTranslationAsync`. Filtering by resource-content ID happens in the queue subscriber, before the orchestration is scheduled.
- No happy-path integration test against the real API — it would enqueue a real translation run and spend AI budget per CI pass.

---

## Prerequisite (manual, out-of-repo, blocking for release only)

- [ ] **Auth0:** create the `requeue-pre-translation:project` permission on the API and grant it to the Admin role, in **every** environment (dev, staging, prod). Until this is done the endpoint returns 403 for everyone, including admins. Implementation and all tests in this plan can proceed without it; only manual verification and release are blocked.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Aquifer.API/Common/Permissions.cs` | Add `RequeuePreTranslationProject` constant. |
| `src/Aquifer.API/Endpoints/Admin/Projects/PreTranslate/Request.cs` | Route param + four run options. |
| `src/Aquifer.API/Endpoints/Admin/Projects/PreTranslate/Validator.cs` | Shape-level validation of the options. |
| `src/Aquifer.API/Endpoints/Admin/Projects/PreTranslate/Endpoint.cs` | Guard, DB-level validation, publish, 202. |
| `src/Aquifer.Common/Messages/Models/TranslateProjectResourcesMessage.cs` | Four new optional fields. |
| `src/Aquifer.Jobs/Subscribers/TranslationMessageSubscriber.cs` | DTO fields; subscriber filtering; origin selection; conditional post-translation steps. |
| `tests/Aquifer.Common.UnitTests/Messages/TranslateProjectResourcesMessageTests.cs` | Backward-compatible deserialization. |
| `tests/Aquifer.Jobs.UnitTests/Subscribers/ProjectPreTranslationOptionsTests.cs` | Origin selection + resource-ID filtering helpers. |
| `tests/Aquifer.API.IntegrationTests/Endpoints/Admin/Projects/PreTranslate/EndpointTests.cs` | Authorization negative cases. (The 404 case was dropped: it needs an admin client the harness can't have until Auth0 is configured.) |
| `docs/ai-translation-workflow.md` | Document the admin re-run path. |
| **content-manager-web** | |
| `src/lib/stores/auth.ts` | Add `Permission.RequeuePreTranslationProject`. |
| `src/lib/utils/projects.ts` | `requeueProjectPreTranslation(id, options)`. |
| `src/lib/utils/resource-content-ids.ts` | `parseResourceContentIds`. (Its own module, not `projects.ts`: importing that drags `$env/static/public` and the Auth0 client into the test.) |
| `src/lib/utils/resource-content-ids.test.ts` | Tests for ID parsing. |
| `src/routes/admin/projects/[projectId]/+page.ts` | Permission guard + project load. |
| `src/routes/admin/projects/[projectId]/+page.svelte` | Options form + confirmation. |
| `src/routes/projects/[projectId]/+page.svelte` | Permission-gated link to the admin page. |

---

### Task 1: Add the permission constant

**Files:** Modify `src/Aquifer.API/Common/Permissions.cs`

- [x] **Step 1:** Add to `PermissionName`, keeping the existing alphabetical-by-constant-name ordering:
  ```csharp
  RequeuePreTranslationProject = "requeue-pre-translation:project",
  ```
  It sorts between `ReadUsers` and `ReviewContent` ("Req" > "Rea", "Req" < "Rev"), so it is a plain insertion into the middle of the existing single `public const string` declaration list — no change to the first or last entry.
- [x] **Step 2:** Build. No test — this is a constant with no behavior.

---

### Task 2: Add the run options to the queue message

**Files:**
- Modify: `src/Aquifer.Common/Messages/Models/TranslateProjectResourcesMessage.cs`
- Test: `tests/Aquifer.Common.UnitTests/Messages/TranslateProjectResourcesMessageTests.cs` (new)

**Interfaces produced (used by tasks 3 and 4):**
```csharp
public sealed record TranslateProjectResourcesMessage(
    int ProjectId,
    int StartedByUserId,
    bool ShouldForceRetranslation = false,
    bool ShouldSkipCompanyLeadAssignment = false,
    bool ShouldSkipProjectStartedNotification = false,
    IReadOnlyList<int>? ResourceContentIds = null);
```

- [x] **Step 1: Write the failing test.** New file, asserting that a message serialized in the *old* two-field shape still deserializes with today's defaults, and that a full-options message round-trips:
  ```csharp
  [Fact]
  public void Deserialize_WhenJsonHasOnlyTheOriginalFields_UsesDefaultsForTheNewOptions()
  {
      const string json = """{"projectId": 42, "startedByUserId": 7}""";

      var message = MessagesJsonSerializer.Deserialize<TranslateProjectResourcesMessage>(json)!;

      message.ProjectId.Should().Be(42);
      message.StartedByUserId.Should().Be(7);
      message.ShouldForceRetranslation.Should().BeFalse();
      message.ShouldSkipCompanyLeadAssignment.Should().BeFalse();
      message.ShouldSkipProjectStartedNotification.Should().BeFalse();
      message.ResourceContentIds.Should().BeNull();
  }

  [Fact]
  public void SerializeThenDeserialize_WithAllOptionsSet_RoundTrips()
  {
      var original = new TranslateProjectResourcesMessage(42, 7, true, true, true, [1, 2, 3]);

      var result = MessagesJsonSerializer.Deserialize<TranslateProjectResourcesMessage>(
          MessagesJsonSerializer.Serialize(original))!;

      result.Should().BeEquivalentTo(original);
  }
  ```
  FluentAssertions and xunit.v3 are `PackageReference`d for every test project by `tests/Directory.Build.props`, which also declares `<Using Include="FluentAssertions" />` and `<Using Include="Xunit" />` — so `Should()` and `[Fact]` need no `using` statements. Note that the existing files in `Aquifer.Common.UnitTests` happen to use plain `Assert.Equal`; either style compiles, and `Should()` matches the newer tests elsewhere in the repo.
- [x] **Step 2:** Add the four parameters to the record. Run the tests; both should pass.

---

### Task 3: Publish the options from a new admin endpoint

**Files:**
- Create: `src/Aquifer.API/Endpoints/Admin/Projects/PreTranslate/{Request,Validator,Endpoint}.cs`
- Test: `tests/Aquifer.API.IntegrationTests/Endpoints/Admin/Projects/PreTranslate/EndpointTests.cs` (new)

- [x] **Step 1: Write the failing tests.** Model on `tests/Aquifer.API.IntegrationTests/Endpoints/Resources/Content/Get/EndpointTests.cs`. Cover only what is safe to run against the real app — none of these reach the publish call:
  - no API key → `Unauthorized`
  - `AnonymousClient` → `Unauthorized`
  - `EditorClient`, `ManagerClient`, `ReviewerClient` → `Forbidden` (confirm the actual status FastEndpoints returns for a failed `Permissions()` check and assert that; adjust if it is `Unauthorized`)
  - a plainly non-existent project ID (e.g. `int.MaxValue`) with a sufficiently-permissioned client → `NotFound`. If no harness client can hold the new permission until Auth0 is configured, write this test and mark it skipped with a comment pointing at the Auth0 prerequisite, rather than deleting it.
- [x] **Step 2: `Request.cs`:**
  ```csharp
  public record Request
  {
      public int Id { get; set; }
      public bool ShouldForceRetranslation { get; set; }
      public bool ShouldSkipCompanyLeadAssignment { get; set; }
      public bool ShouldSkipProjectStartedNotification { get; set; }
      public IReadOnlyList<int>? ResourceContentIds { get; set; }
  }
  ```
- [x] **Step 3: `Validator.cs`:** `RuleFor(x => x.Id).GreaterThan(0);` and, when `ResourceContentIds` is non-null, require every element `> 0`. Membership in the project is a DB check and belongs in the endpoint, not here.
- [x] **Step 4: `Endpoint.cs`.** Constructor injects `AquiferDbContext`, `IUserService`, `ITranslationMessagePublisher`, and `ILogger<Endpoint>`. Configure:
  ```csharp
  Post("/admin/projects/{Id}/pre-translate");
  Permissions(PermissionName.RequeuePreTranslationProject);
  ```
  Handler, in order:
  1. `var user = await userService.GetUserFromJwtAsync(ct);`
  2. Load the project; `SendNotFoundAsync` if null.
  3. `AddError` if `project.Started is null` — message: "Project has not been started. Use POST /projects/{id}/start instead."
  4. `AddError` if `project.ProjectPlatformId` is not the Aquifer platform. Reuse the same constant basis as the subscriber's `aquiferProjectPlatformId`; prefer an existing shared constant if one exists rather than duplicating the literal `1` a second time.
  5. If `ResourceContentIds` is non-null and non-empty: dedupe, then query `ProjectResourceContents` for the project and `AddError` naming any requested IDs not in it.
  6. `ThrowIfAnyErrors();`
  7. Log project ID, all four options, and `user.Id`.
  8. Publish `new TranslateProjectResourcesMessage(project.Id, user.Id, request.ShouldForceRetranslation, request.ShouldSkipCompanyLeadAssignment, request.ShouldSkipProjectStartedNotification, dedupedIds)` — passing `null` when no IDs were supplied.
  9. `await SendAsync(null, StatusCodes.Status202Accepted, ct);` (confirm the idiomatic FastEndpoints call for a 202 with no body in this version and use it consistently).

  Do **not** create snapshots and do **not** set `project.Started`. There is no `SaveChangesAsync` in this handler.
- [x] **Step 5:** Run the integration tests.

---

### Task 4: Thread the options through the orchestration

**Files:** Modify `src/Aquifer.Jobs/Subscribers/TranslationMessageSubscriber.cs`

This is the one substantive logic change. Keep it mechanical and re-read the two flows before editing.

- [x] **Step 1: DTOs** (near line 965):
  - `OrchestrateProjectResourcesTranslationDto` gains `bool ShouldForceRetranslation`, `bool ShouldSkipCompanyLeadAssignment`, `bool ShouldSkipProjectStartedNotification`.
  - `TranslateResourceActivityDto` gains `bool ShouldForceRetranslation`.
  - `UpdateProjectPostTranslationActivityDto` gains `bool ShouldSkipCompanyLeadAssignment`, `bool ShouldSkipProjectStartedNotification`.

  These are durable-function payloads. In-flight orchestrations mid-replay across a deploy will see the new fields default to `false`, which matches today's behavior.
- [x] **Step 2: Subscriber `ProcessAsync`** (the `TranslateProjectResourcesMessage` overload). After loading `projectResourceContentIds`, if `message.ResourceContentIds` is non-null and non-empty, intersect:
  ```csharp
  var requestedIds = message.ResourceContentIds;
  if (requestedIds is { Count: > 0 })
  {
      projectResourceContentIds = projectResourceContentIds.Intersect(requestedIds).ToList();
      if (projectResourceContentIds.Count == 0)
      {
          throw new InvalidOperationException(
              $"None of the requested Resource Content IDs belong to Project ID {message.ProjectId}.");
      }
  }
  ```
  Keep the existing "project has no resource contents" throw ahead of this. Pass the three option flags into the `OrchestrateProjectResourcesTranslationDto`.
- [x] **Step 3: `OrchestrateProjectResourcesTranslationAsync`.** Select the origin once, outside the `Select`:
  ```csharp
  var translationOrigin = dto.ShouldForceRetranslation
      ? TranslationOrigin.BasicTranslationOnly
      : TranslationOrigin.Project;
  ```
  Pass `translationOrigin` and `dto.ShouldForceRetranslation` into each `TranslateResourceActivityDto`, and the two skip flags into `UpdateProjectPostTranslationActivityDto`. Leave the `catch` / poison-queue block untouched.
- [x] **Step 4: `TranslateResourceViaActivityAsync`.** Replace the hardcoded `false` with `dto.ShouldForceRetranslation`.
- [x] **Step 5: `UpdateProjectPostTranslationAsync`.** Make both halves conditional:
  - Move the `CompanyLeadUserId ?? throw` **inside** the assignment branch. Today it throws unconditionally; with assignment skipped, a project with no company lead must not fail the activity.
  - Wrap the query/assign/`SaveChangesAsync` block in `if (!dto.ShouldSkipCompanyLeadAssignment)`.
  - Wrap `PublishSendProjectStartedNotificationMessageAsync` in `if (!dto.ShouldSkipProjectStartedNotification)`.
  - Log which steps were skipped.
- [x] **Step 6:** Build and run the full server test suite. Re-read `Projects/Start/Endpoint.cs`'s publish call and confirm it still compiles unchanged against the new record — it should, because every new parameter is optional.

---

### Task 5: Unit-test the orchestration option logic

**Files:** Create `tests/Aquifer.Jobs.UnitTests/Subscribers/ProjectPreTranslationOptionsTests.cs`

The orchestration methods are not directly unit-testable (durable-function context, DB, injected services). Rather than contorting the tests, extract the two pure decisions made in task 4 into `internal static` helpers on `TranslationMessageSubscriber` and test those:

- [x] **Step 1:** Check whether `Aquifer.Jobs` already has `InternalsVisibleTo` for `Aquifer.Jobs.UnitTests` (`Aquifer.AI` does, per the translation-pairs work). If not, add it.
- [x] **Step 2: Write the failing tests** for:
  - `internal static TranslationOrigin GetProjectTranslationOrigin(bool shouldForceRetranslation)` → `BasicTranslationOnly` when true, `Project` when false.
  - `internal static IReadOnlyList<int> FilterRequestedResourceContentIds(IReadOnlyList<int> projectIds, IReadOnlyList<int>? requestedIds)` → returns all project IDs when `requestedIds` is null or empty; returns only the intersection otherwise; ignores requested IDs not in the project; deduplicates.
- [x] **Step 3:** Extract the helpers and call them from task 4's code paths. Run the tests.

---

### Task 6: Document the admin re-run path

**Files:** Modify `docs/ai-translation-workflow.md`

- [x] **Step 1:** Read the existing project-translation section and add a subsection covering: the endpoint, the permission, what each option does, the `BasicTranslationOnly` coupling and its consequence (a forced run can overwrite in-progress editor content), and that this replaces the manual poison-queue replay. Match the document's existing voice; do not restate the design doc.

---

### Task 7: Web — permission and API helper

**Files:**
- Modify: `src/lib/stores/auth.ts`, `src/lib/utils/projects.ts`
- Test: `src/lib/utils/projects.test.ts`

- [x] **Step 1:** Add `RequeuePreTranslationProject = 'requeue-pre-translation:project'` to the `Permission` enum. The enum is loosely grouped rather than strictly sorted — place it sensibly and match the surrounding style.
- [x] **Step 2: Write the failing tests** for a `parseResourceContentIds(input: string): { ids: number[]; invalid: string[] }` helper — comma and/or whitespace separated, trims, drops empties, dedupes, returns empty for blank input, and reports non-numeric entries in `invalid` rather than silently coercing (`NaN` reaching the API would be a confusing 400).

  **Do not reuse the existing `parseNumbersListFromString` in `src/lib/utils/number-list-parser.ts`**, despite the apparent overlap. It is built for bounded ranges (it requires `min`/`max`, expands `1-5` and `all`, and **silently discards** anything outside the bounds). Resource content IDs have no natural upper bound, and silently dropping an ID the admin typed is exactly the failure mode this helper exists to prevent — the admin would believe they queued five resources and get four. Leave that function alone; it has three existing callers. Add a comment on the new helper saying why it is separate, so the duplication reads as deliberate.

  This will be the repo's first substantive vitest file — `src/index.test.ts` is a placeholder `1 + 2 === 3` test. Follow its `describe`/`it`/`expect` import style from `vitest`.
- [x] **Step 3:** Implement `parseResourceContentIds`, and alongside `startProject`:
  ```ts
  export async function requeueProjectPreTranslation(id: number | string, options: {
      shouldForceRetranslation: boolean;
      shouldSkipCompanyLeadAssignment: boolean;
      shouldSkipProjectStartedNotification: boolean;
      resourceContentIds: number[] | null;
  }) {
      await postToApi(`/admin/projects/${id}/pre-translate`, options);
  }
  ```
- [x] **Step 4:** Run `yarn test` (vitest).

---

### Task 8: Web — the admin page

**Files:** Create `src/routes/admin/projects/[projectId]/+page.ts` and `+page.svelte`

- [x] **Step 1: `+page.ts`.** Copy the guard from `src/routes/admin/api-keys/create/+page.ts` exactly, swapping the permission. Then load the project for the ID in `params` so the page can show its name and started date; reuse whatever loader `src/routes/projects/[projectId]/+page.ts` uses rather than inventing a new fetch.
- [x] **Step 2: `+page.svelte`.** Runes-based, matching the API-keys page's structure and Tailwind idiom:
  - `BackButton`, heading naming the project and ID, and its started date.
  - Three checkboxes, defaulting to unchecked.
  - A text input for resource content IDs, with helper text that blank means the whole project, showing the parse error from task 7 inline.
  - A warning shown when "force re-translation" is checked, stating that it re-translates from the original snapshot and can overwrite editor work in progress.
  - A two-step confirm: the submit button opens a confirmation naming the project; only the confirm actually posts.
  - `isSaving` state, success banner, and `isAuthorizationError` / `log.exception` error handling copied from the API-keys page.
- [x] **Step 3:** `yarn lint` (it runs prettier, eslint, the unused-translations check, and `svelte-check` in one pass). There is no `yarn check` script.

---

### Task 9: Web — link from the project page

**Files:** Modify `src/routes/projects/[projectId]/+page.svelte`

- [x] **Step 1:** Add a link to `/admin/projects/{$project.id}` wrapped in `{#if $userCan(Permission.RequeuePreTranslationProject)}`. The page already imports `Permission` and `userCan`. Place it near the existing start-project control, labelled so it is clearly an admin action (e.g. "Admin: re-run pre-translation"). Only show it when `$project.started` is set, since the endpoint rejects unstarted projects.
- [x] **Step 2:** `yarn lint`.

---

### Task 10: Verify end to end

- [x] **Step 1:** Full server build + all test projects green.
- [x] **Step 2:** `yarn lint:ci` and `yarn test` green.
- [x] **Step 3:** Confirm by reading the diff that `Projects/Start/Endpoint.cs` is unmodified and that its publish call still produces a message identical to today's.
- [ ] **Step 4:** Manual verification requires the Auth0 prerequisite. Once granted in dev: start a project, let it finish, then re-run it from `/admin/projects/{id}` with each option and confirm against logs and the DB. Until then, state plainly in the PR that manual verification is outstanding and blocked on Auth0.
