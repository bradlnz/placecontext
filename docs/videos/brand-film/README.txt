PlaceContext platform film — 16 November 2026

Deliverable: website/assets/platform-film.mp4
Poster: website/assets/platform-film-poster.jpg
English descriptive captions: website/assets/platform-film.vtt
Website player: /#demo (native controls, no autoplay, preload none)

The website and film use the exact alpha mask from the portal logo at
src/PlaceContext.Host/wwwroot/images/corgi-outline.png, in white without a tile.

Current screen footage covers the dashboard, workflow picker, visual canvas,
step catalogue, AI review, retries and timeouts, conditional parallel branches,
YAML editing, scheduling, specialist agents, and inspection of a recorded result.
All screens are recordings of the real local application with fictional enquiry data.
The YAML comment was an unsaved editor demonstration; it was not deployed.
No workflow configuration was saved, no new workflow was run, and no external
messages were sent for this film.

The running-workflow shot uses the successful 15 November execution captured in
dist/demos/enquiry-routing/raw/enquiry-routing.webm. The browser replay and result
show that same historical run, viewed in the current UI. All three are dated on
screen. The current definition includes subsequent changes; this film does not
claim that those changes passed in the older run. Original run evidence is in
../enquiry-routing/capture-evidence.json.

Higgsfield Marketing Studio generated the abstract opening animation. A monochrome
threshold removes its glow to preserve the solid black-and-white direction.
Higgsfield Sonilo Music generated the instrumental score. Seed Audio initially
returned status nsfw with no output for the instrumental request; that result was
not used. higgsfield-assets.json records prompts, model names, statuses and URLs.
The generated animation and music were assembled with real footage using FFmpeg.
No application screens, workflow statuses, or outcomes were generated.

edit.json records every source interval. capture.json records fresh browser routes
and timestamps. export-checks.json records codecs, frame size, duration and full
decode validation. Browser playback checks are recorded in playback-check.json.
Raw recordings, title frames, intermediate clips and the assembly script remain in
dist/demos/brand-film/. The final video is stored once, in website/assets/.

Hero background: website/assets/workflow-execution-loop.mp4 is a silent 12-second
loop cropped from the actual 15 November execution. hero-loop.json records intervals
and the crop. The page has a date label and pause/play control, and respects
reduced-motion and Save-Data preferences before downloading the video.
