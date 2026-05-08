# HIR Optimization Plan

`rite40-hir` ブランチで進めている HIR(High-level IR) 上の最適化方針。
最終形は **HIR で最適化 → mrb bytecode に lower → 既存 VM が実行**。
ZJIT (`~/src/github.com/ruby/ruby/zjit`) のパス構成と命名を踏襲する。

## 目標ワークロード(最優先)

ベンチマーク `sandbox/MRubyCS.Benchmark/ruby/bm_ao_render.rb` の以下の行を **最上位の最適化ターゲット** とする:

```ruby
eye = Vec.new(px, py, -1.0).vnormalize
```

`Vec` は同じファイルで定義されたユーザクラス:

```ruby
class Vec
  def initialize(x, y, z); @x=x; @y=y; @z=z; end
  def x; @x; end
  # ... vadd / vsub / vdot / vlength / vnormalize 等
  def vnormalize
    len = vlength
    v = Vec.new(@x, @y, @z)
    if len > 1.0e-17
      v.x = v.x / len; v.y = v.y / len; v.z = v.z / len
    end
    v
  end
end
```

### この 1 行から発生する可観測コスト

1. `Vec.new(...)` で **Vec インスタンス 1 個 alloc**(以後 *V1*)
2. `Vec#initialize` 呼び出し(`@x/@y/@z` セット)
3. `.vnormalize` 呼び出し
4. `vnormalize` 内 `vlength` 呼び出し(更に `Math.sqrt`)
5. `vnormalize` 内 `Vec.new(@x, @y, @z)` で **Vec インスタンス 1 個 alloc**(以後 *V2*)
6. `Vec#initialize` もう一度
7. `v.x` / `v.y` / `v.z` の getter Send × 6
8. `v.x=` / `v.y=` / `v.z=` の setter Send × 3
9. ivar 読み書きそれぞれ、shape / hash 経由で複数回

**理想形**: V1 はインライン後に escape しないので消える。`@x/@y/@z` は SSA 値に昇格。`vlength` は Math.sqrt 1 個に縮約。`vnormalize` の if 内除算は SSA 上で直接展開。最終的に `eye` の x/y/z が 3 個の Float SSA 値として残るだけ — alloc は V2 の 1 個か、`eye` も escape しないなら 0 個。

### 最適化ステップごとの効き目

| 段階 | 達成すること | 必要なパス |
|---|---|---|
| (a) `Vec` 定数解決 | `Vec.new` の receiver が `Class<Vec>` と確定 | Phase C(constant resolution) |
| (b) `Vec.new` インライン | Send → `<allocate Vec> + <invoke initialize>` | Phase D(method inlining) |
| (c) `Vec#initialize` インライン | ivar setter 3 連 | Phase D |
| (d) `vnormalize` インライン | メソッド本体を caller の HIR に展開 | Phase D |
| (e) `x` / `y` / `z` getter インライン | trivial accessor を ivar read に置換 | Phase D + tiny method 認定 |
| (f) V1 が escape しない判定 | V1 は alloc 後 `vnormalize` で ivar 読まれて捨てられる | Phase E(escape analysis) |
| (g) V1 を SROA | `@x/@y/@z` を 3 SSA 値に昇格 | Phase F(scalar replacement) |
| (h) Float 演算の Send → プリミティブ | `@x * b.x` 等を直接 `Mul` に降格 | Phase B(static type spec) — ただし `b.x` の戻り型が掴めて初めて成立 |
| (i) `Math.sqrt` の direct dispatch | C builtin として 1 命令化 | Phase D の sub-case |
| (j) 不変条件監視 | `Vec` が再定義されない / `Vec#vnormalize` が上書きされない保証 | Phase C(invariant 機構) |

## 設計判断

- **AOT-load-time 方式**: bytecode 読み込み直後に 1 回だけ HIR を構築 → 最適化 → bytecode に書き戻し。実行時 JIT はやらない。
- **静的 + 不変条件 ベースの特殊化を主軸**: profile に依存しない。AOT で「class/method がこの先書き換えられない」ことを保証できる範囲で特殊化する。書き換えがあり得る場合のみ guard を残す。
- **投機(runtime guard + deopt)はオプション**: Phase I に分離。Vec のような静的に解決できるユーザクラスは投機なしで届くので、まずそこを取り切る。投機が要るのは「静的に型が決まらないが profile では一様」なケース(配列要素、外部入力)のみ。
- **`MRubyCS.Jit` プロジェクトは将来用に温存**: 投機を本気でやるなら HIR → C# IL の方が設計が綺麗。本プランは IR をそこへ繋がる形で設計する(Guard insn の余地を残す)。

