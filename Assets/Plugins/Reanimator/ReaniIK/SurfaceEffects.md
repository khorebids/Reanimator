# Surface effects in ReaniIK

The green Scene handles include **Tremor outline**, **Redness**, **Sweat**, and **Darkness**, each from 0 to 1. The same values are serialized under `surfaceEffects` and can be animated. Darkness 1 produces black without changing sprite opacity. Tremor emits 10–20 sparse groups (15 by default) of 2–3 sharp, rapidly flashing needles behind body, arm, leg, face, head and neck sprites. Each group samples its own surface texture color and follows redness/darkness. It does not displace the sprite mesh or cover the entire contour. Width is relative to character height; group count and speed are adjustable.

Use **Paint redness / surface normals** in the ReaniIK Inspector. Choose a sprite and paint its redness mask. Shift or Erase removes redness. Masks save beside the source PSD as a `.surfaces.asset` library and PNGs. Front and rear arm copies share the same sprite entry. Cooking a prefab reconnects its existing library.

- RGBA texture: **RGB** is an ordinary tangent-space normal encoded from -1..1 into 0..1; **A** is redness strength. Import as linear RGBA data, not Unity's compressed Normal Map format.
- Coverage PNG: sprite opacity used to constrain sweat spawning and movement.
- Initial normals approximate convex surfaces from the silhouette. **Import normals RGB** accepts a normal map produced by a ComfyUI image-to-normal workflow and preserves the painted alpha. **Generate base normals** also preserves alpha.
- Sweat uses a shared pool of at most **20** drops per rig, follows surface normals and the current bone orientation, and deposits fading wet tracks in sprite coordinates. It stays attached under skinning. Hair and accessories are excluded from sweat by default; the painter exposes a per-sprite toggle.

All effects default to zero. Missing masks produce no redness or sweat. Runtime materials, trail textures, and outline meshes are transient and released when the rig is disabled or saved.

Verification: `ReaniIKSurfaceEffectsChecks.Run("Temp/ReaniIKSurfaceEffects.png")` renders the shader, checks the black endpoint and opacity, alpha preservation during normal generation, sprite UVs, an exterior contour, the 20-drop limit, and material cleanup. `ReaniIKArmMaskChecks.Run` verifies compatibility with arm masking.

GPU skinning regression: ReaniIKSurfaceSkinningChecks.Run(prefab, outputPath) compares the effect shader with the stock URP shader before/after a 35-degree bone rotation through the real deformation manager. Effects use private materials, never MaterialPropertyBlock, to preserve SRP batching and SpriteSkin bone offsets. Legacy effect blocks are removed automatically on enable.
The GPU regression runs asynchronously: Run returns Running; poll ReaniIKSurfaceSkinningChecks.Status for its result after native frame updates.

The green Tremor groups slider now controls quantity from zero to Maximum Tremor Groups. Tremor Peak Size and Tremor Frequency (Hz) are independent Inspector settings. Material sources are serialized per sprite; missing references are recovered from the prefab or Resources/ReaniIKSpriteBase. ReaniIKSurfaceLifecycleChecks.Run() covers repeated toggles, missing-material recovery and size-independent quantity.

Current defaults: 20 groups, frequency 5 Hz, peak height 0.01. Maximum Tremor Groups supports 1–100. CPU benchmark on rig 22 in the Unity Editor (median of five 120-frame batches after warmup): 20 groups 0.4857 ms, 50 groups 0.5733 ms, 100 groups 0.6930 ms; 6 submeshes in all cases. These timings measure CPU mesh preparation/upload, not GPU cost or total frame time.

Mask authoring is embedded in Reanimator → Surface Masks. Select a PSD directly, or choose an optional scene rig. The Inspector paint button redirects to this tab; no standalone painter window is created.

Surface Masks opens in Whole Body — auto sprite mode. The canvas reconstructs the PSD composition and uses the same PreviewNavigator as Skinning Editor: wheel zooms at the pointer, MMB or Alt+LMB pans, F fits. Each brush texel is routed to the top visible sprite using layer depth and source alpha. A stroke can span multiple sprite masks; covered pixels are protected and RGB normals stay intact. RMB erases. Single Sprite remains available for isolated work and per-sprite normal-map import.

Brush input in both canvas modes: LMB paints, RMB erases. Hold Shift and scroll the wheel to change radius. Mouse movement does not change radius; the circle follows the cursor. Shift never paints or erases. The Paint/Erase toolbar and radius slider were removed. MMB and Alt+LMB still pan the whole-body canvas.

Whole-body canvas: Shift+RMB hides the top visible sprite under the pointer locally in the mask editor. It is skipped by drawing and brush hit-testing, exposing the body beneath. The next Shift+RMB elsewhere (including empty canvas) restores it. Renderer/prefab visibility and the hidden sprite's mask are not modified.

Redness now uses Multiply in both the painter preview and runtime shader, preserving albedo detail and source opacity. Blur redness mask in the left panel applies a Gaussian blur to alpha only, with Undo; RGB normals are preserved. Whole Body processes painted, non-hidden sprite masks; Single Sprite processes the selected mask.

Surface Masks now has a right-hand Affected sprites list. Checked sprites receive redness, sweat, tremor and normal previews; excluded sprites retain their source image and cannot be painted or blurred. Existing libraries initially select body, face, head, neck, arms and legs, preserving all stored mask data.

Normals via Comfy uses the local server at http://127.0.0.1:8188 and the same BAE-NormalMapPreprocessor as Krita AI Diffusion: LoadImage -> BAE (512) -> SaveImage (Reanimator/Normals). Generate for the selected sprite or all checked sprites. RGB is converted to sprite tangent coordinates; existing redness alpha is preserved. Results remain under ComfyUI output/Reanimator/Normals. Cancel retains completed sprite maps. The UI reports connection or inference errors without replacing masks with a procedural approximation.
Unchecked sprites are now hidden entirely from both canvases and do not occlude checked sprites. Comfy generation filters checked entries and rechecks their inclusion before queueing inference and applying its result.
Normal strength (0–3) in Sweat normals is a non-destructive library setting: zero flattens normals, one retains original direction, larger values amplify XY relative to Z. Both Show normals and sweat simulation use the adjusted normalized direction; stored RGB and painted alpha remain unchanged. Drops freeze and smoothly fade over 1.2 seconds when they reach a sprite boundary or finish their lifetime; their trails fade with them.
Sweat Size Min / Sweat Size Max control the random radius range for newly spawned droplets, relative to sprite height; equal endpoints give uniform drops and reversed endpoints are sorted safely. Sweat Fade In controls smooth appearance in seconds (default 0.4). Fade-in multiplies fade-out, including drops that reach an edge while still appearing; trail visibility follows the same opacity.

Contrast (−1..3, default 1) and Desaturation (0..1, default 0) follow Darkness in the inspector and floating scene controls. Contrast is applied around 0.5; 1 is unchanged, 0 is flat gray, negative values invert tonal contrast. Desaturation blends toward luminance; 1 is fully grayscale. Both affect every character sprite and the tremor outline, preserve alpha and share the existing SRP-compatible private materials. Darkness is applied last so full darkness remains black for every contrast setting.

Floating slider rows are 20% closer. Each named slider has a small R button before its label: face/effect values reset to 0, Contrast to 1 and Arm Mask to 1. Belly Scale has its own R reset to unit scale. Reset Face remains below the effect stack.
