# GitHub publishing checklist

## Before the first push

Replace:

- `YOUR_GITHUB_HANDLE`
- `YOUR_NAME`
- `SECURITY_CONTACT@example.com`

Then run the repository checks described in [testing](testing.md).

## Create and push

With GitHub CLI authenticated:

```bash
git init
git add .
git commit -m "feat: publish virtual smart motion cell v0.5.0"
git branch -M main
gh repo create virtual-smart-motion-cell --public --source=. --remote=origin --push
```

The helper script `scripts/bootstrap_github.sh` prints the equivalent guarded workflow.

## Repository settings

- Add topics such as `industrial-automation`, `equipment-software`, `dotnet`, `avalonia`, `threejs`, `opc-ua`, `digital-twin`, `virtual-commissioning`, `motion-control`, and `smart-manufacturing`.
- Enable Issues, Discussions, private vulnerability reporting, Dependabot alerts, and security updates.
- Configure a ruleset for `main` that requires pull requests, review, conversation resolution, and the required CI checks.
- Restrict release and workflow changes using `CODEOWNERS` review.
- Add the companion learning-repository URL to the About section.
- Upload a social preview and short demonstration video when available.

## First public validation

Do not describe an operating system as tested until its CI job is green. Confirm:

1. Windows, Ubuntu, and macOS build jobs pass.
2. Executable specifications and integration specifications pass.
3. The Three.js viewer builds from the lock file.
4. The end-to-end workflow starts the runtime, MES simulator, OPC UA endpoint, and WebSocket client.
5. Release packages are produced for the documented runtime identifiers.
6. SBOM and artifact-attestation jobs complete.

## First release

1. Complete [the release process](maintainers/release-process.md).
2. Update `CHANGELOG.md` and confirm `VERSION` is `0.5.0`.
3. Create a signed `v0.5.0` tag.
4. Let the release workflow generate platform archives, checksums, SBOMs, and attestations.
5. Publish release notes with known limitations and links to the evidence matrix.
