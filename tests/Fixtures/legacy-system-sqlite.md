# 旧SQLite providerの合成fixture

`legacy-system-sqlite.db` はユーザーDBのコピーではない。旧Illustraの `System.Data.SQLite.Core 1.0.119`（実エンジン3.46.1）と `linq2db 5.4.1` で新規作成した合成DB。脆弱な実行ライブラリはfixtureに含めず、テストは新providerだけで開く。

生成時は `E:/Worktrees/Illustra/net9-native-baseline`（開始コミット9f912e0）のアプリを参照した独立console probeで、実際の `FileNodeModel` に対して以下を実行した。

```csharp
var provider = SQLiteTools.GetDataProvider("SQLite");
using var db = new DataConnection(provider, "Data Source=legacy-system-sqlite.db;Pooling=False;");
db.CreateTable<FileNodeModel>();
var date = new DateTime(2026, 10, 11, 12, 34, 56).AddMilliseconds(123);
db.Insert(new FileNodeModel {
    FullPath = "synthetic/日本語.png", FolderPath = "synthetic", FileName = "日本語.png", FileType = ".png",
    CreationTime = date, LastModified = date, LastCheckedTime = date,
    FileSize = 42, Rating = 2, IsImage = true
});
```

生成した接続型は `System.Data.SQLite.SQLiteConnection`、ファイルサイズ12,288 bytes。FileNodeModelの日時はDateTime2宣言でtext格納、boolはBit宣言でinteger格納。

`LegacySystemSqliteFixture_PreservesSchemaStorageTypesAndValues` はfixtureを一時領域へコピーし、新SQLiteMSで日本語・日時・boolを読取り、評価・NULL日時を更新して再openする。`sqlite_master` のDDL、`pragma_table_info`、変更対象以外の `typeof` を保存前後で比較する。fixture自体には書き込まない。
