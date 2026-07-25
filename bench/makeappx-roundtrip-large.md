# MSIX Core corpus round-trip report

makeappx: C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe
overall: diff/error

## C:\Users\andre\Downloads\dist\Contoso Finance Agent 1.0.1.appx

normalized source: C:\corpus-work\roundtrip-large\000-Contoso Finance Agent 1.0.1\normalized

### Stored

- ours deterministic: yes
- ours time: 28873 ms
- makeappx time: 27476 ms
- stored byte-identical: no
- first byte diff: 4
- ZIP structural diffs: 
  - AppxBlockMap.xml central-directory index: 178 vs 177 (Central-directory entry ordering differs.)
  - AppxBlockMap.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxBlockMap.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxBlockMap.xml compressed size: 2154724 vs 1208721 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxBlockMap.xml uncompressed size: 2154724 vs 2126175 (The two packages do not contain the same logical entry bytes.)
  - AppxBlockMap.xml CRC-32: 64F67F57 vs 6B097949 (The entry payload bytes differ.)
  - AppxBlockMap.xml local-header offset: 2285705938 vs 2285706469 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxBlockMap.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxBlockMap.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxBlockMap.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml local-header general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (Local-file-header general-purpose bit flags differ.)
  - AppxBlockMap.xml local-header compression method: 0 vs 8 (Local-file-header compression method differs.)
  - AppxBlockMap.xml local-header CRC-32: 64F67F57 vs 00000000 (Local-file-header CRC differs, often because bit 3 uses a data descriptor.)
  - AppxBlockMap.xml local-header compressed size (32-bit field): 2154724 vs 0 (Local-file-header 32-bit compressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxBlockMap.xml local-header uncompressed size (32-bit field): 2154724 vs 0 (Local-file-header 32-bit uncompressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxManifest.xml central-directory index: 0 vs 176 (Central-directory entry ordering differs.)
  - AppxManifest.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxManifest.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxManifest.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxManifest.xml compressed size: 1938 vs 765 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxManifest.xml local-header offset: 0 vs 2285705634 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxManifest.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxManifest.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxManifest.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
- Block-map semantic diffs: 
  - AppxManifest.xml Block[0].Size: <absent> vs 763 (The stored/compressed block size differs.)

### Optimal

- ours deterministic: yes
- ours time: 127918 ms
- makeappx time: 164801 ms
- optimal equivalent: yes
- package size delta (makeappx - ours): -11127047 bytes
- Payload hash diffs: none
- Block-map semantic diffs: none
