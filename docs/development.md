# Development Guide

Day-to-day setup and workflows for working in this repo. For what the pieces
are, see [architecture.md](architecture.md).

## Setup

- Install the [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
  (see `global.json` for the pinned version).
- Copy `appsettings.example.json` as `appsettings.Development.json` in the
  project(s) you're running and provide the values needed for your instance.
  If you want multiple instances of this file, create them per environment and
  set `ASPNETCORE_ENVIRONMENT` accordingly, e.g.
  `dotnet run --environment Production`. Environment configurations have no
  reason to be checked in.

```bash
dotnet restore
```

## Running the APIs

```bash
# with hot reload (substitute the project you want)
dotnet watch run --project src/Aquifer.API

# normal mode
dotnet run --project src/Aquifer.API
```

The three APIs run side by side; see [architecture.md](architecture.md#the-three-apis)
for their ports and audiences. `Aquifer.Jobs` runs separately (see below).

## Database Migrations

***Important***: Initialize the settings file in the `src/Aquifer.Migrations`
directory by copying `appsettings.example.json` as
`appsettings.Development.json` and providing the values needed for your
instance.

Entity Framework will generate migrations by comparing the C# entities defined
in the project and the current state of the database.

### Add New Entity/Migration

First, create your entity in the `Aquifer.Data/Entities` directory.

Next, add your entity definition to the `src/Aquifer.Data/AquiferDbContext.cs`
file. Entities are listed in alphabetical order.

### Create a New Migration

```bash
dotnet ef migrations add --startup-project src/Aquifer.Migrations --project src/Aquifer.Data --context AquiferDbContext <MigrationNameHere>
```

Your new migration will be created in the `src/Aquifer.Data/Migrations`
directory along with the `.Designer` file and updated
`AquiferDbContextModelSnapshot.cs` file.

If you run that command and the new migration file is empty, that means there
were no changes detected between the C# entities and the database. You can use
this to your advantage to create empty migrations and add your own custom code
(some production indexes are maintained this way — see the note in
`ResourceContentVersionEntityConfiguration`).

### Apply, list, and remove migrations

```bash
# apply migrations to the DB
dotnet ef database update --startup-project src/Aquifer.Migrations --project src/Aquifer.Data --context AquiferDbContext

# list all migrations (useful before applying)
dotnet ef migrations list --startup-project src/Aquifer.Migrations --project src/Aquifer.Data --context AquiferDbContext

# remove the last migration (useful when developing locally)
dotnet ef migrations remove --startup-project src/Aquifer.Migrations --project src/Aquifer.Data --context AquiferDbContext
```

## Jobs/Queues

We use Azure Storage Queues and Azure Functions for queueing and running jobs
(see [architecture.md](architecture.md#background-jobs) for the queue list).
To develop locally, you'll need
[Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite?tabs=visual-studio,blob-storage)
and
[Azure Function Core Tools](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local).

1. Run Azurite so that you can have local queues. In the `aquifer-server` dir, run `azurite`.
2. Run Aquifer.Jobs using the function core tools. In `aquifer-server/src/Aquifer.Jobs` run `func start`.

## Client Generation

`Aquifer.Well.API` supports C# client generation based upon its OpenAPI spec.
The generated client lives in the bible-well repo, so this command assumes you
have a local clone of bible-well in a folder next to this repo:

```bash
dotnet run --project src/Aquifer.Well.API --generateclients true
```

Running the command updates the client code in that repo based on changes to
the OpenAPI spec. Commit the result in bible-well as a separate PR.

## Lint

```bash
dotnet build --no-incremental /p:WarningsAsErrors=true
```

## Test

```bash
dotnet test
```

## CLI REPL

If you want to use a REPL outside of an IDE, csharprepl is a good option:

```bash
# install
dotnet tool install -g csharprepl

# run
csharprepl
```

Then load a project from the csharprepl prompt:
`#r "src/Aquifer.API/Aquifer.API.csproj"`

---
_Last verified: 2026-08-03_
