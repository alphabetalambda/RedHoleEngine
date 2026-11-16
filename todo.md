# Raytracer Next Steps

- Add a more robust denoise (bilateral/temporal) and a toggle for using last-frame history
- Add accumulation persistence controls (sample cap, auto-reset on scene mutation)
- [x] Add a simple light list and direct lighting for better grounding (`LightComponent` + `LightSystem`; directional/point/spot with BVH shadow rays, key/fill/rim fallback when a scene has no lights)
