# MRubyCS Regexp 実装仕様

Ruby 4.x の Regexp / MatchData クラスを MRubyCS に実装するための仕様書。

## 概要

正規表現機能は以下の2つのクラスで構成される:

- **Regexp** - 正規表現パターンを保持し、マッチング操作を提供
- **MatchData** - マッチング結果を保持し、キャプチャグループへのアクセスを提供

C# の `System.Text.RegularExpressions.Regex` をバックエンドとして使用する。

---

## 1. Regexp クラス

### 1.1 定数

| 定数名 | 値 | 説明 |
|--------|-----|------|
| `IGNORECASE` | 1 | 大文字小文字を区別しない (`i` フラグ) |
| `EXTENDED` | 2 | 拡張モード - 空白とコメントを無視 (`x` フラグ) |
| `MULTILINE` | 4 | 複数行モード - `.` が改行にマッチ (`m` フラグ) |
| `FIXEDENCODING` | 16 | 固定エンコーディング |
| `NOENCODING` | 32 | エンコーディングなし |

### 1.2 クラスメソッド

| メソッド | シグネチャ | 説明 | 優先度 |
|----------|------------|------|--------|
| `new` | `new(pattern, options=0, timeout: nil)` | 新しい Regexp を作成 | 必須 |
| `compile` | `compile(pattern, options=0, timeout: nil)` | `new` のエイリアス | 必須 |
| `escape` | `escape(string) -> String` | メタ文字をエスケープ | 必須 |
| `quote` | `quote(string) -> String` | `escape` のエイリアス | 必須 |
| `union` | `union(*patterns) -> Regexp` | 複数パターンの OR 結合 | 高 |
| `try_convert` | `try_convert(obj) -> Regexp or nil` | Regexp への変換を試みる | 高 |
| `last_match` | `last_match(n=nil) -> MatchData or String or nil` | 直前のマッチ結果を返す | 中 |
| `timeout` | `timeout -> Float or nil` | グローバルタイムアウトを取得 | 低 |
| `timeout=` | `timeout=(seconds)` | グローバルタイムアウトを設定 | 低 |
| `linear_time?` | `linear_time?(pattern, options=0) -> bool` | 線形時間でマッチ可能か | 低 |

### 1.3 インスタンスメソッド

| メソッド | シグネチャ | 説明 | 優先度 |
|----------|------------|------|--------|
| `match` | `match(string, pos=0) -> MatchData or nil` | マッチを実行、グローバル変数を設定 | 必須 |
| `match?` | `match?(string, pos=0) -> bool` | マッチの真偽のみ返す (グローバル変数を設定しない) | 必須 |
| `=~` | `=~(string) -> Integer or nil` | マッチ位置を返す | 必須 |
| `===` | `===(string) -> bool` | case 文用の等価演算子 | 必須 |
| `~` | `~ -> Integer or nil` | `$_` に対してマッチ | 低 |
| `source` | `source -> String` | パターン文字列を返す | 必須 |
| `options` | `options -> Integer` | オプションのビットマスクを返す | 必須 |
| `casefold?` | `casefold? -> bool` | IGNORECASE が設定されているか | 必須 |
| `encoding` | `encoding -> Encoding` | エンコーディングを返す | 中 |
| `fixed_encoding?` | `fixed_encoding? -> bool` | 固定エンコーディングか | 低 |
| `named_captures` | `named_captures -> Hash` | 名前付きキャプチャのハッシュ | 高 |
| `names` | `names -> Array` | 名前付きキャプチャの名前配列 | 高 |
| `to_s` | `to_s -> String` | 文字列表現 | 必須 |
| `inspect` | `inspect -> String` | 検査用文字列表現 | 必須 |
| `==` | `==(other) -> bool` | 等価比較 | 必須 |
| `eql?` | `eql?(other) -> bool` | 等価比較 | 必須 |
| `hash` | `hash -> Integer` | ハッシュ値 | 必須 |
| `timeout` | `timeout -> Float or nil` | このRegexpのタイムアウト | 低 |

---

## 2. MatchData クラス

### 2.1 インスタンスメソッド

