# Raytracer Next Steps

- [x] Temporal denoise with last-frame history toggle (gravity-gated, never runs under lensing; see docs/TEMPORAL_DENOISE.md). Still TODO: GPU runtime verification, wire SVGF spatial/variance stages, bilinear history fetch
- Add a more robust spatial denoise (SVGF spatial / A-trous are scaffolded but not wired)
- Add accumulation persistence controls (sample cap, auto-reset on scene mutation)
- Add a simple light list and direct lighting for better grounding
