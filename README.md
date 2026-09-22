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

## Security pass conclusions (GL-44)

Decisions, not silent defaults — each one lives in code as well as here; this section is the one
place to find all of them together.

- **CORS is already locked down** (GL-18, predates GL-44): the allowed origin comes from
  `Cors:AllowedOrigins` (`Program.cs`), unset by default — an empty origin list fails closed
  rather than falling back to `*`. devenv's compose sets it to the SPA's own dev origin
  (`Cors__AllowedOrigins__0`). No change was needed here; verified, not re-implemented.
- **GraphQL IDE, `?sdl` (GL-113) and introspection are one policy**, decided together rather than
  left at HotChocolate's defaults (all three on, to anyone, in every environment):
  `GraphQLServerOptions.Tool.Enable` and `.EnableSchemaRequests`
  (`GatewayGraphQlEndpointRouteBuilderExtensions.MapGatewayGraphQlEndpoints`) and
  `DisableIntrospection` (`GatewayInfrastructureServiceCollectionExtensions.AddGraphQl`) all key
  off `IHostEnvironment.IsDevelopment()`. Development keeps all three; every other environment
  turns them off. **devenv runs as Development** — its compose sets `DOTNET_ENVIRONMENT:
  Development` in the `x-dotnet-env` anchor the gateway inherits, and the host honours that
  whenever `ASPNETCORE_ENVIRONMENT` is unset, which it is — **so the demo stack does serve the
  IDE, `?sdl` and introspection**, to anyone who can reach the port. That is the intended answer
  to GL-113 for a laptop demo that exists to be explored, not an oversight: what GL-113 asked for
  is that the exposure be chosen, and the choice is "on where a developer is the only caller, off
  everywhere else". A deployment that faces anything wider inherits the off half by having any
  other environment name. `GraphQlSchemaExposureTests` proves the "off" half against a second,
  Production-mode host built alongside the normal Development one, and the "on" half against the
  Development host — both directions, so neither can pass for the wrong reason.
- **`app.UseAuthorization()` with no default policy is intentional, not an oversight**: ownership
  lives in each interactor (see `ViewGiftListInteractor` and friends), and the middleware call
  exists only so `RequireAuthorization()`/`RequireRateLimiting()`
  have something to opt into per endpoint (`GatewayGrpcEndpointRouteBuilderExtensions`'s own doc
  comment carries the full reasoning). Left as-is; documented rather than changed.
- **GL-109 — a browser GET to `/graphql?query=...` no longer 500s.** `AllowedGetOperations.None`
  (GL-105) already gave an API client (no `Accept: text/html`) a clean 405; a plain browser
  request carrying the same `query=` string but preferring `text/html` reached a different,
  unhandled code path inside HotChocolate's own content negotiation instead, and threw. Read from
  the code rather than run in every environment: this repo's `Program.cs` calls neither
  `UseDeveloperExceptionPage` nor `UseExceptionHandler` itself, and `WebApplication.Build()` only
  auto-registers the former when the environment is Development — so the un-fixed behaviour was a
  full stack-trace developer exception page in Development, and a bare, empty-bodied 500 in every
  other environment (no leak, but not a deliberate answer either). **devenv is Development** (see
  the bullet above), so the demo stack was serving the stack trace, not the bare 500 — which is
  the more severe of the two readings, and the one this fix actually removes. Fixed with `UseGatewayGraphQlGetQueryGuard`, a small middleware ahead of
  `MapGraphQL()` that answers any GET carrying `query=` with the same 405, independent of the
  `Accept` header — deterministic, and not dependent on which internal HotChocolate branch a given
  Accept header happens to select. `GraphQlGetRequestTests.GraphQlGetWithQuery_ShouldBeRejected_WhenTheRequestPrefersHtml`
  pins it.
- **Rate limiting on the two no-JWT edges**: `AuthGrpcService` (SignUp + Login) and
  `ReservationsGrpcService` (ReserveGift, the share-token guest surface) each carry one
  `RequireRateLimiting` policy (`GatewayGrpcEndpointRouteBuilderExtensions`), defined in
  `GatewayInfrastructureServiceCollectionExtensions.AddRateLimiting`. Both are partitioned by
  remote IP address — the only thing available to key on, since neither endpoint ever carries a
  JWT (a share-token guest and a not-yet-registered sign-up both look like "an anonymous caller
  from one address" to this service; a signed-in caller reaching `GiftListsGrpcService` is
  unaffected — that endpoint's `RequireAuthorization()` is the control that matters for it, and
  was not given a rate-limit policy here). Limits are fixed-window, per minute, no queueing
  (reject immediately rather than hold a connection open): 5/minute for the auth edge (blunt
  credential stuffing / sign-up spam against a demo with no CAPTCHA), 20/minute for the
  reservation edge (a guest genuinely working down a list is normal traffic; this is sized to stop
  a scripted sweep, not a person). Known, accepted limitation: a rejected request gets a plain
  HTTP 429, not a grpc `RESOURCE_EXHAUSTED` status — `Microsoft.AspNetCore.RateLimiting` sits
  below grpc-web's own status/trailer framing, the same layering problem `Program.cs`'s CORS
  comment describes for exposed headers; wiring a real grpc status through would need a
  rate-limiting grpc `Interceptor` instead of this middleware, deferred.
- **Secrets review**: no secret is committed in this repo. `appsettings.Development.json` carries
  only the dev CORS origin (not a secret); the JWT public key, Mongo/RabbitMQ connection strings
  and everything else config-shaped comes from environment variables at runtime (devenv's compose,
  which is where any real secret-shaped value — none currently — would need to live, gated by
  `.gitignore`/`.env.example` conventions that repo owns).
- **Validation audit**: this repo carries no free-text field of its own to bound — `RecordGiftItemAddedRequest`
  et al. are pure DTOs into a read-model projection built from GiftLists' `GiftItemAddedV1`, itself
  the corrected wire shape after `giftlist-giftlists`' own GL-44 fix (URL scheme allow-list,
  `Description` length cap — see that repo's own PR). This repo trusts the contract by
  construction (ARCHITECTURE.md "Consuming other services' events: anti-corruption layer" — the
  contract type is what's translated, not re-validated field by field a second time here).

## What does not work yet, and whose job it is

The local folder feed, this repo's `nuget.config` (GL-26) and the compose mounts that make the
feed visible inside every container (GL-29) have all landed — see the nuget.config's own comments
for the packageSourceMapping reasoning (dependency confusion against nuget.org's unrelated
`BuildingBlocks` and `Identity.Contracts` packages) and for why one mount path,
`- ../local-feed:/local-feed:ro`, now serves every service, .NET or web, with no
container-specific path or symlink to reconcile. What's still missing:

| Missing | Issue |
|---|---|
| Semantic versioning discipline and consumer pinning | GL-27 |
| `make pack-all` (dependency-ordered, refuses to overwrite a version already in the feed) and `clone-all.sh` | GL-28 |
| Per-repo CI (build and test only; there is nowhere to publish to) | GL-30 |

Restore and build normally:

```
dotnet restore <Solution>.sln
dotnet build <Solution>.sln --no-restore
dotnet test tests/*.UnitTests/*.UnitTests.csproj
```
