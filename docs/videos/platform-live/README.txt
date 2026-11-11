PlaceContext live platform demonstration

Open preview.html locally to play the finished video.

Deliverables
  placecontext-demo-branded.mp4 — 1920 × 1080, 30 fps, 2:40; branded shell and captions.
  placecontext-demo-clean.mp4   — 1600 × 900, 30 fps, 2:31; footage without shell or title cards.
  northstar-onboarding-brief.pdf — Actual PDF downloaded from the recorded run's Artifacts preview.
  poster.png                   — PDF scene from the finished branded video.
  brand-shell.html             — Editable shell using the existing PlaceContext logo.

The recording shows a new Northstar Customer Success project with five fictional
accounts. A configured agent creates and saves Account onboarding in chat. The
saved workflow is reopened and run, including Agent in the loop and Human in the
loop. After all six stages succeed, the video visits Data and opens the resulting
PDF in Artifacts. Three automatic workflow agent steps used Codex gpt-5.6-luna.
The human approval was submitted through browser automation for this demo.

Three accounts qualified and two were held. The one-page PDF contains the eligible
accounts, held accounts and reasons, independent review, and next steps. The PDF
job refuses input without approval or a passed review. No account data changed
and no external communications were sent.

The video is silent. Chronological cuts remove idle time and authoring refinements.
Selected authoring and execution scenes play at labelled 2× or 2.5× speed; other
scenes play at normal speed. The agent corrected draft saves while authoring. A
second chat request refined PDF layout before the single workflow run; these
refinements are omitted from the edit. No application UI or outputs were fabricated.

Evidence and editing
  capture-evidence.json — Application revision, run IDs, PDF hash, timing and verification.
  workflow-output.json — Actual final context read from the application.
  export-checks.json   — Dimensions, durations, frame counts and full decode checks.
  playback-check.json — Chromium playback and seeking checks.
  edit.json            — Selected source intervals, speeds and chapter captions.
  timeline.json        — Timestamps recorded during the live browser capture.

The running application revision is e70904d5. The repository was at 79d2b609 when
recorded. Project, workflow, run and artifact IDs refer to the local demo database;
this package does not recreate that database in a new checkout. The job emitted
its PDF through both an output file and a JSON artifact envelope, so Artifacts
shows two grouped versions of the same PDF from the single job run.

The raw recording, editor script, rendered shell frames, PDF source and intermediate
clips remain in ignored dist/demos/platform-live-v2 on the capture machine. They
are not included in this delivery package. To re-export on that machine, run:
  python3 dist/demos/platform-live-v2/edit_video.py

The MP4s and preview package can be copied together. Editing brand-shell.html
requires the repository's website/assets/logo.svg at its existing relative path.
