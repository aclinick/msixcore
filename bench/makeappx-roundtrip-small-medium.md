# MSIX Core corpus round-trip report

makeappx: C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe
overall: diff/error

## C:\Users\andre\Downloads\CcProto_1.0.3.0_x64_Debug_Test\CcProto_1.0.3.0_x64_Debug_Test\Dependencies\x86\Microsoft.WindowsAppRuntime.1.8-experimental3.msix

normalized source: D:\corpus-work\roundtrip-small-medium\000-Microsoft.WindowsAppRuntime.1.8-experimental3\normalized

### Stored

- ours deterministic: yes
- ours time: 183 ms
- makeappx time: 377 ms
- stored byte-identical: no
- first byte diff: 4
- ZIP structural diffs: 
  - AppxBlockMap.xml central-directory index: 246 vs 245 (Central-directory entry ordering differs.)
  - AppxBlockMap.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxBlockMap.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxBlockMap.xml compressed size: 80673 vs 39282 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxBlockMap.xml uncompressed size: 80673 vs 82246 (The two packages do not contain the same logical entry bytes.)
  - AppxBlockMap.xml CRC-32: BC9FF4DF vs 225EA060 (The entry payload bytes differ.)
  - AppxBlockMap.xml local-header offset: 54499354 vs 54391091 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxBlockMap.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxBlockMap.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxBlockMap.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml local-header general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (Local-file-header general-purpose bit flags differ.)
  - AppxBlockMap.xml local-header compression method: 0 vs 8 (Local-file-header compression method differs.)
  - AppxBlockMap.xml local-header CRC-32: BC9FF4DF vs 00000000 (Local-file-header CRC differs, often because bit 3 uses a data descriptor.)
  - AppxBlockMap.xml local-header compressed size (32-bit field): 80673 vs 0 (Local-file-header 32-bit compressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxBlockMap.xml local-header uncompressed size (32-bit field): 80673 vs 0 (Local-file-header 32-bit uncompressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxManifest.xml central-directory index: 0 vs 244 (Central-directory entry ordering differs.)
  - AppxManifest.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxManifest.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxManifest.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxManifest.xml compressed size: 122353 vs 9270 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxManifest.xml local-header offset: 0 vs 54381751 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxManifest.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxManifest.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxManifest.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
- Block-map semantic diffs: 
  - AppxManifest.xml Block[0].Size: <absent> vs 5051 (The stored/compressed block size differs.)
  - AppxManifest.xml Block[1].Size: <absent> vs 4217 (The stored/compressed block size differs.)

### Optimal

- ours deterministic: yes
- ours time: 1827 ms
- makeappx time: 2045 ms
- optimal equivalent: yes
- package size delta (makeappx - ours): 98666 bytes
- Payload hash diffs: none
- Block-map semantic diffs: none
## C:\Users\andre\Downloads\CcProto_1.0.3.0_x64_Debug_Test\CcProto_1.0.3.0_x64_Debug_Test\Dependencies\arm64\Microsoft.WindowsAppRuntime.1.8-experimental3.msix

normalized source: D:\corpus-work\roundtrip-small-medium\001-Microsoft.WindowsAppRuntime.1.8-experimental3\normalized

### Stored

- ours deterministic: yes
- ours time: 126 ms
- makeappx time: 404 ms
- stored byte-identical: no
- first byte diff: 4
- ZIP structural diffs: 
  - AppxBlockMap.xml central-directory index: 275 vs 274 (Central-directory entry ordering differs.)
  - AppxBlockMap.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxBlockMap.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxBlockMap.xml compressed size: 96137 vs 47450 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxBlockMap.xml uncompressed size: 96137 vs 98024 (The two packages do not contain the same logical entry bytes.)
  - AppxBlockMap.xml CRC-32: 92AAD5AB vs 5B0040D2 (The entry payload bytes differ.)
  - AppxBlockMap.xml local-header offset: 67471979 vs 67361225 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxBlockMap.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxBlockMap.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxBlockMap.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml local-header general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (Local-file-header general-purpose bit flags differ.)
  - AppxBlockMap.xml local-header compression method: 0 vs 8 (Local-file-header compression method differs.)
  - AppxBlockMap.xml local-header CRC-32: 92AAD5AB vs 00000000 (Local-file-header CRC differs, often because bit 3 uses a data descriptor.)
  - AppxBlockMap.xml local-header compressed size (32-bit field): 96137 vs 0 (Local-file-header 32-bit compressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxBlockMap.xml local-header uncompressed size (32-bit field): 96137 vs 0 (Local-file-header 32-bit uncompressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxManifest.xml central-directory index: 0 vs 273 (Central-directory entry ordering differs.)
  - AppxManifest.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxManifest.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxManifest.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxManifest.xml compressed size: 125848 vs 9637 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxManifest.xml local-header offset: 0 vs 67351518 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxManifest.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxManifest.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxManifest.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
- Block-map semantic diffs: 
  - AppxManifest.xml Block[0].Size: <absent> vs 5053 (The stored/compressed block size differs.)
  - AppxManifest.xml Block[1].Size: <absent> vs 4582 (The stored/compressed block size differs.)

### Optimal

- ours deterministic: yes
- ours time: 2439 ms
- makeappx time: 3096 ms
- optimal equivalent: yes
- package size delta (makeappx - ours): 111246 bytes
- Payload hash diffs: none
- Block-map semantic diffs: none
## C:\Users\andre\Downloads\CcProto_1.0.3.0_x64_Debug_Test\CcProto_1.0.3.0_x64_Debug_Test\Dependencies\x64\Microsoft.WindowsAppRuntime.1.8-experimental3.msix

normalized source: D:\corpus-work\roundtrip-small-medium\002-Microsoft.WindowsAppRuntime.1.8-experimental3\normalized

### Stored

- ours deterministic: yes
- ours time: 150 ms
- makeappx time: 5002 ms
- stored byte-identical: no
- first byte diff: 4
- ZIP structural diffs: 
  - AppxBlockMap.xml central-directory index: 279 vs 278 (Central-directory entry ordering differs.)
  - AppxBlockMap.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxBlockMap.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxBlockMap.xml compressed size: 93985 vs 46099 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxBlockMap.xml uncompressed size: 93985 vs 96044 (The two packages do not contain the same logical entry bytes.)
  - AppxBlockMap.xml CRC-32: 16B524B6 vs BC73F333 (The entry payload bytes differ.)
  - AppxBlockMap.xml local-header offset: 64724732 vs 64614075 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxBlockMap.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxBlockMap.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxBlockMap.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml local-header general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (Local-file-header general-purpose bit flags differ.)
  - AppxBlockMap.xml local-header compression method: 0 vs 8 (Local-file-header compression method differs.)
  - AppxBlockMap.xml local-header CRC-32: 16B524B6 vs 00000000 (Local-file-header CRC differs, often because bit 3 uses a data descriptor.)
  - AppxBlockMap.xml local-header compressed size (32-bit field): 93985 vs 0 (Local-file-header 32-bit compressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxBlockMap.xml local-header uncompressed size (32-bit field): 93985 vs 0 (Local-file-header 32-bit uncompressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxManifest.xml central-directory index: 0 vs 277 (Central-directory entry ordering differs.)
  - AppxManifest.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxManifest.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxManifest.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxManifest.xml compressed size: 125844 vs 9634 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxManifest.xml local-header offset: 0 vs 64604371 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxManifest.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxManifest.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxManifest.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
- Block-map semantic diffs: 
  - AppxManifest.xml Block[0].Size: <absent> vs 5051 (The stored/compressed block size differs.)
  - AppxManifest.xml Block[1].Size: <absent> vs 4581 (The stored/compressed block size differs.)

### Optimal

- ours deterministic: yes
- ours time: 2394 ms
- makeappx time: 2775 ms
- optimal equivalent: yes
- package size delta (makeappx - ours): 100900 bytes
- Payload hash diffs: none
- Block-map semantic diffs: none
## C:\Users\andre\Downloads\CcProto_1.0.3.0_x64_Debug_Test\CcProto_1.0.3.0_x64_Debug_Test\CcProto_1.0.3.0_x64_Debug.msix

normalized source: D:\corpus-work\roundtrip-small-medium\003-CcProto_1.0.3.0_x64_Debug\normalized

### Stored

- ours deterministic: yes
- ours time: 953 ms
- makeappx time: 2624 ms
- stored byte-identical: no
- first byte diff: 4
- ZIP structural diffs: 
  - AppxBlockMap.xml central-directory index: 308 vs 307 (Central-directory entry ordering differs.)
  - AppxBlockMap.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxBlockMap.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxBlockMap.xml compressed size: 156990 vs 84056 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxBlockMap.xml uncompressed size: 156990 vs 163446 (The two packages do not contain the same logical entry bytes.)
  - AppxBlockMap.xml CRC-32: C7581D57 vs 274CB9F5 (The entry payload bytes differ.)
  - AppxBlockMap.xml local-header offset: 130083522 vs 130087641 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxBlockMap.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxBlockMap.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxBlockMap.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml local-header general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (Local-file-header general-purpose bit flags differ.)
  - AppxBlockMap.xml local-header compression method: 0 vs 8 (Local-file-header compression method differs.)
  - AppxBlockMap.xml local-header CRC-32: C7581D57 vs 00000000 (Local-file-header CRC differs, often because bit 3 uses a data descriptor.)
  - AppxBlockMap.xml local-header compressed size (32-bit field): 156990 vs 0 (Local-file-header 32-bit compressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxBlockMap.xml local-header uncompressed size (32-bit field): 156990 vs 0 (Local-file-header 32-bit uncompressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxManifest.xml central-directory index: 0 vs 306 (Central-directory entry ordering differs.)
  - AppxManifest.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxManifest.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxManifest.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxManifest.xml compressed size: 3692 vs 1363 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxManifest.xml local-header offset: 0 vs 130086208 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxManifest.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxManifest.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxManifest.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
- Block-map semantic diffs: 
  - AppxManifest.xml Block[0].Size: <absent> vs 1361 (The stored/compressed block size differs.)

### Optimal

- ours deterministic: yes
- ours time: 4900 ms
- makeappx time: 5486 ms
- optimal equivalent: yes
- package size delta (makeappx - ours): 93069 bytes
- Payload hash diffs: none
- Block-map semantic diffs: none
## C:\Users\andre\Downloads\Claude.msix

normalized source: D:\corpus-work\roundtrip-small-medium\004-Claude\normalized

### Stored

- ours deterministic: yes
- ours time: 9003 ms
- makeappx time: 5564 ms
- stored byte-identical: no
- first byte diff: 4
- ZIP structural diffs: 
  - AppxBlockMap.xml central-directory index: 1763 vs 1762 (Central-directory entry ordering differs.)
  - AppxBlockMap.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxBlockMap.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxBlockMap.xml compressed size: 778442 vs 371108 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxBlockMap.xml uncompressed size: 778442 vs 789020 (The two packages do not contain the same logical entry bytes.)
  - AppxBlockMap.xml CRC-32: F4BCC31C vs 540320D6 (The entry payload bytes differ.)
  - AppxBlockMap.xml local-header offset: 556241815 vs 556275812 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxBlockMap.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxBlockMap.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxBlockMap.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxBlockMap.xml local-header general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (Local-file-header general-purpose bit flags differ.)
  - AppxBlockMap.xml local-header compression method: 0 vs 8 (Local-file-header compression method differs.)
  - AppxBlockMap.xml local-header CRC-32: F4BCC31C vs 00000000 (Local-file-header CRC differs, often because bit 3 uses a data descriptor.)
  - AppxBlockMap.xml local-header compressed size (32-bit field): 778442 vs 0 (Local-file-header 32-bit compressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxBlockMap.xml local-header uncompressed size (32-bit field): 778442 vs 0 (Local-file-header 32-bit uncompressed-size field differs; 4294967295 indicates ZIP64 extra-field use.)
  - AppxManifest.xml central-directory index: 0 vs 1761 (Central-directory entry ordering differs.)
  - AppxManifest.xml central-directory version-needed-to-extract: 2.0 (20) vs 4.5 (45) (ZIP version-needed metadata differs; 4.5 indicates ZIP64.)
  - AppxManifest.xml central-directory general-purpose flags: 0x0800 [utf8] vs 0x0008 [data-descriptor] (General-purpose bit flags differ, including UTF-8 or data-descriptor bits.)
  - AppxManifest.xml compression method: 0 vs 8 (Stored should be method 0; optimal payloads may use method 8 except pre-compressed extensions.)
  - AppxManifest.xml compressed size: 7556 vs 1912 (Entry bytes differ after compression or ZIP64 size decoding differs.)
  - AppxManifest.xml local-header offset: 0 vs 556273830 (Earlier entry sizes, ordering, or ZIP header fields differ.)
  - AppxManifest.xml central-directory extra length: 0 vs 28 (Central-directory extra-field byte counts differ.)
  - AppxManifest.xml central-directory extra fields: <none> vs 0x0001(24) (Central-directory extra-field IDs or sizes differ; 0x0001 is ZIP64 extended information.)
  - AppxManifest.xml local-header version-needed-to-extract: 2.0 (20) vs 4.5 (45) (Local-file-header version-needed metadata differs; 4.5 indicates ZIP64.)
- Block-map semantic diffs: 
  - AppxManifest.xml Block[0].Size: <absent> vs 1910 (The stored/compressed block size differs.)
  - [Content_Types].old LfhSize: 49 vs 53 (ZIP local-file-header size semantics differ.)

### Optimal

- ours deterministic: yes
- ours time: 17195 ms
- makeappx time: 20936 ms
- optimal equivalent: yes
- package size delta (makeappx - ours): 825989 bytes
- Payload hash diffs: none
- Block-map semantic diffs: none
