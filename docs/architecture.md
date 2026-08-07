# Architecture

`aquifer-server` is the back end of the Aquifer platform. It owns the single
Azure SQL database and exposes it through three separate ASP.NET Core APIs
(plus background jobs), each aimed at a different audience. For how this repo
fits with the others in the org, see [ecosystem.md](ecosystem.md).

## The three APIs

All three APIs share the same data layer (`Aquifer.Data`), use FastEndpoints,
and are deployed as separate Azure Web Apps. They are separate apps — rather
than one API with varying auth — so that each can scale, deploy, and apply
rate-limiting/caching policies independently.

| App | Audience | Auth | Local port | Production |
|---|---|---|---|---|
| `Aquifer.API` | Internal apps: content-manager-web, well-web, marketing site | Auth0 JWT (users) **or** API key (scope `InternalApi`); `/marketing` routes are API-key-only | 5257 | `api-bn.aquifer.bible` |
| `Aquifer.Public.API` | External consumers of Aquifer content | API key (scope `PublicApi`) | 5187 | `api.aquifer.bible` |
| `Aquifer.Well.API` | bible-well mobile app (discontinued — see [ecosystem.md](ecosystem.md)) | API key (scope `WellApi`) | 5139 | `api-well.aquifer.bible` |

Notes:

- API keys are stored hashed in the `ApiKeys` table and checked by
  `ApiKeyAuthorizationMiddleware` (cached via `CachingApiKeyService`).
- `Aquifer.Well.API` is deliberately slim: it exposes exactly what the mobile
  app needs to sync content, and its OpenAPI spec is the source for the
  Kiota-generated C# client in the bible-well repo (see
  [development.md](development.md#client-generation)).
- `Aquifer.Public.API` owns its OpenAPI documentation (`OpenApi/`), which is
  what external integrators see; keep it accurate.

## Project map

| Project | Purpose |
|---|---|
| `Aquifer.API` | Internal API (admin CMS operations, reporting, content workflow) |
| `Aquifer.Public.API` | Public content-consumption API |
| `Aquifer.Well.API` | Mobile sync API for bible-well |
| `Aquifer.Data` | EF Core `DbContext`, entities, migrations, event handlers. The only project that talks to the database schema directly |
| `Aquifer.Migrations` | Startup project for running EF migrations (used by the CLI commands and by CI/CD) |
| `Aquifer.Common` | Shared utilities: configuration, queue message publishers, Azure clients (Key Vault, storage), caching services |
| `Aquifer.Jobs` | Azure Functions app: queue subscribers, timer-triggered managers, durable-task orchestrations |
| `Aquifer.AI` | Azure OpenAI integration for machine translation drafts |
| `Aquifer.JsEngine` | Executes JavaScript from .NET (used to run the aquifer-tiptap package server-side) |
| `Aquifer.Tiptap` | Bridges `Aquifer.JsEngine` and the aquifer-tiptap npm package to render/validate content HTML ↔ JSON on the server |

## The content format

Resource content is stored as **Tiptap/ProseMirror JSON** in
`ResourceContentVersions.Content`. The node/mark vocabulary (Bible references,
videos, comments, footnotes, indentation) is defined by the `aquifer-tiptap`
package, which is shared with the front ends so that editing
(content-manager-web), rendering (well-web), and server-side processing (this
repo, via `Aquifer.JsEngine`/`Aquifer.Tiptap`) all agree on the schema. The
`aquifer-api-samples` repo documents the JSON schema for external consumers.

## Background jobs

`Aquifer.Jobs` is an Azure Functions app using Azure Storage Queues. APIs
publish messages via the queue publisher services in `Aquifer.Common`
(`Messages/Publishers`); subscribers in `Aquifer.Jobs/Subscribers` consume
them. Queue names are defined in `Aquifer.Common/Messages/Queues.cs`:

| Queue | Purpose |
|---|---|
| `send-email`, `send-templated-email` | Transactional email via SendGrid |
| `send-project-started-notification` | In-app/email notifications |
| `track-resource-content-request` | Records content-request analytics |
| `translate-language-resources`, `translate-project-resources`, `translate-resource` | AI machine-translation pipeline (durable-task fan-out per resource) |
| `upload-resource-content-audio` | Audio upload processing |
| `generate-resource-content-similarity-score` | Computes similarity scores between source and translated content |

Timer-triggered managers (`Aquifer.Jobs/Managers`) handle periodic work:
new-content digest emails, resource-assignment notifications, and poison-queue
alerting. Language-specific translation post-processing lives in
`Services/TranslationProcessors`.

`JobHistory` rows in the database track durable orchestration runs so jobs are
observable from the admin CMS.

## Key flows

### Content lifecycle (draft → published)

1. An editor creates a draft `ResourceContentVersions` row in
   content-manager-web (via `Aquifer.API`).
2. The `ResourceContents.Status` moves through the workflow
   (`New` → `Aquiferize*`/`Translation*` review steps → `Complete`); every
   transition is recorded in `ResourceContentVersionStatusHistory` and
   assignment changes in `ResourceContentVersionAssignedUserHistory`.
3. "Aquiferizing" means creating content in a language directly from scratch;
   "Translation" means deriving it from existing source-language content,
   optionally starting from an AI draft (`TranslationAwaitingAiDraft` →
   `TranslationAiDraftComplete`) produced by the translation queues above.
4. On publish, the version's `IsPublished` flag is set (a version may be a
   draft **or** published, never both — enforced by a check constraint and
   filtered unique indexes) and a snapshot is written to
   `ResourceContentVersionSnapshots`, which is what the public/well APIs serve.

### Mobile sync (bible-well)

> Note: the bible-well app is discontinued; this section documents the
> Well.API design for reference in case mobile work resumes.



bible-well calls `Aquifer.Well.API` with its API key to download published
resource content and Bible texts into its on-device SQLite database. The API
surface is versioned through the OpenAPI spec; regenerate the client after
endpoint changes.

## Cross-cutting concerns

- **Configuration**: `appsettings.json` + `appsettings.{Environment}.json`
  (not checked in); secrets come from Azure Key Vault via `AzureKeyVaultClient`.
- **Telemetry**: Application Insights in all apps.
- **Caching**: output caching in the public-facing APIs; `Caching*Service`
  classes for hot lookup data (languages, API keys, versification).
- **Read-only DB access**: `AquiferDbReadOnlyContext` is used for heavy
  read paths so reporting doesn't contend with writes.

---
_Last verified: 2026-08-03_
