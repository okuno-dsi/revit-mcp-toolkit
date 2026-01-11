# AutoCAD Core Console 実行の絶対に間違えないメモ（必読）

目的: パスにスペースが含まれる `accoreconsole.exe` を確実に起動し、引数（/i, /s, /l）を安全に渡すための最小原則と、実用コピペ例。エラー「'C:\Program' は、内部コマンドまたは外部コマンド…」は、引用符の不足/ずれが原因です。

---

## 失敗しない原則
- パスにスペースがある部分は、必ず二重引用符 `"…"` で囲む。
- `cmd.exe /k` でコマンドを渡すときは、入れ子の引用符が必要。
  - 形式: `cmd /k ""C:\path to\exe\accoreconsole.exe" /i "C:\...seed.dwg" /s "C:\...run.scr" /l en-US"`
  - 最初の `"` は `/k` の引数開始、直後の `"` で実行ファイルパスの開始（ダブルクォートが連続するのが正しい）。
- PowerShell は `Start-Process` で `-FilePath` と `-ArgumentList` を分けると引用が自動整形されて安全。
  - 例: `Start-Process -FilePath 'C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe' -ArgumentList @('/i', 'C:\seed.dwg', '/s', 'C:\run.scr', '/l', 'en-US')`
- 変数にパスを保持するときは、必ず引用した上で展開（cmd なら `set "ACCORE=…"` → `%ACCORE%`）。

---

## すぐ使えるコピペ（今回の環境）

以下は、今回の実行対象に合わせた即コピペ用です。パスは環境に合わせて調整してください。

- 直起動（cmd）
```
"C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe" ^
  /i "%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\seed.dwg" ^
  /s "%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\Staging\aeebea9a92f1439d85cd03cb7277905d\run.scr" ^
  /l en-US
```

- コンソールを開いたまま実行（cmd /k）
```
cmd /k ""C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe" /i "%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\seed.dwg" /s "%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\Staging\aeebea9a92f1439d85cd03cb7277905d\run.scr" /l en-US"
```

- PowerShell（自動引用・推奨）
```
$exe = 'C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe'
$seed = '%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\seed.dwg'
$scr  = '%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\Staging\aeebea9a92f1439d85cd03cb7277905d\run.scr'
Start-Process -FilePath $exe -ArgumentList @('/i', $seed, '/s', $scr, '/l', 'en-US') -WorkingDirectory '%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut'
```

---

## よくある落とし穴（チェックリスト）
- [ ] 実行ファイルパスに引用符がない（`C:\Program Files\...` を裸で書いている）
- [ ] `cmd /k` の二重引用の入れ子が足りない（先頭の `""..."` になっていない）。
- [ ] 引数 `/i`, `/s` のファイルパスが未引用（スペース・日本語を含む）。
- [ ] パス中の全角引用符（”）や別文字を混入（ASCII のダブルクォート `"` を使用）。
- [ ] PowerShell で 1 本の長い文字列を `-ArgumentList` に渡し、内部の引用が崩れている（配列で渡す）。

---

## 補助テクニック
- cmd で安全に: 先に環境変数に格納
```
set "ACCORE=C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe"
set "SEED=%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\seed.dwg"
set "SCR=%USERPROFILE%\Documents\VS2022\Ver431\Codex\Work\AutoCadOut\Staging\aeebea9a92f1439d85cd03cb7277905d\run.scr"
"%ACCORE%" /i "%SEED%" /s "%SCR" /l en-US
```
- 行継続（cmd）: `^`、（PowerShell）: `` ` `` バッククォート。
- ロケール: 原則 `en-US` 推奨（スクリプトは英語コマンド前提）。

---

## トラブル時の採取ポイント
- `run.scr` の内容（TRUSTEDPATHS、XREF、BIND、LISP 関数、SAVEAS 行）
- CoreConsole の標準出力/標準エラー（末尾数十行）
- 出力先 `out\merged.dwg` の有無

以上。

