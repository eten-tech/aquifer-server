# Admin-triggered re-run of project pre-translation

Status: design approved; implementation plan in [plan.md](plan.md).

See [ai-translation-workflow.md](../../ai-translation-workflow.md) for the
translation workflow this builds on.

## Problem

When a project's pre-translation run fails partway through, there is no
supported way to re-run it. `OrchestrateProjectResourcesTranslationAsync`
(`src/Aquifer.Jobs/Subscribers/TranslationMessageSubscriber.cs`) catches any
orchestration failure, republishes the original queue message text to
`translate-project-resources-poison`, and logs:

> "A poison message will be published to `{PoisonQueueName}` to enable manual
> retry. Manual dev intervention is required."

That manual intervention is a developer opening Azure Storage Explorer (or
equivalent), finding the poison message, and hand-copying it back onto
`translate-project-resources`. It requires production queue access, it is
invisible to everyone but the developer doing it, and it can only replay the
run *exactly* as it was originally queued — there is no way to re-run just the
handful of resources that failed, to force a re-translation of content that
already has an AI draft, or to avoid re-notifying the company lead.

## Goal

Give admins a guarded, self-service way to re-run a started project through
pre-translation, with per-run control over what the re-run does, so that
routine recovery no longer requires a developer with production queue access.

## Non-goals

- Replacing or changing the normal `POST /projects/{id}/start` flow. Its
  behavior must be byte-for-byte unchanged.
- Draining or managing the poison queue itself. This feature gives admins a
  way to re-run a project; it does not read poison messages. A poison message
  left behind after a successful admin re-run is still cleaned up the way it
  is today.
- Any kind of run history, audit table, or progress UI. Logs remain the record
  of what happened.
