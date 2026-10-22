# Historical acceptance evidence

Records below this directory describe execution on the source revision or hashes, tool versions and
machine identified in each record. Old paths describe those runs; the later sample/fixture separation
does not relocate or reinterpret them. They are not current setup instructions or proof for a later commit.

Start with the [documentation map](../README.md) for current guidance and status, or the
[milestone acceptance matrix](../19-milestone-1-acceptance.md) for A01–A18 evidence.
Later changes have their own dated records, including [CI selection](proportional-ci-2026-10-20/README.md),
[test helpers](integration-helpers-2026-10-21/README.md), [minimal applications](minimal-application-2026-10-21/README.md),
the [development startup helper](development-startup-2026-10-21/README.md), including actual source/package editor acceptance,
[watcher resource ownership](watcher-owner-2026-10-21/README.md) and the
[development-experience review](development-experience-review-2026-10-21/README.md).
The selected D1 implementation has its own [startup-error record](startup-errors-2026-10-21/README.md),
including process-level negatives and actual source/package editor failure/recovery checks.

The [fixed-suite execution record](test-suites-2026-10-22/README.md) retains local xUnit selection,
startup-observer evidence and the actual failed-child/required-gate proof for PR #46.

Keep existing evidence links, provenance, first failures and unexecuted checks intact. This documentation
cleanup neither deletes historical artifacts nor rewrites Git history. Apply the
[retention policy](../21-continuous-integration.md#evidence-retention-policy) to new evidence: concise durable
records belong in Git; routine detailed output belongs in expiring CI artifacts. A dead artifact link alone
cannot support an enduring acceptance claim.