## 現状(2026-05-08 時点)

| 領域 | 状況 | ファイル |
|---|---|---|
| データ構造 | 実装済 | `Hir/HirFunction.cs`、`HirInsn.cs`、`HirBlock.cs`、`HirBranchEdge.cs`、`HirIds.cs` |
| Insn 種別 | 主要 op を網羅 | `Hir/HirInsnKind.cs` |
| 型ラティス | bits + spec(ConstInt/Sym/Class) | `Hir/HirType.cs` |
| 副作用分類 | Kind 単位の静的決定のみ | `Hir/HirEffect.cs` |
| ビルダ | bytecode → HIR(SSA は edge args による暗黙φ) | `Hir/HirBuilder.cs` |
| 解析 | RPO 不動点の TypeInference のみ | `Hir/Passes/TypeInference.cs` |
| **基盤 (Phase A 完了)** | use-list / MakeEqualTo / DCE / Verifier | `HirFunction.cs` / `Passes/Dce.cs` / `HirVerifier.cs` |
| 周辺 | dumper / `MRubyState.DumpHir` / CLI `--hir` | `HirDumper.cs` 他 |

## Phase 構成(改訂版)

優先順は **目標ワークロードに直接効く順**。

### ✅ Phase A: 基盤(完了)

use-list / MakeEqualTo / Pure DCE / HirVerifier。テスト緑。

### Phase B: 軽量な特殊化と CFG クリーンアップ

副次的だが他パスの前提。

- **B-1** Constant fold(`LoadInt(7) + LoadInt(5)` → `LoadInt(12)`、定数 BranchIf の枝刈り)
- **B-2** CFG cleanup(空ブロック・単一前任 → 単一後任 chain 圧縮)
- **B-3** Move 削除(`v_dst = Move v_src` を `MakeEqualTo` で消す)
- **B-4** Static type-spec(両入力が静的に Integer の `Send :+` を `Add` に降格 等)— 適用範囲は狭いが、後段の D/E/F でインライン化が進むと急激に増える

### Phase C: 名前解決と不変条件機構 ★ 目標達成の前提

ZJIT の `Invariant` / `PatchPoint` / `RootBoxOnly` 系統に相当するものを mruby 用に実装する。**ここが目標ワークロードの分岐点**。

- **C-1** Constant resolution: トップレベル irep を走査して定数(クラス・モジュール)を集め、`Vec`、`Math` などを `RClass` 参照に解決。`HirType.ExactClassOf(Vec, ClassObj)` で型を貼る。
- **C-2** Method resolution: あるレシーバ型が決まった `Send` を `MethodEntry`(callable method entry) と child Irep に解決。
- **C-3** Invariant の登録機構: `MRubyState` に `OptimizationInvariants` を持たせ、「class `Vec` の `vnormalize` は現在の Irep」「`Vec` 定数は現在の RClass」という assumption を記録。`define_method` / `alias` / `undef` / `Object.const_set` 等で **対応する HIR 最適化版を invalidate** する hook を VM 側に差し込む。
- **C-4** 簡易ポリシー: 投機なし版では「invariant がトップレベルで一度確立したら、その点以降同一 irep 内で再定義が無いことを静的にチェックして打ち切る」だけでも 8 割以上のユーザクラス使用箇所をカバーできるはず(再オープンしないのが普通)。完全版(VM hook)は Phase I で。

### Phase D: メソッドインライン化 ★ 主役

Phase C で `Send` の呼び先 Irep が解決済みのものが対象。**ZJIT の `inline()` パスの mruby 版**。

- **D-1** Tiny accessor inlining: `def x; @x; end` のような 1 命令メソッドを `GetIV @x` に置換。getter/setter は **最頻出**。
- **D-2** Leaf method inlining: 呼び先 Irep が再帰なし・block 不要・例外捕捉なし・呼び先 Send が解決可能な深さに収まっているとき、Irep を caller の HIR にコピーして`Send` を消す。
  - レジスタ番号は merger 用に rebase。
  - 呼び先の `Param` は引数 SSA 値で `MakeEqualTo` 置換。
  - 戻り値は `Return` 跡に edge を貼り直して合流ブロックに集約。
  - `OP_Enter` は引数バインディングなので、引数 SSA 値の連結だけで消える。
