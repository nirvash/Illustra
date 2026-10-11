using Microsoft.Data.Sqlite;
using System.Reflection;
using Illustra.Helpers;
using NUnit.Framework;
using System;
using System.Linq;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using LinqToDB;
using Illustra.Models;
using System.IO;

namespace Illustra.Tests.Helpers
{
    [TestFixture]
    public class DatabaseAccessProviderTests
    {
        [Test]
        public void FileNodeSchema_PreservesJapaneseDatesAndCrudAfterReopen()
        {
            var directory = Path.Combine(Path.GetTempPath(), "Illustra-provider-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "synthetic.db"), Pooling = false
            }.ToString();
            var provider = SQLiteTools.GetDataProvider(new DatabaseManager.MySettings(connectionString).ConnectionStrings.Single().ProviderName);
            var timestamp = new DateTime(2026, 10, 11, 12, 34, 56).AddMilliseconds(123);
            var row = new FileNodeModel
            {
                FullPath = "合成/双葉.png", FolderPath = "合成", FileName = "双葉.png", FileType = ".png",
                CreationTime = timestamp, LastModified = timestamp, LastCheckedTime = timestamp,
                FileSize = 123, Rating = 2, IsImage = true
            };
            try
            {
                using (var db = new DataConnection(provider, connectionString))
                {
                    db.CreateTable<FileNodeModel>();
                    db.Insert(row);
                    Assert.That(db.Execute<string>("SELECT typeof(CreationTime) FROM FileNodeModel"), Is.EqualTo("text"));
                }
                using (var db = new DataConnection(provider, connectionString))
                {
                    var loaded = db.GetTable<FileNodeModel>().Single();
                    Assert.Multiple(() =>
                    {
                        Assert.That(loaded.FileName, Is.EqualTo(row.FileName));
                        Assert.That(loaded.CreationTime, Is.EqualTo(timestamp));
                        Assert.That(loaded.LastModified, Is.EqualTo(timestamp));
                        Assert.That(loaded.LastCheckedTime, Is.EqualTo(timestamp));
                        Assert.That(loaded.IsImage, Is.True);
                        Assert.That(loaded.FileSize, Is.EqualTo(123));
                    });
                    loaded.Rating = 5;
                    loaded.LastCheckedTime = null;
                    db.Update(loaded);
                }
                using (var db = new DataConnection(provider, connectionString))
                {
                    Assert.That(db.GetTable<FileNodeModel>().Single().Rating, Is.EqualTo(5));
                    Assert.That(db.GetTable<FileNodeModel>().Single().LastCheckedTime, Is.Null);
                    db.GetTable<FileNodeModel>().Delete();
                    Assert.That(db.GetTable<FileNodeModel>().Count(), Is.Zero);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void LegacySystemSqliteFixture_PreservesSchemaStorageTypesAndValues()
        {
            var path = Path.Combine(Path.GetTempPath(), "Illustra-legacy-" + Guid.NewGuid().ToString("N") + ".db");
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            var provider = SQLiteTools.GetDataProvider(ProviderName.SQLiteMS);
            const string schemaQuery = "SELECT sql FROM sqlite_master WHERE type='table' AND name='FileNodeModel'";
            const string columnsQuery = "SELECT group_concat(name || ':' || type || ':' || \"notnull\", '|') FROM pragma_table_info('FileNodeModel')";
            const string typesQuery = "SELECT typeof(CreationTime) || ':' || typeof(LastModified) || ':' || typeof(IsImage) || ':' || typeof(FileSize) || ':' || typeof(Rating) FROM FileNodeModel";
            var timestamp = new DateTime(2026, 10, 11, 12, 34, 56).AddMilliseconds(123);
            try
            {
                using (var fixture = typeof(DatabaseAccessProviderTests).Assembly.GetManifestResourceStream("Illustra.Tests.Fixtures.legacy-system-sqlite.db")!)
                using (var output = File.Create(path)) fixture.CopyTo(output);
                string schema, columns, types;
                using (var db = new DataConnection(provider, connectionString))
                {
                    schema = db.Execute<string>(schemaQuery);
                    columns = db.Execute<string>(columnsQuery);
                    types = db.Execute<string>(typesQuery);
                    var row = db.GetTable<FileNodeModel>().Single();
                    Assert.Multiple(() =>
                    {
                        Assert.That(row.FileName, Is.EqualTo("日本語.png"));
                        Assert.That(row.CreationTime, Is.EqualTo(timestamp));
                        Assert.That(row.LastModified, Is.EqualTo(timestamp));
                        Assert.That(row.LastCheckedTime, Is.EqualTo(timestamp));
                        Assert.That(row.IsImage, Is.True);
                        Assert.That(row.FileSize, Is.EqualTo(42));
                        Assert.That(row.Rating, Is.EqualTo(2));
                        Assert.That(types, Is.EqualTo("text:text:integer:integer:integer"));
                    });
                    row.Rating = 5;
                    row.LastCheckedTime = null;
                    db.Update(row);
                }
                using (var db = new DataConnection(provider, connectionString))
                {
                    Assert.That(db.Execute<string>(schemaQuery), Is.EqualTo(schema));
                    Assert.That(db.Execute<string>(columnsQuery), Is.EqualTo(columns));
                    Assert.That(db.Execute<string>(typesQuery), Is.EqualTo(types));
                    Assert.That(db.GetTable<FileNodeModel>().Single().Rating, Is.EqualTo(5));
                    Assert.That(db.GetTable<FileNodeModel>().Single().LastCheckedTime, Is.Null);
                }
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ConfiguredProvider_UsesMicrosoftAndPatchedNativeEngine()
        {
            var settings = new DatabaseManager.MySettings("Data Source=:memory:");
            using var db = new DataConnection(SQLiteTools.GetDataProvider(settings.ConnectionStrings.Single().ProviderName), "Data Source=:memory:");
            var version = db.Execute<string>("SELECT sqlite_version()");
            TestContext.WriteLine($"SQLite native engine: {version}; connection: {db.Connection.GetType().FullName}");
            Assert.Multiple(() =>
            {
                Assert.That(db.Connection.GetType().FullName, Is.EqualTo("Microsoft.Data.Sqlite.SqliteConnection"));
                Assert.That(Version.Parse(version), Is.GreaterThanOrEqualTo(new Version(3, 50, 2)));
            });
        }

        private static readonly MethodInfo IsBusyError = typeof(DatabaseAccess).GetMethod(
            "IsBusyError", BindingFlags.Static | BindingFlags.NonPublic)!;

        [Test]
        public void IsBusyError_RecognizesBusyAndRejectsOtherProviderErrors()
        {
            Assert.That(IsBusyError.Invoke(null, new object[] { new SqliteException("合成 busy", 5).SqliteErrorCode }), Is.EqualTo(true));
            Assert.That(IsBusyError.Invoke(null, new object[] { new SqliteException("合成 locked", 6).SqliteErrorCode }), Is.EqualTo(false));
            Assert.That(IsBusyError.Invoke(null, new object[] { new SqliteException("合成 error", 1).SqliteErrorCode }), Is.EqualTo(false));
        }
    }
}