| メソッド | シグネチャ | 説明 | 優先度 |
|----------|------------|------|--------|
| `[]` | `[](index) -> String or nil` | インデックスまたは名前でキャプチャを取得 | 必須 |
| `[]` | `[](start, length) -> Array` | 範囲でキャプチャを取得 | 高 |
| `[]` | `[](range) -> Array` | Range でキャプチャを取得 | 高 |
| `[]` | `[](name) -> String or nil` | 名前でキャプチャを取得 | 必須 |
| `begin` | `begin(n) -> Integer or nil` | n 番目のキャプチャの開始位置 | 必須 |
| `end` | `end(n) -> Integer or nil` | n 番目のキャプチャの終了位置 | 必須 |
| `offset` | `offset(n) -> [Integer, Integer] or [nil, nil]` | 開始・終了位置の配列 | 高 |
| `byteoffset` | `byteoffset(n) -> [Integer, Integer]` | バイト位置での offset | 中 |
| `match` | `match(n) -> String or nil` | n 番目のキャプチャ文字列 | 高 |
| `match_length` | `match_length(n) -> Integer or nil` | n 番目のキャプチャの長さ | 中 |
| `captures` | `captures -> Array` | キャプチャの配列 (全体マッチを除く) | 必須 |
| `to_a` | `to_a -> Array` | 全マッチの配列 (全体マッチを含む) | 必須 |
| `to_s` | `to_s -> String` | マッチした文字列全体 | 必須 |
| `named_captures` | `named_captures -> Hash` | 名前付きキャプチャのハッシュ | 高 |
| `names` | `names -> Array` | キャプチャグループ名の配列 | 高 |
| `pre_match` | `pre_match -> String` | マッチ前の文字列 | 必須 |
| `post_match` | `post_match -> String` | マッチ後の文字列 | 必須 |
| `regexp` | `regexp -> Regexp` | マッチに使用した Regexp | 必須 |
| `string` | `string -> String` | マッチ対象の文字列 (frozen) | 必須 |
| `size` | `size -> Integer` | キャプチャの数 + 1 | 必須 |
| `length` | `length -> Integer` | `size` のエイリアス | 必須 |
| `values_at` | `values_at(*indices) -> Array` | 指定インデックスのキャプチャを配列で返す | 高 |
| `==` | `==(other) -> bool` | 等価比較 | 高 |
| `eql?` | `eql?(other) -> bool` | 等価比較 | 高 |
| `hash` | `hash -> Integer` | ハッシュ値 | 高 |
| `inspect` | `inspect -> String` | 検査用文字列表現 | 必須 |
| `deconstruct` | `deconstruct -> Array` | パターンマッチ用 | 低 |
| `deconstruct_keys` | `deconstruct_keys(keys) -> Hash` | パターンマッチ用 | 低 |

---

## 3. グローバル変数

正規表現マッチ後に設定されるグローバル変数（スレッドローカル）:

| 変数 | 説明 | 実装方法 |
|------|------|----------|
| `$~` | 最後の MatchData | `MRubyState` にスレッドローカルで保持 |
| `$&` | マッチした文字列全体 | `$~[0]` と同等 |
| `$`` | マッチ前の文字列 | `$~.pre_match` と同等 |
| `$'` | マッチ後の文字列 | `$~.post_match` と同等 |
| `$+` | 最後にマッチしたキャプチャ | 最後の非nil キャプチャ |
| `$1` ~ `$9` | 番号付きキャプチャ | `$~[n]` と同等 |

### 実装上の注意

- グローバル変数は `Regexp#match`、`Regexp#=~`、`String#match`、`String#=~` で設定される
- `Regexp#match?`、`String#match?` では設定されない（パフォーマンス向上のため）
- MRubyCS では `MRubyState` にフィールドとして保持し、メソッド呼び出し時に更新

---

## 4. String クラスの拡張

Regexp をサポートするために String クラスに以下のメソッドを追加/拡張:

