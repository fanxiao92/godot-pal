# export-map v1

Map-only offline conversion ([Issue #8](https://github.com/fanxiao92/godot-pal/issues/8)); not scene execution or M2 rendering.

```powershell
dotnet run --project tools/Pal.Extract -- export-map `
  --input data/raw/sdlpal --output data/extracted/m1-map-12 `
  --map 12 --palette 0 --lighting day
```

All five options are required, in any order. Map IDs are positive, palette IDs nonnegative; lighting is `day` or `night`. The example explicitly selects palette 0/day, **not** a palette inferred from map/scene ID or a claim about the palette after entry scripts. Only MAP/GOP/PAT are consumed. Names are case-insensitive; ambiguous names fail. Windows MAP is YJ2; YJ1 is explicitly unsupported. GOP and PAT are raw.

Output must be new or empty and outside input, including after normalizing `..`. Directory links/reparse points anywhere along input/output paths, and linked required archives, are conservatively rejected rather than followed. Existing data is never cleared. Input is read-only. Data validation completes before creating output; an I/O failure while writing can leave an incomplete **new** output, which must be inspected rather than automatically overwritten. Paths should not be concurrently changed by another process during export.

Exit codes: `0` success, `1` input/data/output error, `2` argument error. Success is silent; failures use stderr without stack traces, local paths or partial stdout reports. JSON/PNG output is deterministic for the same input, options and installed .NET runtime.

## Files and coordinates

All JSON roots have `formatVersion: 1`. IDs and offsets are original, not renumbered.

- `map.json`: `mapId`, `width: 64`, `height: 128`, `halfRows: 2`, `cellOrder: "[y,x,h]"`, `decodedByteLength: 65536`, source/coordinate/preview conventions and 16,384 `cells`. Each cell has `y`, `x`, `h`, unsigned `raw` DWORD, `bottomFrame`, nullable `topFrame`, `bottomHeight`, `topHeight`, `blocked`.
- `palette.json`: `paletteId`, requested `lighting`, actual `selectedLighting`, 256 RGB `colors`, source. PAT components convert with `<< 2`; a night request on a day-only PAT retains the request but selects day colors, as in `palette.c`.
- `tiles.json`: `mapId`, GOP source and `frames` in ascending index order. Each frame has original `index`, chunk-relative byte `sourceOffset`, `width`, `height`, relative `png`, row-major base64 `indices` and independent byte `alpha`. Opaque palette index 0 is preserved. Exports nominal frames plus any explicitly referenced valid extra table entry; a terminal offset is not a frame.
- `tiles/<index>.png`: lossless RGBA, with the selected palette; no metadata/timestamps.
- `preview.png`: 2048×2048 viewport at map pixel origin `(0,0)`, not a cropped or relocated scene.

Per `reference/sdlpal/map.c` (`PAL_MapGetTileBitmap`, `PAL_MapGetTileHeight`, `PAL_MapTileIsBlocked`), each DWORD's lower/upper WORD holds bottom/top information. Frame bits are 0–7 plus bit 12; top encoded 0 means absent, otherwise subtract one. Logical height is bits 8–11 of each WORD; blocked is DWORD bit 13. Raw bits remain available even when not interpreted.

The bitmap top-left is `(x*32+h*16-16, y*16+h*8-8)`. Logical height controls sprite occlusion, not bitmap displacement. Preview reproduces `PAL_MapBlitToSurface`'s order **layer (bottom then top), y, h, x**, which differs from stored `[y,x,h]` order. It includes that function's out-of-grid bottom fallback to cell `[0,0,0]`, absent top outside the grid, and viewport clipping. Layer ordering also follows `scene.c`'s two initial map passes. No scene objects, scripts, palette animation, wave effects or subsequent sprite-occlusion passes run.

## Bounds and regression seam

The shared `MkfArchive` extracts the existing inspector's complete offset validation without changing its report/errors; bounded `ReadChunk` is reused by export consumers. Map export caps tables at 65,536 chunks, each MAP/GOP chunk at 8 MiB, PAT at 1,536 bytes, decoded MAP at exactly 65,536 bytes, nominal GOP frames at 4,096, aggregate tile pixels at 16 Mi pixels, and conservative preview pixel visits at 128 Mi. Limits reject resources rather than truncate them.

`ExportMapCliTests` invokes the compiled CLI with artificial MKFs and independently expected fields/RGBA pixels, including overlapping draw-order examples. Component public-API tests additionally cover compression, RLE, palette and PNG byte-level boundaries. Real-data validation is local evidence in Issue #8, not a dependency of CI. Whole-package identity/reference manifests and final M1 usage documentation belong to #10.
