ASHEN VOXEL — AI HANDOFF DOCUMENTATION OVERLAY — 2026-10-27

This is documentation for the already-delivered Exploration Unity project.
It is not a second project or a gameplay update.

INSTALL
1. Finish extracting the full AshenVoxel Exploration project download.
2. Extract this documentation ZIP beside it and MERGE the AshenVoxel folders.
   Allow replacement of documentation files. Do not delete the existing project.
3. Read MEMORY.md, TODO.md, then Docs/AI_HANDOFF.md.

The overlay contains Markdown/text documentation and JSON evidence only.
Assets, scripts, shaders, settings, packages and generated world data are unchanged.
Relative code/image links resolve after merging into the complete project.
For a new chat, attach the project or grant access to it as well as these notes.
Documentation alone does not give a chat access to the actual Unity sources.

READING ORDER
AGENTS.md                  Project rules and agent entry point
MEMORY.md                  Fast current-state summary, requirements and limits
TODO.md                    Priorities, acceptance criteria and finished baseline
Docs/AI_HANDOFF.md         Detailed context, contracts, validation and recovery
Docs/FILE_MAP.md           Direct route to code, evidence and research
Docs/SESSION_LOG.md        Major milestones and important corrections
README.md                  User-facing project launch instructions
Docs/releases.json        Exact download, hash, versions and documentation scope

CHECKSUMS
HANDOFF-MANIFEST.sha256 lists the files delivered by this overlay.
The original MANIFEST.sha256 still describes the frozen gameplay ZIP. Its
documentation entries naturally differ after this update. Keep it as historical
release provenance; do not pretend the old ZIP contains these later notes.

CRITICAL BOUNDARIES
Native Unity import/HLSL/play/FPS and Windows launcher remain unverified.
The current terrain is a height proxy, not global streamed 10cm destructible matter.
Whole world source is learned1280m coarse; starter source is decoded5m;
near terrain mesh is0.5-1m; props/local matter are10cm. These are distinct.
The newest requested Avroid video was inaccessible. Offline previews are labelled.

NEXT CHAT PROMPT
Continue AshenVoxel from the attached project. Read AGENTS.md, MEMORY.md,
TODO.md, Docs/AI_HANDOFF.md and Docs/FILE_MAP.md first. Follow my latest request,
preserve existing work, GUIDs,0.1m matter and source identities, and distinguish
completed code, executed checks and unverified native behavior. Older recovery
logs and plans are historical. Update memory/TODO/session log at the next checkpoint.