- **D-3** `Class#new` のインライン: `Vec.new(a, b, c)` を `<NewObject Vec> + <invoke Vec#initialize self, a, b, c>` の 2 段に展開。`NewObject` 後の self は新しい SSA 値で、後続の initialize インライン化と SROA に乗る。
- **D-4** Cfunc(C 実装)の direct dispatch: `Math.sqrt` 等は `MRubyMethod` の C 関数を直接呼び出す `CCallDirect` Insn に置換(将来 lowering で `OP_Send` に戻すか、新 op `OP_CCall` を入れるか別途検討)。
- **D-5** インライン深さ制限と invariant 登録: 深さ N まで、code size budget で頭打ち。各インライン箇所で C-3 の invariant を登録。

### Phase E: エスケープ解析

Phase D でインライン化が進んだあとの SSA グラフに対して走らせる。

- **E-1** 抽象 escape state(`NoEscape` / `ArgEscape` / `GlobalEscape`)を SSA 値ごとに点格子で fixed-point。
- **E-2** mruby 固有伝播ルール:
  - escape: `SetIV/GV/CV/Const/UpVar`、`Return*`、未解決 `Send` の引数。
  - 非 escape: `GetIV` の receiver(read のみ)、純粋演算、`Move`、`BranchIf` predicate、解決済みかつ leaf な `Send` の引数(再帰解析)。
- **E-3** `NewObject Vec` の判定: V1 は `vnormalize` 内で ivar 読まれた後そのまま落ちる → NoEscape。V2 は `eye` に流れる → 呼び出し元で更に解析(IPA)。
- **E-4** 結果の保持: `HirFunction.InsnEscape: List<EscapeState>`。

### Phase F: スカラ置換(Object SROA + 配列 SROA)

NoEscape のオブジェクトを SSA 値に分解する。**目標ワークロードの本丸**。

- **F-1** `NewObject` SROA: NoEscape の `NewObject Vec` + 後続 `SetIV` 群を、ivar 名 → SSA 値の連想に変換。続く `GetIV` を直接フォワード。`NewObject` を DCE。
  - 部分的 escape(条件分岐の片側だけ)は対応しない(まずは)。
  - `Return` で返る場合(V2)は、return-elision を caller で行う(IPA)。
- **F-2** 配列 SROA: `NewArray` 結果が NoEscape + 添字が定数のみ → 要素を SSA 値に分解。
- **F-3** Hash SROA: 同様、キー全部定数 Symbol 限定。
- **F-4** String 縮約: `NewString` から read-only に流れるだけなら `LoadPool` reference に縮約。
- **F-5** Range elision: `for i in 0...n` を `BranchIf` ループに正規化。

### Phase G: ブロックインライン化

`each` / `times` / `map` 系。Phase D + E + F の組合せの応用。

- **G-1** `Send` の block 引数が `OP_Block`/`OP_Lambda` 由来 + NoEscape の場合、block irep を受け取り側で展開し `OP_Block` の RProc / REnv alloc を消す。
- **G-2** `OP_Call` / `OP_BlkCall` 地点を block の HIR エントリに置換。`GetUpVar/SetUpVar` は SSA 値に解決(REnv 不要)。
- **G-3** 段階導入: 最初は yield 1 回の単純 leaf に限定。Array#each / Integer#times を Ruby 実装で持つ場合のみ対象。

### Phase H: bytecode lowering

最適化済み HIR を mrb bytecode に書き戻す。VM 側は変更なし(まずは)。

- **H-1** `RegisterVariableCount` を SROA で増えた SSA 値に合わせて拡張。
- **H-2** Phi(edge args)を末尾 `OP_Move` 連で展開。
- **H-3** インライン化された irep の `OP_GetUpVar/SetUpVar` をローカルレジスタアクセスに書き換え。
- **H-4** Cfunc direct dispatch は当面 `OP_Send` に戻す(後で新 op 検討)。
- **H-5** 投機 guard が必要になった段階で新 op(`OP_GuardClass` 等)を追加検討 → Phase I に持ち越し。

### Phase I: 投機(オプション、最後)

