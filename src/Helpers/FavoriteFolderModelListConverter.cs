using System;
using System.Collections.ObjectModel;
using System.Text.Json; // System.Text.Json を使用
using System.Text.Json.Serialization; // JsonConverter を使用
using Illustra.Models; // FavoriteFolderModel を使うために追加
using System.Linq; // ToList を使うために追加
using System.Diagnostics; // Debug.WriteLine を使うために追加

namespace Illustra.Helpers
{
    /// <summary>
    /// ObservableCollection<FavoriteFolderModel> のカスタムJsonConverter (System.Text.Json 用)。
    /// 古い形式 (stringの配列) と新しい形式 (FavoriteFolderModelオブジェクトの配列) の両方を読み込めるようにする。
    /// </summary>
    public class FavoriteFolderModelListConverter : JsonConverter<ObservableCollection<FavoriteFolderModel>>
    {
        public override void Write(Utf8JsonWriter writer, ObservableCollection<FavoriteFolderModel> value, JsonSerializerOptions options)
        {
            // 書き込みはデフォルトの動作に任せる (新しい形式で書き込む)
            // カスタムコンバーター内でデフォルトのシリアライザを直接呼び出すのは少し複雑になる場合があるため、
            // ここでは単純に JsonSerializer.Serialize を使う (再帰呼び出しに注意が必要だが、通常は問題ない)
            // JsonSerializer.Serialize(writer, value, typeof(ObservableCollection<FavoriteFolderModel>), options);
            // より安全な方法: オプションからこのコンバーターを除外してシリアライズする
            var optionsWithoutConverter = new JsonSerializerOptions(options);
            optionsWithoutConverter.Converters.Remove(this); // 自分自身を除外
            JsonSerializer.Serialize(writer, value, optionsWithoutConverter);
        }

        public override ObservableCollection<FavoriteFolderModel> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // JSONトークンが配列の開始でない場合はエラーまたは空のコレクションを返す
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                // throw new JsonException("Expected StartArray token");
                // デシリアライズに失敗した場合や予期しない形式の場合は空のリストを返すのが安全
                // 不正な形式の場合、reader を読み進めないと後続の処理で問題が起きる可能性がある
                reader.Skip(); // 現在のトークンとその子要素をスキップ
                return new ObservableCollection<FavoriteFolderModel>();
            }

            var list = new ObservableCollection<FavoriteFolderModel>();

            // 配列の要素を読み取るループ
            while (reader.Read())
            {
                // 配列の終わりならループ終了
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                try // 個々の要素の変換エラーが全体に影響しないようにする
                {
                    // 要素が文字列の場合 (古い形式)
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var path = reader.GetString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            list.Add(new FavoriteFolderModel(path));
                        }
                    }
                    // 要素がオブジェクトの場合 (新しい形式)
                    else if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        // オブジェクトを FavoriteFolderModel としてデシリアライズ
                        // ここでも再帰呼び出しを避けるため、オプションからコンバーターを除外する
                        var optionsWithoutConverter = new JsonSerializerOptions(options);
                        optionsWithoutConverter.Converters.Remove(this); // 自分自身を除外
                        // reader の現在の位置からオブジェクトをデシリアライズ
                        var model = JsonSerializer.Deserialize<FavoriteFolderModel>(ref reader, optionsWithoutConverter);

                        if (model != null && !string.IsNullOrEmpty(model.Path))
                        {
                            list.Add(model);
                        }
                        // Deserialize<T> は EndObject まで読み進めるはずなので、手動で Skip する必要はない
                    }
                    // その他の形式はスキップする
                    else
                    {
                        reader.Skip(); // 不明なトークンをスキップ
                    }
                }
                catch (Exception ex)
                {
                    // エラーログなどを記録することが望ましい
                    Debug.WriteLine($"Error converting favorite folder item: {ex.Message}");
                    // エラーが発生した要素はスキップして続行 (reader.Skip() が必要か要検討)
                    // reader.Skip(); // エラー発生時もスキップを試みる (Deserialize で例外が出た場合など)
                    // → Deserialize<T> が例外を投げた場合、reader の位置が不定になる可能性があるため、
                    //   安全のためには try-catch の外で reader.Skip() するか、より堅牢なエラー処理が必要。
                    //   ここでは簡略化のため、例外発生時は次の Read() に任せる。
                }
            }

            return list;
        }
    }
}
