# Limits for untrusted .gost input

Opening a `.gost` file treats both the ZIP container and `document.json` as
untrusted input. The reader rejects the package before materialization when a
hard limit is exceeded.

The same structural, string, image and geometry limits are checked before the
writer creates a ZIP. The final encoded manifest length is checked after JSON
serialization. A path-based save still performs both stages inside its atomic
temporary file, so a limit error cannot publish a partial destination.

Current limits are defined in `GostArchiveLimits`:

- raw archive on every core load path, and the UI fingerprint buffer: 512 MiB;
- ZIP entries: 2,048, with names up to 1,024 characters;
- central directory: 16 MiB, validated before `ZipArchive.Entries` is used;
- total declared uncompressed ZIP data: 512 MiB;
- manifest: 16 MiB, JSON depth 64 and 2,000,000 tokens;
- one image: 64 MiB; all materialized images: 256 MiB;
- every decodable image: at most 16,384 pixels on either axis and
  40,000,000 pixels in total;
- 100,000 paragraphs, 1,000,000 runs and 1,000 images;
- 10,000 code listings and 100,000 bibliography sources;
- one JSON string: 16,000,000 characters; one numeric literal: 256 bytes;
- finite, bounded page, paragraph, image-placement and font dimensions.

The core reader bounds non-seekable streams before parsing and preflights the
standard single-disk central directory before `ZipArchive` materializes entry
objects. ZIP64 and multi-volume packages are outside the supported `.gost`
profile. The entry reader checks declared lengths and also counts bytes while
copying. A separate image budget counts the actual returned byte arrays, never
only the untrusted central-directory lengths. Duplicate entries, overlong
names, null collection elements and inconsistent image references are invalid
data. Nullable legacy string values are normalized to empty strings before the
document is published.

Legacy v0/v1 compatibility remains intentional. Values that existing migration
code normalizes (for example a non-positive legacy image placement size) are
still allowed through validation and normalized by that migration. Limits are
security boundaries, not automatic repair rules for the current v2 format.

These limits bound parser and managed-memory exposure. A UTF-8 lexical preflight
rejects oversized numeric tokens before Newtonsoft.Json can parse them as large
integers. They do not claim to make ZIP parsing immune to every CPU-based denial
of service. Image headers are probed without rendering before images reach UI
decoding. The UI buffers the selected file only through the same bounded-read
contract so fingerprinting cannot allocate without limit before the core reader
runs.
