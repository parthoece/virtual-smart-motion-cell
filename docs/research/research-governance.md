# Research governance

## Proposal requirement

New environments, taxonomies, official metrics, or public datasets require a research proposal describing:

- research question and novelty;
- model or scenario effect;
- observable symptoms;
- oracle-label generation;
- metrics and baseline expectations;
- determinism and provenance;
- leakage and synthetic-to-real risks;
- safety, security, privacy, and ethics implications;
- compatibility impact.

## Review roles

A publication-grade change should receive review from at least two relevant perspectives, such as controls/equipment, data/ML, networking/security, or reproducibility/tooling.

## Immutable releases

Published benchmark releases, taxonomies, schemas, splits, and reference results are immutable. Corrections require a new version and migration note.

## Dataset releases

Large datasets are not merged into the source repository. A release should use a permanent archive, checksums, a dataset card, license, schema version, provenance, and access/privacy classification.
