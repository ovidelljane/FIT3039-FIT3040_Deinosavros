# Development tools and verification evidence

Keep source generators, editor tests, handoff documentation and concise verification reports in Git. Unity runtime assets and their `.meta` files remain under `Assets`.

Generated screenshots, frame recordings, raw timing samples and diagnostic dumps are local evidence, not required game assets. They are excluded from Git. The existing raw delivery captures were archived outside the project during the `mapv2` cleanup; historical screenshot paths in handoff reports describe those archived captures, not files included in a fresh clone. Retained test reports describe the runs on their recorded dates, not a new validation run.

Use the maintained documents under `Assets/MapReview/Docs`; duplicated delivery copies have been removed. Do not delete `Assets/MapReview` as a temporary folder: the production Map references its environment models, materials, shaders and graph.
