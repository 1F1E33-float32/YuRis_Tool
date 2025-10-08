# YuRis script analyze tool

Author: https://github.com/nanami5270

Decompile: https://github.com/pkuislm

Usage

- `YuRis_Tool --root <ysbin_dir> [--yscd <yscd_file>] [--key <hex32>|<b0 b1 b2 b3>] [--json|--format json]`
- Short options are supported: `-r`, `-c`, `-k`, `-j`.
- Examples:
  - `YuRis_Tool -r D:\\game\\ysbin`
  - `YuRis_Tool -r . -c yscd.bin -k 0x4A415E60`
  - `YuRis_Tool -r . -k 4A 41 5E 60`
  - `YuRis_Tool -r . --json` (outputs `.json` instead of `.txt`)

Formats list

- [x] YSCM
- [x] YSLB
- [x] YSTL
- [x] YSVR
- [x] YSTB