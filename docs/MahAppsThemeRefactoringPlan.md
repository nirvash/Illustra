# MahApps カスタムテーマ修正計画

## 目的

`src/Themes/Light.xaml` および `src/Themes/Dark.xaml` を、`docs/Theme.Template.xaml` の構造に従って修正し、論理的に適切な配色を割り当てます。カスタムコンポーネントで使用されている可能性のある独自のブラシ定義は一旦維持し、後続ステップで整理します。

## 修正方針

1.  **基本テーマのマージ解除**: 既存の `MergedDictionaries` をコメントアウトし、各テーマファイルが単独で必要な定義を持つようにします。
2.  **テンプレート要素の網羅的な追加**: `Theme.Template.xaml` の全要素（メタデータ、色、ブラシ、WinUI スタイルリソース）を `Light.xaml` と `Dark.xaml` に追加します。
3.  **セクション分け**: ファイル内で定義を明確に区別するため、コメントで見出しを付けたセクションに分けます (`<!-- Theme Metadata -->`, `<!-- MahApps Base Colors -->`, `<!-- MahApps Brushes -->`, `<!-- WinUI Style Resources -->`, `<!-- Custom Colors &amp; Brushes -->`)。
4.  **論理的な色割り当て**:
    - `MahApps.Colors.*` には、各キーの意味に基づき、Light/Dark テーマとして標準的・論理的に正しい色を割り当てます。
    - **Light テーマ**: 眩しすぎないよう、背景はオフホワイト系 (`#FFF5F5F5` 等)、前景はソフトブラック系 (`#FF333333` 等) を基本とします。
    - **Dark テーマ**: 背景は黒系 (`#FF1E1E1E` 等)、前景は白系 (`#FFFFFFFF` 等) を基本とします。
    - 判断が難しいキーには `<!-- TODO: Verify/Adjust color value -->` コメントを付与します。
5.  **ブラシのリンク**: `MahApps.Brushes.*` は、対応する `MahApps.Colors.*` を `StaticResource` で参照する形式にします。
6.  **カスタムキーの維持**: 既存テーマ独自のキーは `<!-- Custom Colors &amp; Brushes (Project specific) -->` セクションに移動し、定義を維持します。

## 計画概要図 (Mermaid)

```mermaid
graph TD
    A[分析: docs/Theme.Template.xaml] --> F{修正方針};
    B[分析: src/Themes/Light.xaml] --> F;
    C[分析: src/Themes/Dark.xaml] --> F;

    F --> G[1. MergedDictionaries コメントアウト];
    F --> H[2. テンプレート要素を網羅的に追加];
    F --> I[3. コメントでセクション分け];
    F --> J_final[4. 論理的に正しい色を割り当て (Lightはトーン抑制、不明な色はTODO)];
    F --> K[5. カスタムキーは別セクションで維持];

    subgraph "修正後のテーマファイル (Light.xaml / Dark.xaml)"
        direction TB
        L[<!-- Theme Metadata -->];
        M[<!-- MahApps Base Colors (Logically assigned) -->];
        N[<!-- MahApps Brushes (StaticResource) -->];
        O[<!-- WinUI Style Resources -->];
        P[<!-- Custom Colors &amp; Brushes -->];
    end

    G --> L &amp; M &amp; N &amp; O &amp; P;
    H --> L &amp; M &amp; N &amp; O &amp; P;
    I --> L &amp; M &amp; N &amp; O &amp; P;
    J_final --> L &amp; M &amp; N &amp; O &amp; P;
    K --> P;

    P --> Q{実装 &amp; 確認};
    L --> Q;
    M --> Q;
    N --> Q;
    O --> Q;

    Q --> R[カスタムブラシの割当検討];
    Q --> S[TODO箇所の調整];
    Q --> T[基本テーマのマージ再開検討];

```

## 次のステップ

1.  この計画に基づき、`code` モードで `src/Themes/Light.xaml` と `src/Themes/Dark.xaml` を修正します。
2.  修正後、アプリケーションをビルド・実行し、エラーがないか、基本的な表示がされるかを確認します。
3.  `TODO` コメント箇所の色を調整します。
4.  カスタムブラシの扱いを検討します。
