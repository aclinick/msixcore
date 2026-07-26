# Error codes

`msixmgr --json` reports stable, machine-readable error categories in its `code` field.

| Code | Meaning | Typical trigger |
| --- | --- | --- |
| `zip_structure` | Malformed ZIP structure | Invalid EOCD, central directory, or entry header |
| `part_name` | Invalid or unsafe OPC part name | Traversal, rooted names, encoded separators, NUL, or equivalent names |
| `footprint_missing` | Required footprint absent | Missing `AppxManifest.xml`, `AppxBlockMap.xml`, or `[Content_Types].xml` |
| `content_types` | Invalid content-types semantics | Invalid declaration, duplicate declaration, or uncovered part |
| `block_map_semantics` | Invalid block-map semantics | Duplicate file, inconsistent sizes, ZIP mismatch, or mapped content-types part |
| `manifest_semantics` | Invalid application manifest semantics | Missing identity, bad version or architecture, or missing required element |
| `bundle_semantics` | Invalid bundle semantics | Invalid bundle manifest or package/bundle kind mismatch |
| `signature_format` | Malformed package signature | Invalid P7X/CMS data or digest table |
| `xml` | Unsafe or malformed XML | XML syntax error or prohibited DTD/DOCTYPE |
| `package_store` | Invalid deployment store state | Corrupt commit journal or unsafe staged content |
| `unknown` | Reserved fallback | No more specific category is available |

Category names are the stable contract. The enum deliberately has no assigned numeric values; callers
must branch on serialized names rather than enum ordinals.

Every category listed above is emitted by at least one code path. Categories are added only when the
code that raises them exists — the registry never advertises a category a caller could wait for
forever.

`unknown` is a read-side fallback only: it is what `MsixError.GetCode` returns for an exception that
carries no category, and it is never assigned at a throw site. It is declared first in the enum so
that `default(MsixErrorCode)` is `unknown` rather than a specific, misleading category.

Categories are attached using `Exception.Data["MsixCore.ErrorCode"]` as boxed `MsixErrorCode` values.
This preserves the exact `System.IO.InvalidDataException` type because that type is sealed and cannot
be subclassed. Changing the thrown type would break the published API and callers that catch it.

## Process exit codes

| Exit code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Negative validation verdict |
| `2` | Usage error |
| `3` | Operational error |
