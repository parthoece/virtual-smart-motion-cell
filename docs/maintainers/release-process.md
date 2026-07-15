# Release process

1. Confirm the milestone is complete and all required checks pass.
2. Update `VERSION`, `CHANGELOG.md`, `CITATION.cff`, and compatibility notes.
3. Run the development check and extended reliability campaign.
4. Create a signed tag such as `v0.5.0`.
5. Let the release workflow build self-contained runtime and HMI packages for each RID.
6. Verify checksums, provenance attestations, startup, viewer loading, and one normal/fault scenario.
7. Publish release notes with highlights, breaking changes, known limitations, and contributor credits.
8. Open the next milestone and move unfinished work explicitly.
