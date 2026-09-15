# giftlist-gateway

API Gateway: grpc-web ingress from the SPA, GraphQL egress, and the read model built from
GiftLists' integration events. Owns the `gateway` database and the `gateway` Rebus queue (both
singular — CONVENTIONS.md "Persistence").

```
protos/                      THE .proto FILES — this repo owns them
clients/typescript/          THE PUBLISHED npm PACKAGE built from them (see below)
src/Gateway.Application      use cases and ports; BuildingBlocks only
src/Gateway.Infrastructure   grpc-web services, HotChocolate, Mongo read model, Rebus handlers
src/Gateway.Host             composition root; wiring only
```

**There is no `Gateway.Domain` and no `Gateway.Contracts`, and neither is an oversight.** No
Domain, because the Gateway carries no business rules of its own — only wire translation
(ARCHITECTURE.md "Mapping to Clean Architecture's rings"). No Contracts, because a
`*.Contracts` package holds the events a service publishes and the commands it accepts
(ARCHITECTURE.md "Contracts: each service owns and publishes its own"), and the Gateway
publishes no event and accepts no command: it *sends* other services' commands and *subscribes*
to their events. Its public surface is the generated TypeScript client below and its GraphQL
schema, neither of which is a NuGet package.

It consumes `Identity.Contracts` and `GiftLists.Contracts` as packages — permitted, because
Infrastructure may reference another service's contracts (CONVENTIONS.md "Project reference
graph").

## `@giftlist/gateway-client` — the generated TypeScript client

One `.proto` file, two generators, never drifting:

- `Grpc.Tools` generates the C# server stubs for `Gateway.Infrastructure`, straight from
  `protos/*.proto` (see the `<Protobuf>` items in `Gateway.Infrastructure.csproj`).
- `buf generate` in `clients/typescript/` generates the browser client from the same files, and
  `npm pack` publishes it as a tarball for `giftlist-web` (ARCHITECTURE.md "Repository layout").

```
cd clients/typescript
npm install
npm run build                                   # buf generate + tsc
npm pack --pack-destination ../../../local-feed  # -> giftlist-gateway-client-0.1.0.tgz
```

`prepack` runs the build, so `npm pack` cannot ship a stale client. The generator used to live in
`giftlist-web` and reach across the monorepo at `../gateway/protos`; after the split there is no
such path, and a relative path into a sibling clone would have made the SPA's build depend on
whether another repo happened to be checked out.

## Directory.Build.props is a copy, and it is checked

`net10.0`, `LangVersion latest`, nullable on, warnings as errors, implicit usings — set once in
`Directory.Build.props` at this repo's root, inherited by every project. No `.csproj` sets
`TargetFramework` itself (CONVENTIONS.md "Target framework").

Before the split there was one such file, at the monorepo root, and MSBuild's directory walk gave
every service the same values. MSBuild does not walk out of a repo, so each .NET repo now has its
own copy — and copies drift. **Do not hand-edit this one.** Edit the canonical copy in
`giftlist-buildingblocks`, then run `giftlist-devenv/scripts/sync-repo-roots.sh`, which rewrites
every copy and regenerates the `repo-root-files.sha256` manifest beside each.

Two tests fail if you edit it in place, and they check different things:

- `RepoRootFileSyncTests` — this copy is byte-identical to the canonical one.
- `TargetFrameworkTests.DirectoryBuildProps_ShouldMatchConventionsVerbatim` — the content is the
  block CONVENTIONS.md documents. Every repo can agree on a wrong file; this is what catches it.

The `Architecture/` suite under `tests/*.UnitTests/` is governed the same way: canonical copy in
`giftlist-giftlists`, propagated by `giftlist-devenv/scripts/sync-arch-tests.sh`, pinned by
`architecture-tests.sha256` and `ArchitectureTestSyncTests`.

## Where this repo sits

Seven repos under `goodsell-engineering`, cloned as siblings (ARCHITECTURE.md "Repository
layout"):

```
giftlist/
  local-feed/              <- .nupkg and .tgz files land here; not a git repo
  giftlist-devenv/         <- docker compose, make up, the sync scripts
  giftlist-buildingblocks/
  giftlist-gateway/
  giftlist-identity/
  giftlist-giftlists/
  giftlist-reservations/
  giftlist-web/
```

Design documents (`ARCHITECTURE.md`, `CONVENTIONS.md`) live in the workspace repository, not in
any of the seven: they govern all of them, a home inside one is invisible to the other six, and
seven copies is exactly the drift they warn about. Comments here cite them by document and
heading text, never by section number (CONVENTIONS.md "Citing the rules").

## What does not work yet, and whose job it is

The local folder feed and this repo's `nuget.config` landed in GL-26 — see that file's own
comments for the packageSourceMapping reasoning (dependency confusion against nuget.org's
unrelated `BuildingBlocks` and `Identity.Contracts` packages) and for how the same
`"../local-feed"` value resolves correctly both on the host and inside the .NET service
containers. What's still missing:

| Missing | Issue |
|---|---|
| Semantic versioning discipline and consumer pinning | GL-27 |
| `make pack-all` (dependency-ordered, refuses to overwrite a version already in the feed) and `clone-all.sh` | GL-28 |
| Compose mounting the feed into the containers | GL-29 |
| Per-repo CI (build and test only; there is nowhere to publish to) | GL-30 |

Restore and build normally:

```
dotnet restore <Solution>.sln
dotnet build <Solution>.sln --no-restore
dotnet test tests/*.UnitTests/*.UnitTests.csproj
```
