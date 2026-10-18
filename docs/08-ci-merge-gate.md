# Upstream CI merge gate

Date: 2026-10-18. Validation in progress on a separate PR targeting `sotsera/spar:main`.

The intended required check is **Windows managed probe**, produced by GitHub Actions. The existing upstream `main` run passed; this PR verifies that upstream also evaluates fork pull requests and that a failed check prevents merging.

The initial validation commit deliberately changes the console greeting to an invalid `return;` statement. The PR stays a draft until the real upstream failure has been observed and the required-check rule is enabled. The invalid change will be restored before this PR is ready to merge. No failing commit will be merged into `main` as part of this test.

Remaining observations:

- Observe the real upstream PR build failure and its check identity.
- Add that check to `protect-main`, preserving the other rules and the empty bypass list.
- Mark the validation PR ready and verify its failed required check blocks merging.
- Restore the original source and verify a successful upstream PR run permits merging.
- Record the exact rule, run links, and limits here and in the milestone checklist.
