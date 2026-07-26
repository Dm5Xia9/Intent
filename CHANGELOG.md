# Changelog

All notable changes to **Intent** / **Intent.Polly** are documented here.

Format inspired by [Keep a Changelog](https://keepachangelog.com/).
Versioning: [SemVer](https://semver.org/).

## API stability

Until **1.0.0**:

- Pipeline order (`IntentPipelineOrder` / docs/03-pipeline.md) and the public surface of `Intent` / `Intent{T}` / `IntentPolicies` / `Intent.Polly` may still change.
- Every breaking change is listed in this file under the release that introduces it.

From **1.0.0** onward:

- Pipeline order and public API are **frozen**.
- Breaking changes require a major version bump and an explicit CHANGELOG entry.

## [Unreleased]

### Changed

- `int.Seconds()` / `int.Milliseconds()` moved to namespace **`Intents.Time`** (`using Intents.Time;`). No longer pollute the global `Intents` namespace.
- `Intent` / `Intent{T}` share internal `IntentPlan` helpers so Configure / Schedule / SM-clone stay aligned.
- Metrics use `System.Diagnostics.Metrics` via `IntentInstrumentation` (ActivitySource/Meter name `Intents.Intent`).
- Resilience lives in package **Intent.Polly** (Polly v8).
- Idempotent / Cache are implemented only through `IntentPolicy.Wrap`.

### Added

- `IntentPipelineOrder` — documented frozen order constants (pre-1.0 may still move with CHANGELOG).
- `Intent.Analyzers` — Roslyn warnings: discarded Intent without `Background()`, configure-after-await, Intent expression not awaited.
- `Intent<T>.Into(Channel<T> / ChannelWriter<T>)` — background run that writes the result into a channel; sample: `examples/Intent.ChannelPipeline`.
- `Channel.FromEach` / `ChannelReader.FromEach` — background consumer that awaits an Intent per item (required `CancellationToken`; optional `into:` writer).
- Observability guide: docs/10-observability.md; sample: examples/Intent.OtelSample.
- SM-clone contract: docs/09-sm-clone-contract.md.

## [0.3.0] — 2026-07-26

Initial documented 0.3 line: cold Intent, policy pipeline, Intent.Polly adapter, OTel metrics/activity.
