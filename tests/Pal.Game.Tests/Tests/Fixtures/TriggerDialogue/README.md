# Artificial trigger dialogue collection

These are manually authored, redistributable test inputs, not extracted game data.

- `index.json` lists the entire artificial array: 396 addresses (`0000..018B`), 3168 declared source bytes, a deliberately fictitious all-`a` source hash.
- `shared.json` retains address 0 and raw padding `0001..0187`. Its last `next` crosses to `scene-004#L0188`; selecting the raw padding must reject.
- `scene-004.json` preserves the independently specified shape: upper/portrait59/color0/rng0, message61, message62, end keep. Owner60 and address0188 are metadata only, not copied source records.
- The default language has two body lines (`AB`, `A`), English one (`B`). Both resolve the same 61..62 range; punctuation selects synthetic prompt frames.
- Native font/portrait/prompt inputs reuse the existing #23 artificial A/B/colon block bitmaps and one-pixel palette images. They contain no Unifont/ISO extracted glyphs, real dialogue, RGM image or SLF content.
- Tests copy this set to their own GUID `Assets/RuntimeTests` directory for controlled missing/invalid/no-wait variants, then remove only that directory in the normal fixture lifetime. Nothing prepares, repairs or overwrites existing real resources.
