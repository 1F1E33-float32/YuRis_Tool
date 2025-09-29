# YuRis script analyze tool

Usage

- `YuRis_Tool --root <ysbin_dir> [--yscd <yscd_file>] [--key <hex32>|<b0 b1 b2 b3>]`
- Short options are supported: `-r`, `-c`, `-k`.
- Examples:
  - `YuRis_Tool -r D:\\game\\ysbin`
  - `YuRis_Tool -r . -c yscd.bin -k 0x4A415E60`
  - `YuRis_Tool -r . -k 4A 41 5E 60`

Formats list

- [x] YSCM
- [x] YSLB
- [x] YSTL
- [x] YSVR
- [x] YSTB

Decompilation contributed by Pkuism, BIG THANKS.

2024.12.20
