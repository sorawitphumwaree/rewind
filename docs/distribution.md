# Distribution and Release Process

## Public distribution model

- Application developers install `Rewind.Sdk` from nuget.org.
- NuGet resolves matching `Rewind.Protocol` and `Rewind.Abstractions` packages.
- The self-contained Agent and its operating files are distributed through GitHub
  Releases.
- GitHub hosts the source repository and release notes.

Direct DLL ZIPs are not public release assets.

## Versioning

The shared product/package version is defined once in `Directory.Build.props`.
SDK, Protocol, Abstractions, Agent artifacts, documentation, tags, and release
notes use the same Semantic Version.

Release tag format:

```text
v0.1.0-alpha.1
```

No tag, NuGet publication, or GitHub Release is created without Ham's explicit
instruction.

## NuGet publication order

Publish in dependency order:

1. `Rewind.Abstractions`
2. `Rewind.Protocol`
3. `Rewind.Sdk`

Each primary package has a matching `.snupkg` for the nuget.org symbol server.
Pushing the `.nupkg` with both files present publishes the primary and symbol
packages together.

Users install only:

```powershell
dotnet add package Rewind.Sdk --version <version>
```

Published NuGet versions are immutable. A defective package is unlisted and
replaced by a new version; it is never overwritten.

## GitHub Release assets

Attach:

- `rewind-agent-win-x64.zip`
- `sbom.spdx.json`
- `SHA256SUMS.txt`

GitHub automatically provides source ZIP and tar archives for the release tag.
NuGet packages are not duplicated as GitHub Release assets.

## Pre-release preparation

1. Complete the release checklist.
2. Confirm `Directory.Build.props` contains the intended version.
3. Run the `release-candidate` GitHub Actions workflow.
4. Download and inspect `rewind-release-candidate`.
5. Verify NuGet metadata, dependencies, target frameworks, and package README.
6. Verify the Agent ZIP, configuration, schema, service scripts, documentation,
   SBOM, and checksums.
7. Malware-scan and Authenticode-sign the Agent executable for a public release.
8. Rebuild the Agent ZIP and checksums from the signed executable.
9. Create the release tag only after explicit owner approval.

## Required repository configuration

NuGet publication uses Trusted Publishing. No long-lived NuGet API key is
created or stored in GitHub.

On nuget.org, create one Trusted Publishing policy owned by the individual
account `sorawitphumwaree`:

- Repository owner: `sorawitphumwaree`
- Repository: `rewind`
- Workflow file: `publish-nuget.yml`
- Environment: `release`

Enter only the workflow file name, not `.github/workflows/`.

In the GitHub repository, create an environment named `release` and require
Ham's approval before deployment. The `publish-nuget` workflow has
`id-token: write` permission so `NuGet/login@v1` can exchange the GitHub OIDC
token for a one-hour NuGet API key. The temporary key exists only for that
workflow run.

Before public Agent publication, configure the selected Authenticode signing
service or certificate as protected release-environment secrets.

## Publication

The NuGet publication workflow is manual and must not be run until:

- the Trusted Publishing policy and protected `release` environment are
  configured;
- trusted Authenticode signing is available;
- the representative-host qualification and release checklist pass;
- Ham explicitly authorizes the tag and release.

The workflow builds and verifies all artifacts, requests its short-lived key
immediately before publication, and pushes the packages in dependency order.
The NuGet username is a public profile name; the account email is not required
by the workflow and must not be added to source or GitHub secrets.

The GitHub Release should be created as a draft, assets attached and verified,
then published only after all three NuGet packages are accepted.