| メソッド | シグネチャ | 説明 | 優先度 |
|----------|------------|------|--------|
| `=~` | `=~(regexp) -> Integer or nil` | マッチ位置を返す | 必須 |
| `match` | `match(regexp, pos=0) -> MatchData or nil` | マッチを実行 | 必須 |
| `match?` | `match?(regexp, pos=0) -> bool` | マッチの真偽のみ | 必須 |
| `scan` | `scan(regexp) -> Array` | 全マッチを配列で返す | 高 |
| `scan` | `scan(regexp) { \|m\| ... } -> self` | ブロック付き scan | 高 |
| `split` | `split(regexp, limit=-1) -> Array` | 正規表現で分割 | 必須 |
| `sub` | `sub(regexp, replacement) -> String` | 最初のマッチを置換 | 必須 |
| `sub` | `sub(regexp) { \|m\| ... } -> String` | ブロック付き sub | 高 |
| `sub!` | `sub!(regexp, replacement) -> String or nil` | 破壊的 sub | 高 |
| `gsub` | `gsub(regexp, replacement) -> String` | 全マッチを置換 | 必須 |
| `gsub` | `gsub(regexp) { \|m\| ... } -> String` | ブロック付き gsub | 高 |
| `gsub!` | `gsub!(regexp, replacement) -> String or nil` | 破壊的 gsub | 高 |
| `index` | `index(regexp, offset=0) -> Integer or nil` | 正規表現でインデックス検索 | 高 |
| `rindex` | `rindex(regexp, offset=-1) -> Integer or nil` | 逆方向検索 | 中 |
| `slice` | `slice(regexp) -> String or nil` | 正規表現で部分文字列取得 | 高 |
| `slice` | `slice(regexp, capture) -> String or nil` | キャプチャ指定 | 高 |
| `partition` | `partition(regexp) -> Array` | マッチで3分割 | 中 |
| `rpartition` | `rpartition(regexp) -> Array` | 逆方向 partition | 中 |

### 置換文字列の特殊シーケンス

`sub`/`gsub` の置換文字列では以下の特殊シーケンスをサポート:

| シーケンス | 説明 |
|------------|------|
| `\0`, `\&` | マッチした文字列全体 |
| `\1` ~ `\9` | 番号付きキャプチャ |
| `\`` | マッチ前の文字列 |
| `\'` | マッチ後の文字列 |
| `\+` | 最後にマッチしたキャプチャ |
| `\\` | バックスラッシュ自体 |
| `\k<name>` | 名前付きキャプチャ |

---

## 5. C# 実装設計

Time クラスと同様に、`RData` を使用して C# のデータオブジェクトを保持する方式を採用する。

### 5.1 ファイル構成

```
src/MRubyCS/
├── MRubyState.Regex.cs     # クラス定義と初期化
└── StdLib/
    ├── RegexpMembers.cs    # Regexp のメソッド実装 + MRubyRegexpData クラス
    └── MatchDataMembers.cs # MatchData のメソッド実装 + MRubyMatchData クラス
```

### 5.2 MRubyRegexpData クラス設計

`RData.Data` に格納する C# データクラス:

```csharp
/// <summary>
/// RData に格納される Regexp のデータ
/// </summary>
class MRubyRegexpData : IEquatable<MRubyRegexpData>
{
    // バックエンドの .NET Regex
    public Regex CompiledRegex { get; }

    // 元のパターン文字列 (UTF-8)
    public byte[] Pattern { get; }

    // Ruby のオプションフラグ
    public RegexpOptions Options { get; }

    // タイムアウト (nullable)
    public TimeSpan? Timeout { get; }

    public MRubyRegexpData(
        Regex compiledRegex,
        byte[] pattern,
        RegexpOptions options,
        TimeSpan? timeout = null)
    {
        CompiledRegex = compiledRegex;
        Pattern = pattern;
        Options = options;
        Timeout = timeout;
    }

    public bool Equals(MRubyRegexpData? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Pattern.AsSpan().SequenceEqual(other.Pattern) &&
               Options == other.Options;
    }

    public override bool Equals(object? obj) =>
        obj is MRubyRegexpData other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Pattern.Length, Options);
}

[Flags]
public enum RegexpOptions
{
    None = 0,
    IgnoreCase = 1,      // IGNORECASE
    Extended = 2,        // EXTENDED
    Multiline = 4,       // MULTILINE
    FixedEncoding = 16,  // FIXEDENCODING
    NoEncoding = 32      // NOENCODING
}
```

