using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using Illustra.Models;
using Illustra.Helpers;
using Illustra.Mcp.Tools;
using NUnit.Framework;

namespace Illustra.Tests;

[TestFixture]
public class FolderMoveTests
{
    private string _root = null!;
    private string _source = null!;
    private string _target = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "illustra-move-" + Guid.NewGuid());
        _source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        _target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        Directory.CreateDirectory(Path.Combine(_source, "child"));
        File.WriteAllText(Path.Combine(_source, "child", "image.txt"), "preserved content");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void Move_PreservesNestedContentsAndRemovesSource()
    {
        var destination = FolderMoveHelper.Move(_source, _target);
        Assert.That(destination, Is.EqualTo(Path.Combine(_target, "source")));
        Assert.That(Directory.Exists(_source), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(destination, "child", "image.txt")), Is.EqualTo("preserved content"));
    }

    [TestCase("self")]
    [TestCase("descendant")]
    [TestCase("parent")]
    [TestCase("collision")]
    [TestCase("fileCollision")]
    public void Move_RejectsInvalidDestinationWithoutChangingSource(string kind)
    {
        var destination = kind switch
        {
            "self" => _source.ToUpperInvariant() + Path.DirectorySeparatorChar,
            "descendant" => Path.Combine(_source, "child"),
            "parent" => _root,
            _ => _target
        };
        if (kind == "collision") Directory.CreateDirectory(Path.Combine(_target, "source"));
        if (kind == "fileCollision") File.WriteAllText(Path.Combine(_target, "source"), "existing");
        Assert.That(FolderMoveHelper.CanMove(_source, destination), Is.False);
        Assert.Throws<IOException>(() => FolderMoveHelper.Move(_source, destination));
        Assert.That(File.ReadAllText(Path.Combine(_source, "child", "image.txt")), Is.EqualTo("preserved content"));
    }

    [Test]
    public void CanMove_AllowsSiblingWithSharedNamePrefix()
    {
        var sibling = Directory.CreateDirectory(_source + "-other").FullName;
        Assert.That(FolderMoveHelper.CanMove(_source, sibling), Is.True);
    }

    [Test]
    public void McpMoveFolder_RejectsSelfBeforeDatabaseAccess()
    {
        var database = (DatabaseManager)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseManager));
        var tools = new FolderTools(null!, database);
        Assert.ThrowsAsync<IOException>(async () => await tools.MoveFolders([_source], _source));
        Assert.That(Directory.Exists(_source), Is.True);
    }

    [Test]
    public async Task McpMoveFolders_MovesBothFoldersAndPreservesRatingsAsync()
    {
        var second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        File.WriteAllText(Path.Combine(second, "other.txt"), "second content");
        var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, "test.db"), Pooling = false
        }.ToString();
        var provider = SQLiteTools.GetDataProvider(ProviderName.SQLiteMS);
        using var db = new DataConnection(provider, connection);
        db.CreateTable<FileNodeModel>();
        foreach (var folder in new[] { _source, second })
        {
            db.Insert(new FileNodeModel
            {
                FullPath = Path.Combine(folder, "sample.png"), FolderPath = folder,
                FileName = "sample.png", Rating = 4
            });
        }
        using var access = new DatabaseAccess(provider, connection);
        var database = (DatabaseManager)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseManager));
        typeof(DatabaseManager).GetField("_dbAccess", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, access);
        var results = await new FolderTools(null!, database).MoveFolders([_source, second], _target);
        Assert.That(results, Has.Count.EqualTo(2));
        foreach (var result in results)
        {
            Assert.That(result.Moved && result.DatabaseUpdated, Is.True);
            Assert.That(Directory.Exists(result.OldPath), Is.False);
            var row = db.GetTable<FileNodeModel>().Single(r => r.FullPath == Path.Combine(result.NewPath, "sample.png"));
            Assert.That(row.FolderPath, Is.EqualTo(result.NewPath));
            Assert.That(row.Rating, Is.EqualTo(4));
        }
        Assert.That(File.ReadAllText(Path.Combine(_target, "second", "other.txt")), Is.EqualTo("second content"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void McpMoveFolders_PreflightsEntireBatch(bool overlapping)
    {
        var other = overlapping ? Path.Combine(_source, "child") : Directory.CreateDirectory(Path.Combine(_root, "other", "source")).FullName;
        var database = (DatabaseManager)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseManager));
        var tools = new FolderTools(null!, database);
        Assert.ThrowsAsync<IOException>(async () => await tools.MoveFolders([_source, other], _target));
        Assert.That(Directory.Exists(_source), Is.True);
        Assert.That(Directory.Exists(other), Is.True);
        Assert.That(Directory.Exists(Path.Combine(_target, "source")), Is.False);
    }
}
