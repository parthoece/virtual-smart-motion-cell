# Compatibility policy

- Stable REST routes and shared contracts follow semantic versioning.
- Additive fields are allowed in minor releases.
- Removed or behavior-changing fields require a major release and migration guide.
- Recipe schemas include an explicit schema version.
- Adapter SDK breaking changes require one release of deprecation when feasible.
- Runtime data migrations must be reversible or include a verified backup step.
