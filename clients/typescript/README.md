# `@giftlist/gateway-client`

The generated TypeScript grpc-web client for the Gateway, built from `../../protos/*.proto`.

The Gateway owns the `.proto` files and publishes this client as an npm package
(ARCHITECTURE.md "Repository layout"); `giftlist-web` consumes it. That is why the generator
lives here and not in `giftlist-web`, where it used to reach across the monorepo at
`../gateway/protos` — after the GL-25 split there is no such path.

One file, two generators, never drifting: `Grpc.Tools` generates the C# server stubs for
`Gateway.Infrastructure` straight from the same `.proto` (see `Gateway.Infrastructure.csproj`'s
`<Protobuf>` items), and `buf generate` here produces the browser client.

```
npm install
npm run build                                  # buf generate + tsc
npm pack --pack-destination ../../../local-feed
```

`prepack` runs `npm run build`, so `npm pack` can never ship a stale client.

`npm pack` by hand is the fallback. The dependency-ordered version is `make pack-all` in
`giftlist-devenv` (GL-28), which also refuses to overwrite a version already in the feed —
ARCHITECTURE.md "Packaging: local feed" explains why that guard matters more than it looks.
