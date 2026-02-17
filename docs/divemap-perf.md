# DiveMap Performance Optimization Notes

## Baseline (before optimization)

FPS drop: 60 → 50~55 with DiveMap enabled.

Profiling via Stopwatch instrumentation in `DiveMapBehaviour.Update()`, logging every 5s:

| Phase | Avg per frame | % of total | Description |
|-------|--------------|------------|-------------|
| cam | 0.007ms | <1% | Set camera position/orthoSize |
| **layout** | **0.34ms** | **31%** | `ApplyLayout()` → `SetMarkerSizes()` iterating 1008+ markers every frame |
| **scan** | **0.50ms** | **45%** | `ScanEntities()` with `FindObjectsOfType`, ~13ms per invocation (every 0.5s) |
| player | 0.006ms | <1% | `UpdatePlayerMarker()` |
| **markers** | **0.27ms** | **24%** | `UpdateMarkerPositions()` iterating 1000 markers, mostly `SetActive(false)` |
| **total** | **~1.1ms** | — | CPU-side only |

Key finding: CPU-side 1.1ms/frame only accounts for ~6.6% of 16.7ms frame budget.
The dominant bottleneck is **Camera→RenderTexture GPU rendering** (renders entire scene a second time).

Evidence: disabling DiveMap recovers ~6 FPS (51→57), but CPU savings alone can't explain this.

## Round 1: CPU Optimization (implemented)

### Changes

1. **SetMarkerSizes only on mode switch** — added `_lastBigMap` dirty flag; `SetMarkerSizes()` only called when mini↔big toggles, not every frame.

2. **Reduced hidden marker traversal** — added `_prevEscapeIdx` / `_prevEntityIdx` to track previous frame's active count. Only hide markers in `[currentIdx, prevIdx)` range instead of iterating all 1000.

3. **Scan interval 0.5s → 1.0s** — static entities (chests/items) don't need high-frequency scanning.

### Results

| Phase | Before | After | Change |
|-------|--------|-------|--------|
| total | 1.1ms | 0.47ms | **-57%** |
| layout | 0.34ms | 0.01ms | **-97%** |
| scan | 0.50ms | 0.23ms | **-54%** |
| markers | 0.27ms | 0.22ms | -19% |

CPU overhead halved. Layout essentially eliminated. But FPS still fluctuates (32~60), confirming GPU is the real bottleneck.

## Round 2: GPU Optimization (implemented)

### Changes

1. **Lower RenderTexture resolution** — reduced from 512 height to 256 height. Map looks slightly blurrier but perfectly usable for a minimap. Configurable via code constant.

2. **Camera frame skipping** — map camera doesn't render every frame. Renders once every N frames (default: 3, i.e. map updates at ~20 FPS). Imperceptible for a minimap; slight stutter on big map during fast movement but acceptable.

### Trade-offs

- Lower resolution: slightly blurry, no functional impact
- Frame skipping: map updates at reduced FPS, barely noticeable on minimap, slight lag on big map during fast player movement

## Future options (not yet implemented)

3. **Camera culling mask** — only render specific Layers (skip particles, UI, effects). Requires investigating which Layers the game uses; risk of accidentally hiding terrain. Higher effort, uncertain payoff.
