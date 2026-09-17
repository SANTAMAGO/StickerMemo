# Third-Party Notices

StickerMemo (Rust/Tauri edition, v1.0.0+) is built on the open-source Rust crates below. All are used under permissive licenses that allow redistribution in a compiled, closed or open binary without additional obligations beyond preserving copyright/license notices.

(The original .NET/WPF implementation had its own NuGet-based notices; that version's history, including its third-party notices, remains available in this repository's git log prior to the Rust rebuild.)

## Direct dependencies (`src-tauri/Cargo.toml`)

| Crate | Version | License | Project |
| --- | ---: | --- | --- |
| tauri | 2.11.x | MIT OR Apache-2.0 | https://github.com/tauri-apps/tauri |
| tauri-build | 2.6.x | MIT OR Apache-2.0 | https://github.com/tauri-apps/tauri |
| serde | 1.0.x | MIT OR Apache-2.0 | https://github.com/serde-rs/serde |
| serde_json | 1.0.x | MIT OR Apache-2.0 | https://github.com/serde-rs/json |
| rusqlite | 0.32.x | MIT | https://github.com/rusqlite/rusqlite |
| chrono | 0.4.x | MIT OR Apache-2.0 | https://github.com/chronotope/chrono |
| uuid | 1.x | MIT OR Apache-2.0 | https://github.com/uuid-rs/uuid |
| regex | 1.x | MIT OR Apache-2.0 | https://github.com/rust-lang/regex |
| dirs | 5.x | MIT OR Apache-2.0 | https://github.com/soc/dirs-rs |
| winreg | 0.52.x | MIT | https://github.com/gentoo90/winreg-rs |
| windows | 0.58.x | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |

`rusqlite` is built with the `bundled` feature, which statically links [SQLite](https://www.sqlite.org/copyright.html), a work dedicated to the public domain.

## Transitive dependencies

The full dependency graph (~460 crates, pulled in by the direct dependencies above — mainly by `tauri`'s WebView2/Win32 integration) uses only permissive open-source licenses: **MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, Zlib, ISC, Unicode-3.0, Unlicense**, and a small number of **MPL-2.0** files (used unmodified, in source form, as required by that license). No copyleft license (GPL/LGPL/AGPL) applies to any dependency.

To regenerate the exact, versioned list at any time:

```powershell
cd src-tauri
cargo metadata --format-version=1 | jq -r '.packages[] | "\(.name) \(.version) \(.license // "")"' | sort
```

(or use [`cargo-license`](https://crates.io/crates/cargo-license) for a formatted report.)

## Runtime component (not bundled)

StickerMemo renders its UI using the **Microsoft Edge WebView2 Runtime**, which is part of Windows 11 and most up-to-date Windows 10 installations. It is not bundled inside `StickerMemo.exe`; Windows installs/updates it independently under Microsoft's own [WebView2 license terms](https://developer.microsoft.com/microsoft-edge/webview2/).

---

## License texts

### MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

### Apache License 2.0 (summary)

Full text: https://www.apache.org/licenses/LICENSE-2.0

Licensed under the Apache License, Version 2.0 (the "License"); you may not
use the covered files except in compliance with the License. You may obtain
a copy of the License at the URL above. Unless required by applicable law or
agreed to in writing, software distributed under the License is distributed
on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either
express or implied.

Other permissive licenses used by transitive dependencies (BSD-2-Clause,
BSD-3-Clause, Zlib, ISC, Unicode-3.0, Unlicense, MPL-2.0) can be found at
[choosealicense.com](https://choosealicense.com/appendix/) or in each crate's
own repository linked via the `cargo metadata` command above.