### 5.3 MRubyMatchData クラス設計

`RData.Data` に格納する C# データクラス:

```csharp
/// <summary>
/// RData に格納される MatchData のデータ
/// </summary>
class MRubyMatchData
{
    // .NET の Match 結果
    public Match Match { get; }

    // マッチに使用した Regexp (RData)
    public RData Regexp { get; }

    // マッチ対象の文字列
    public string TargetString { get; }

    public MRubyMatchData(Match match, RData regexp, string targetString)
    {
        Match = match;
        Regexp = regexp;
        TargetString = targetString;
    }
}
```

### 5.4 RData 作成ヘルパー

Time クラスと同様のパターンでヘルパーメソッドを提供:

```csharp
static class RegexpMembers
{
    public static RData CreateRegexpRData(MRubyState mrb, MRubyRegexpData data)
    {
        var regexpClass = mrb.GetConst(mrb.Intern("Regexp"u8), mrb.ObjectClass).As<RClass>();
        return new RData(regexpClass, data);
    }

    public static bool TryGetRegexpData(MRubyValue value, out MRubyRegexpData data)
    {
        if (value.Object is RData { Data: MRubyRegexpData regexpData })
        {
            data = regexpData;
            return true;
        }
        data = default!;
        return false;
    }

    static MRubyRegexpData GetRegexpData(MRubyState mrb, MRubyValue value)
    {
        if (TryGetRegexpData(value, out var data))
        {
            return data;
        }
        mrb.Raise(Names.TypeError, "wrong argument type"u8);
        return default!; // unreachable
    }

    // ... メソッド実装
}

static class MatchDataMembers
{
    public static RData CreateMatchDataRData(MRubyState mrb, MRubyMatchData data)
    {
        var matchDataClass = mrb.GetConst(mrb.Intern("MatchData"u8), mrb.ObjectClass).As<RClass>();
        return new RData(matchDataClass, data);
    }

    public static bool TryGetMatchData(MRubyValue value, out MRubyMatchData data)
    {
        if (value.Object is RData { Data: MRubyMatchData matchData })
        {
            data = matchData;
            return true;
        }
        data = default!;
        return false;
    }

    static MRubyMatchData GetMatchData(MRubyState mrb, MRubyValue value)
    {
        if (TryGetMatchData(value, out var data))
        {
            return data;
        }
        mrb.Raise(Names.TypeError, "wrong argument type"u8);
        return default!; // unreachable
    }

    // ... メソッド実装
}
```

### 5.5 オプション変換

Ruby と .NET のオプション対応:

| Ruby オプション | .NET RegexOptions |
|----------------|-------------------|
| `IGNORECASE` (i) | `RegexOptions.IgnoreCase` |
| `MULTILINE` (m) | `RegexOptions.Singleline` (注意: 意味が逆) |
| `EXTENDED` (x) | `RegexOptions.IgnorePatternWhitespace` |

**重要**: Ruby の `MULTILINE` は `.` が改行にマッチする機能で、.NET の `Singleline` に相当する。
Ruby の `^`/`$` はデフォルトで行頭/行末にマッチするが、.NET では `Multiline` オプションが必要。

```csharp
static RegexOptions ConvertOptions(RegexpOptions rubyOptions)
{
    var options = RegexOptions.Multiline; // Ruby のデフォルト動作

    if ((rubyOptions & RegexpOptions.IgnoreCase) != 0)
        options |= RegexOptions.IgnoreCase;

    if ((rubyOptions & RegexpOptions.Multiline) != 0)
        options |= RegexOptions.Singleline; // . が \n にマッチ

    if ((rubyOptions & RegexpOptions.Extended) != 0)
        options |= RegexOptions.IgnorePatternWhitespace;

    return options;
}
```

### 5.6 パターン変換

Ruby と .NET で正規表現構文に違いがあるため、一部変換が必要:

