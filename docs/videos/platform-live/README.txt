PlaceContext live platform demonstration

Open preview.html locally to play the finished video.

Deliverables
  placecontext-demo-branded.mp4 — 1920 × 1080, 30 fps, 1:51; branded shell and captions.
  placecontext-demo-clean.mp4   — 1600 × 900, 30 fps, 1:42; footage without the shell or title cards.
  poster.png                   — Frame from the finished branded video.
  brand-shell.html             — Editable shell source using the existing PlaceContext logo.

The recording shows a fresh Northstar Onboarding project with five fictional
accounts. Chat starts a saved workflow: query data, research, independent review,
human approval, and an internal handoff. Three automatic agent steps used Codex
gpt-5.6-luna. Approval was submitted through browser automation for this demo.
All five steps succeeded; three accounts qualified and two were held. No account
data changed and no external communications were sent.

The video is silent. Chronological cuts remove idle time; the live model execution
scene plays at a labelled 2.5× speed. Other selected scenes play at normal speed.

Evidence and editing
  capture-evidence.json — Application revision, run IDs, timing and verification.
  workflow-output.json — Actual final output read from the application.
  export-checks.json   — Dimensions, duration, frame counts and full decode checks.
  playback-check.json — Successful Chromium playback check.
  edit.json            — Selected source intervals, speed and chapter captions.
  timeline.json        — Timestamps recorded during the live browser capture.

The application revision is e70904d5. The recorded project and run IDs refer to
the local demo database; they do not recreate that database in a new checkout.

The original recording, editor script, rendered shell frames and intermediate
clips remain in ignored dist/demos/platform-live on the capture machine. They
are not included in this delivery package. To re-export on that machine, run:
  python3 dist/demos/platform-live/edit_video.py

The MP4s and preview package can be copied together. Editing brand-shell.html
requires the repository's website/assets/logo.svg at its existing relative path.