profile が無くてもトップレベルで `Vec` が一意に解決できるケースは Phase C-D で取れる。**Phase I が必要になるのは:**
- メソッド引数の型(`def foo(v); v.x; end` の `v` の型が静的に決まらない)
- 配列要素の型
- 外部入力(stdin / IO)

これらに対し **runtime guard + deopt** を入れる。

- **I-1** Profile 収集: 既存 VM に observation hook を仕込み、Send の receiver 型分布を集める。`zjit/src/profile.rs` 相当。
- **I-2** Guard insn(`HirInsnKind.GuardType` / `GuardClass`)を導入。
- **I-3** Side-exit 経路: ガード失敗時に元の Send 版 bytecode に戻れる仕掛け。新 op `OP_GuardClass` を追加し、失敗時は元の SourcePc にジャンプ。
- **I-4** Version 管理: 同一メソッドを「投機版」と「保守版」の 2 バージョン保持。`MRubyCS.Jit` プロジェクトに移行する候補。

### Phase J: VM フック(C-3 と連動)

Phase C の invariant 機構を完成させるには VM 側の hook が必要。

- `define_method` / `alias_method` / `remove_method` / `undef_method` / `Class#class_eval` / 定数再代入 — それぞれ `OptimizationInvariants` の対応エントリを invalidate。
- 当面は **invalidate 時に最適化版 Irep を捨てて元の bytecode に戻す** だけで OK(再最適化はやらない)。

## パス実行順(改訂版)

```
TypeInference         // 既存
ConstantResolution    // C-1
Dce                   // A-3 (基盤)
ConstantFold          // B-1
MoveElim              // B-3
StaticTypeSpec        // B-4
MethodInline          // D
TypeInference (再)
ConstantFold (再)
EscapeAnalysis        // E
ScalarReplacement     // F
BlockInline           // G
TypeInference (再)
ConstantFold (再)
CleanCfg              // B-2
Dce
Verify                // A-4
```

ZJIT のように **複数回ループさせる** 前提で `optimize()` を組む(D が新しい機会を生む → E/F が新しい dead code を生む → DCE で掃除 → 型情報が更新される → 再特殊化…)。

## 検証戦略

- 各パスの golden snapshot テスト(`HirDumpTest` 拡張、ZJIT の insta 風 before/after)。
- ベンチ 3 系統を `sandbox/MRubyCS.Benchmark` に追加:
  - `bm_ao_render.rb`(目標ワークロード — 数値 + ユーザクラス)
  - block-heavy(`Array#each` 連鎖)
  - alloc-heavy(短命 Hash)
- 計測項目: 実行時間、`GC.GetTotalAllocatedBytes` 差分、Vec alloc 回数。
- dev ビルドで `HirVerifier` を全パス後に挟む。

## マイルストーン(改訂版)

1. **M1** ✅ Phase A 完了。
2. **M2** Phase B-1/B-2/B-3 + Phase H の最初(SSA → bytecode の往復が壊れない最小ケース)。HIR 最適化が VM 実行に効く回路を開通させる。**ここで初めて `bm_ao_render.rb` が HIR 経由で動く。**
3. **M3** Phase C(constant resolution + invariant 骨格)+ Phase J の最小実装。
4. **M4** Phase D-1(tiny accessor inlining)+ D-2(leaf method)+ D-3(`Class#new`)。**ここで Vec.new + initialize + getter のインライン化が完了し、`bm_ao_render` の主要 Send が HIR 上で消える。**
5. **M5** Phase E + F-1(`NewObject` SROA)。**ここで Vec の alloc が消える。目標ワークロードの本丸。**
6. **M6** Phase B-4(動的化が外れた数値演算のプリミティブ化)+ 再ループ。**ここで Float 演算が直接命令に。**
7. **M7** Phase G(block inlining)+ F-2/F-3(Array/Hash SROA)。
8. **M8** Phase I(投機)— 必要が観測されたら。

## 議論ポイント

- **不変条件の invalidate 時の動作**: 最適化版 Irep を捨てて元の bytecode に戻すだけで足りるか? それとも再最適化をスケジュールするか? 当面は前者で。
- **インライン budget**: 何 op までインラインするか、何深さまで掘るか。当初は保守的に(深さ 2、サイズ 32 op)。
- **`MRubyCS.Jit` プロジェクトとの分割**: Phase I 以降は Jit に移すべきか、HIR 側に投機 op を入れて lowering で吸収するか。前者の方が綺麗だが工数大。