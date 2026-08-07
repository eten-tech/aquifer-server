# Aquifer Server

The back end of the [Aquifer](https://aquifer.bible) platform: three ASP.NET
Core APIs plus background jobs over a single Azure SQL database, serving
biblical translation resources to the apps in the
[eten-tech ecosystem](docs/ecosystem.md).

| API | Audience | Auth |
|---|---|---|
| `Aquifer.API` | Internal apps (content-manager-web, well-web) | Auth0 JWT / API key |
| `Aquifer.Public.API` | External consumers | API key |
| `Aquifer.Well.API` | bible-well mobile app | API key |

## Quickstart

```bash
# 1. Install the .NET 9 SDK (see global.json)

# 2. Configure settings for the project you want to run
cp src/Aquifer.API/appsettings.example.json src/Aquifer.API/appsettings.Development.json
#    ...fill in values for your environment...

# 3. Run
dotnet restore
dotnet watch run --project src/Aquifer.API
```

Everything beyond this — EF migrations, running the jobs locally with
Azurite, API client generation, lint/test — is in
[docs/development.md](docs/development.md).

## Documentation

| Doc | What's in it |
|---|---|
| [docs/ecosystem.md](docs/ecosystem.md) | **Start here if you're new.** How all the repos in the org fit together |
| [docs/architecture.md](docs/architecture.md) | The three APIs, project map, content format, background jobs, key flows |
| [docs/database.md](docs/database.md) | Domain model, key tables, and schema conventions |
| [docs/development.md](docs/development.md) | Setup, migrations, jobs, client generation, lint/test |
| [docs/infrastructure.md](docs/infrastructure.md) | Azure SQL auth, managed identities, GitHub Actions OIDC |