- Concurrency locking. See [Deliberate omissions](#deliberate-omissions).

## Key constraint: force re-translation is coupled to TranslationOrigin

`TranslateResourceCoreAsync`
(`src/Aquifer.Jobs/Subscribers/TranslationMessageSubscriber.cs:681`) throws
outright if `shouldForceRetranslation` is true and the origin is anything
other than `TranslationOrigin.BasicTranslationOnly`:

```csharp
if (shouldForceRetranslation && translationOrigin != TranslationOrigin.BasicTranslationOnly)
{
    throw new InvalidOperationException(
        $"Translation Origin must be {TranslationOrigin.BasicTranslationOnly} when {nameof(shouldForceRetranslation)} is true but was \"{translationOrigin}\".");
}
```

So "force re-translation" cannot be a flag bolted onto
`TranslationOrigin.Project`. It has to *select* the origin the fan-out uses:

| Run option | Origin used per resource | Behavior |
|---|---|---|
| force off (default) | `TranslationOrigin.Project` | Today's behavior. Only content in `TranslationAwaitingAiDraft`/`AquiferizeAwaitingAiDraft` translates; anything else is gracefully skipped, as is any non-aquiferization content that already has `ContentUpdated` set. A new snapshot is added; status moves to `*AiDraftComplete`. |
| force on | `TranslationOrigin.BasicTranslationOnly` | Re-translates from the *first* snapshot rather than current content, overwrites the most recent `*AwaitingAiDraft` snapshot instead of adding one, and leaves status alone unless the content is still in the awaiting status. |

This is not a workaround — `BasicTranslationOnly`'s own doc comment already
describes it as "Not triggered by a user flow; manually dev triggered only",
which is precisely the workflow being productized here. The feature is, in
effect, a front door for `BasicTranslationOnly` at project scope.

Two consequences worth stating plainly:

- With force on, an admin **can overwrite editor work in progress**, because
  `BasicTranslationOnly` does not bail out on content that already has
  updated content. This is the intended escape hatch, but it is why the
  endpoint is admin-guarded and why the UI confirms before firing.
- With force on, statuses mostly do *not* advance, so a forced re-run of a
  project whose content has already moved to editor review leaves those
  statuses where they are.

## Design

### Guard

A new permission, `requeue-pre-translation:project`, added as
`PermissionName.RequeuePreTranslationProject` in
`src/Aquifer.API/Common/Permissions.cs` and as
`Permission.RequeuePreTranslationProject` in
`content-manager-web`'s `src/lib/stores/auth.ts`.

Permissions in this codebase are only string constants; the actual grant lives
in Auth0. **This permission must be created in Auth0 and granted to the Admin
role before release**, in every environment, or the endpoint is unreachable
for everyone. That is a manual, out-of-repo step and is called out as such in
the plan.

`edit:projects` was considered and rejected: it is held by ordinary project
managers, and this endpoint can spend real AI budget and overwrite drafts.
Reusing `create:apikey` was rejected as conflating two unrelated capabilities.
The API-key `ApiKeyScope.Admin` pattern used by `/admin/caches/clear` was
rejected because it is machine-to-machine and would not work from
content-manager-web, which authenticates with an Auth0 JWT.

### Endpoint

`POST /admin/projects/{Id}/pre-translate`, in
`src/Aquifer.API/Endpoints/Admin/Projects/PreTranslate/`, following the
`Admin/ApiKeys/Create/` layout (`Endpoint.cs`, `Request.cs`, `Validator.cs`).

Request:

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Id` | `int` (route) | — | Project ID |
| `ShouldForceRetranslation` | `bool` | `false` | Select `BasicTranslationOnly` origin; re-translate from the first snapshot |
| `ShouldSkipCompanyLeadAssignment` | `bool` | `false` | Skip the assignment half of post-translation processing |
| `ShouldSkipProjectStartedNotification` | `bool` | `false` | Suppress the project-started notification |
| `ResourceContentIds` | `int[]?` | `null` | Restrict the run to these resource contents; null/empty means the whole project |

The handler validates and publishes. It deliberately does **not** do the two
things `Projects/Start/Endpoint.cs` does beyond publishing:

- It does not create new snapshots. The existing snapshots are what a forced
  re-translation reads its original content from; adding more would corrupt
  that and inflate snapshot history on every re-run.
- It does not touch `project.Started`. The project is already started; that is
  a precondition, not something to re-stamp.

`StartedByUserId` on the published message is the admin firing the re-run, via
`IUserService.GetUserFromJwtAsync` — so history rows attribute the re-run to
the person who triggered it.

Validation (all as FastEndpoints errors except where noted):

- Project does not exist → 404.
- `project.Started is null` → error telling the admin to use
  `POST /projects/{id}/start` instead. Re-running pre-translation on a project
  that was never started would skip snapshot creation and then fail in
  `TranslateResourceCoreAsync`, which throws when a resource content version
  has no pre-existing snapshots.
- `project.ProjectPlatformId` is not Aquifer → error. The subscriber silently
  returns for non-Aquifer platforms, so without this check the admin gets a
  202 and nothing ever happens.
- Any supplied `ResourceContentIds` not in the project → error naming the
  offending IDs.
- Duplicate IDs in `ResourceContentIds` → deduplicated rather than rejected.

Returns `202 Accepted` — the work is queued, not done.

### Message and orchestration changes

`TranslateProjectResourcesMessage` gains the four options. Messages are
serialized with Newtonsoft (`MessagesJsonSerializer`, camelCase, no
`MissingMemberHandling.Error`), so messages already sitting on
`translate-project-resources` or its poison queue when this deploys will
deserialize with the defaults and behave exactly as they do today. A
round-trip test locks that in.

`OrchestrateProjectResourcesTranslationDto` carries the same options.
`UpdateProjectPostTranslationActivityDto` carries the two skip flags.
`TranslateResourceActivityDto` already carries `TranslationOrigin`; it gains
`ShouldForceRetranslation`, which `TranslateResourceViaActivityAsync`
currently hardcodes to `false`.

Flow changes:

- The queue subscriber's `ProcessAsync` filters
  `projectResourceContentIds` to the requested subset when one was supplied,
  and errors if the intersection is empty (rather than silently translating
  the whole project).
- `OrchestrateProjectResourcesTranslationAsync` picks
  `BasicTranslationOnly` vs `Project` per the force flag, passes
  `ShouldForceRetranslation` into each activity, and passes the skip flags
  into the post-translation activity.
- In `UpdateProjectPostTranslationAsync`, the assignment block and the
  `PublishSendProjectStartedNotificationMessageAsync` call each become
  conditional. Note the existing `CompanyLeadUserId ?? throw` runs
  unconditionally today; with assignment skipped it must not throw, so that
  null check moves inside the assignment branch.

`Projects/Start/Endpoint.cs` keeps calling the publisher with all defaults and
is otherwise untouched.

### Web UI

`src/routes/admin/projects/[projectId]/+page.ts` and `+page.svelte` in
content-manager-web, following `admin/api-keys/create/` exactly: `+page.ts`
does `await parent()` then `redirect(302, '/')` unless
`userCan(Permission.RequeuePreTranslationProject)`.

The page loads the project, shows its name, ID, and started date, and offers
the three checkboxes plus an optional comma-separated resource-content-ID
field. Because a forced run can overwrite in-progress editor work, firing
requires a confirmation step that names the project. Errors use the same
`isAuthorizationError` / `log.exception` handling as the API-keys page.

A link into this page is rendered on `/projects/[projectId]`, gated on the
same permission, so admins do not have to hand-type URLs.

### Testing

The existing `Aquifer.API.IntegrationTests` harness authenticates real Auth0
users per `UserRole` against a real database, and the app publishes to real
queues — there are no fakes. That shapes what is testable:

- **Feasible and valuable:** negative cases via the existing clients —
  unauthenticated, no API key, and authenticated-but-unauthorized
  (`EditorClient`, `ManagerClient`, `ReviewerClient`) all rejected. These need
  no admin identity, since they assert the request is turned away.
- **Written but skipped until Auth0 is configured:** the 404-for-unknown-project
  case, which needs a client that actually holds the new permission. The
  harness has no `AdminClient` and cannot have one until the permission exists
  in Auth0.
- **Deliberately not written:** a happy-path integration test. It would
  enqueue a real translation run and spend real AI budget on every CI pass.
  It would also require the admin identity that does not yet exist.
- Validation and orchestration logic therefore carries its weight in
  `Aquifer.Jobs.UnitTests`: origin selection, resource-ID filtering, skip-flag
  handling, and the message backward-compatibility round trip.

On the web side, the existing vitest setup covers the request-body shaping
(checkbox state and the ID field → the posted payload), which is where the
logic actually is.

## Deliberate omissions

**No concurrency lock.** Nothing prevents an admin from firing a second run
while one is in flight. Detecting a live durable orchestration from the API
tier is not cheap, the individual activities are largely idempotent, and only
a small number of people hold the permission. The UI confirmation is the
guard. If this proves insufficient in practice, adding a lock means tracking
run state on the project and is a separate piece of work.

**No run history.** Logs are the record. The endpoint logs project ID, the
options used, and the triggering user before publishing.
