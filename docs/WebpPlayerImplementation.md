# WebP アニメーション再生の設計

## 基本方針

WebPDemuxer を活用し、最小限の初期化で高速な表示開始を実現する。

## インターフェース定義

```csharp
public interface IWebpAnimationService : IDisposable
{
    Task InitializeAsync(string filePath);
    LibWebP.WebPBitstreamFeatures GetFeatures();
    List<TimeSpan> GetFrameDelays();
    Task<BitmapSource> DecodeFrameAsync(int index);
}
動作の流れ
初期化 (InitializeAsync)

Demuxer の作成
基本情報の取得 (幅・高さ・フレーム数)
遅延時間の計算は後回し
最初のフレーム表示

GetFeatures で情報取得
DecodeFrameAsync(0) で最初のフレームをデコード
UI への即時表示
アニメーション再生 (必要な場合のみ)

GetFrameDelays で遅延時間を取得 (初回呼び出し時に計算)
プレイヤーで再生処理を開始
パフォーマンス考慮事項
メモリ効率

必要最小限の情報のみキャッシュ
フレームは随時デコード
再生制御

キャッシュ活用
遅れを許容 (フレームスキップは将来の課題)
将来の拡張
フレームスキップ
キャッシュ戦略の改善
エラーハンドリングの強化
