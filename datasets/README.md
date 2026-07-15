# Generated datasets

Generated research bundles are intentionally not committed to Git. They contain synchronized observable data and separate oracle ground truth.

Canonical formats:

- Parquet for typed analytical tables;
- CSV as a flattened compatibility export;
- PCAPNG for offline wire-format EtherCAT packet evidence;
- JSONL for streaming runtime logs;
- YAML/JSON for manifests and provenance.

Use `datasets/examples/` only for small reviewed fixtures. Large releases should be archived separately with checksums and a persistent identifier.
