# Release checklist

- [x] Ham accepts the documented “Rewind” naming risk and authorizes Apache-2.0.
- [x] Public Agent releases are explicitly authorized unsigned until trusted
  Authenticode signing becomes available; every release includes SHA-256
  checksums and a visible unsigned-binary warning.
- [x] All automated acceptance checks and CI smoke/soak gates pass.
- [ ] The 24-hour soak and representative-host qualification are completed
  before declaring production readiness.
- [ ] Caller-path and Agent-resource reports are attached to the commit.
- [x] `dotnet restore --locked-mode` and Release build pass locally.
- [x] SDK packages and portable Agent artifact are produced locally.
- [x] NuGet packages contain both target frameworks and `Rewind.Sdk` declares matching transitive dependencies.
- [x] A clean consumer project installs only `Rewind.Sdk`, resolves both matching dependencies, and builds.
- [x] Matching `.snupkg` symbol packages are produced for nuget.org.
- [x] The Agent ZIP contains a self-contained single-file Windows x64 executable and operating documentation.
- [x] End-user guides cover SDK integration, Agent configuration, Windows Service operation, troubleshooting, upgrades, removal, and source builds.
- [x] SBOM and SHA-256 checksums are produced locally.
- [x] Direct and transitive dependencies report no known vulnerabilities from the configured NuGet sources on 2026-08-02.
- [x] Clean-directory Agent startup verification passes locally.
- [ ] Incident and configuration schemas validate their examples.
- [x] Known limitations and upgrade notes match the public-alpha artifact.
- [ ] Artifacts are malware-scanned before publication.
- [ ] Agent artifacts are Authenticode-signed after trusted signing becomes
  available.
- [x] Ham explicitly authorized the unsigned stable `v0.1.0` public release.
