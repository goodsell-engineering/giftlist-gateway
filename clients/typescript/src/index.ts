/**
 * The Gateway's public TypeScript surface: everything protoc-gen-es emits for this repo's
 * protos/*.proto, re-exported from one entry point.
 *
 * `export *` rather than a hand-listed set, deliberately. The alternative is a list that has to
 * be edited every time an RPC or message is added to a .proto, and the failure mode of forgetting
 * is that a consumer cannot see a type that is definitely in the package — reported as "the
 * tarball is stale" and debugged against the wrong thing. The generated modules export nothing
 * that is not already part of the wire contract, so there is no narrower surface worth curating.
 *
 * The ".js" specifiers below are correct and are not a mistake to "fix": TypeScript never
 * rewrites a module specifier on emit, so an extensionless one would survive into dist/index.js
 * and Node's ESM resolver — which is what vitest and any non-bundler consumer use for a package
 * under node_modules — would fail to resolve it. A bundler papers over that, so the defect shows
 * up only in giftlist-web's test run and not in its dev server.
 *
 * Name collisions between the two .proto files would surface here as a compile error in this
 * package rather than as ambiguity in giftlist-web. Today there are none: each file has its own
 * proto package (giftlist.identity.v1, giftlist.giftlists.v1) and protoc-gen-es derives the
 * TypeScript names from the message names, which do not overlap.
 */
export * from "./gen/identity_pb.js";
export * from "./gen/giftlists_pb.js";