| Ruby 構文 | .NET 構文 | 対応 |
|-----------|-----------|------|
| `(?<name>...)` | `(?<name>...)` | 同じ |
| `(?'name'...)` | `(?'name'...)` | 同じ |
| `\h` (16進数字) | `[0-9a-fA-F]` | 変換必要 |
| `\H` | `[^0-9a-fA-F]` | 変換必要 |
| `\p{...}` | `\p{...}` | 一部互換 |
| `\R` (改行) | `(?:\r\n\|[\n\v\f\r\x85\u2028\u2029])` | 変換必要 |
| `\X` (grapheme) | サポート困難 | 制限事項 |
| `\K` (match reset) | サポートなし | 制限事項 |

---

## 6. 実装フェーズ

### Phase 1: 基本機能 (必須)

1. `MRubyRegexpData` クラス作成、`RegexpMembers.cs` 実装
2. `MRubyMatchData` クラス作成、`MatchDataMembers.cs` 実装
3. `Regexp.new`, `Regexp#match`, `Regexp#=~`, `Regexp#===`
4. `MatchData#[]`, `#begin`, `#end`, `#captures`, `#to_a`, `#to_s`
5. `MatchData#pre_match`, `#post_match`, `#regexp`, `#string`, `#size`
6. `String#=~`, `String#match`, `String#split` (正規表現対応)
7. グローバル変数 `$~`, `$&`, `$1`-`$9`

### Phase 2: 置換機能 (必須)

1. `String#sub`, `String#gsub` (基本)
2. 置換文字列の特殊シーケンス対応
3. `String#sub!`, `String#gsub!`

### Phase 3: 高度な機能 (高優先度)

1. `Regexp.escape`, `Regexp.union`, `Regexp.try_convert`
2. `Regexp#named_captures`, `Regexp#names`
3. `MatchData#named_captures`, `#names`, `#offset`, `#values_at`
4. `String#scan`, `String#index` (正規表現対応)
5. ブロック付き `sub`, `gsub`, `scan`

### Phase 4: 追加機能 (中〜低優先度)

1. `Regexp.last_match`
2. `Regexp#encoding`, `#fixed_encoding?`
3. `MatchData#byteoffset`, `#match_length`
4. `String#slice` (正規表現対応)
5. `String#partition`, `#rpartition` (正規表現対応)
6. タイムアウト機能
7. `deconstruct`, `deconstruct_keys` (パターンマッチ)

---

## 7. 制限事項

MRubyCS での Regexp 実装における制限:

1. **`\K` (match reset)** - .NET でサポートされていないため未対応
2. **`\X` (Unicode grapheme cluster)** - 完全な実装は困難
3. **`\G` (前回マッチ位置)** - グローバルマッチの連続実行時のみ対応
4. **後方参照の制限** - .NET の制限に従う
5. **Onigmo 固有機能** - Ruby の Onigmo エンジン固有の機能は一部未対応
6. **`$_` 変数** - mruby では通常サポートされないため低優先度

---

## 8. テスト計画

### 単体テスト

```ruby
# Regexp 基本
assert_equal "foo", /foo/.source
assert_equal true, /foo/i.casefold?
assert_equal 0, /foo/ =~ "foo bar"

# MatchData 基本
m = /(.)(.)/.match("ab")
assert_equal "ab", m[0]
assert_equal "a", m[1]
assert_equal "b", m[2]
assert_equal ["a", "b"], m.captures

# 名前付きキャプチャ
m = /(?<first>.)(?<second>.)/.match("ab")
assert_equal "a", m[:first]
assert_equal "b", m["second"]

# String メソッド
assert_equal ["a", "b", "c"], "a1b2c".split(/\d/)
assert_equal "axbxc", "a1b2c".gsub(/\d/, "x")
```

### 互換性テスト

mruby の既存テストスイートから Regexp 関連テストを移植して実行。

---

## 参考資料

- [Ruby 4.x Regexp Documentation](https://docs.ruby-lang.org/en/master/Regexp.html)
- [Ruby 4.x MatchData Documentation](https://docs.ruby-lang.org/en/master/MatchData.html)
- [.NET Regex Documentation](https://docs.microsoft.com/en-us/dotnet/standard/base-types/regular-expressions)
- [mruby-regexp-pcre](https://github.com/iij/mruby-regexp-pcre)
