using System.IO;
using System.Windows.Controls;
using Illustra.Models;

namespace Illustra.Helpers
{
    public static class FileSystemHelper
    {
        // ドットで始まるフォルダを除外するためのヘルパーメソッド
        private static bool ShouldShowDirectory(DirectoryInfo directory)
        {
            // Hidden属性を持つか、ドットで始まる名前のフォルダを除外
            return (directory.Attributes & FileAttributes.Hidden) == 0 &&
                   !directory.Name.StartsWith(".", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<List<NodeModel>> LoadDrivesAsync()
        {
            var drives = await Task.Run(() => DriveInfo.GetDrives());
            var driveNodeModels = new List<NodeModel>();
            foreach (var drive in drives)
            {
                try
                {
                    var driveNode = await Task.Run(() => CreateDriveNodeModel(drive));
                    driveNodeModels.Add(driveNode);
                }
                catch (IOException)
                {
                    // Handle IO exceptions (e.g., device not ready)
                    // Log or handle the exception as needed
                }
            }
            return driveNodeModels;
        }

        public static NodeModel CreateDriveNodeModel(DriveInfo driveInfo)
        {
            var driveNodeModel = new NodeModel { Name = driveInfo.Name, FullPath = driveInfo.RootDirectory.FullName };
            return driveNodeModel;
        }

        public static NodeModel CreateDirectoryNodeModel(DirectoryInfo directoryInfo)
        {
            var directoryNodeModel = new NodeModel { Name = directoryInfo.Name, FullPath = directoryInfo.FullName };
            try
            {
                foreach (var directory in directoryInfo.GetDirectories())
                {
                    // ドットで始まるフォルダも除外するように変更
                    if (ShouldShowDirectory(directory))
                    {
                        directoryNodeModel.Directories.Add(CreateDirectoryNodeModel(directory));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Handle access denied errors
            }
            return directoryNodeModel;
        }

        public static TreeViewItem CreateDriveNode(NodeModel driveNodeModel)
        {
            var driveNode = new TreeViewItem { Header = driveNodeModel.Name, Tag = driveNodeModel.FullPath };

            // ドライブ直下のサブフォルダの有無をチェック
            try
            {
                var rootDir = new DirectoryInfo(driveNodeModel.FullPath);
                // ドットで始まるフォルダも除外するように変更
                var hasSubDirectories = rootDir.GetDirectories().Any(dir => ShouldShowDirectory(dir));
                if (hasSubDirectories)
                {
                    driveNode.Items.Add("Loading..."); // サブフォルダがある場合のみLoadingを追加
                    driveNode.Expanded += async (sender, e) =>
                    {
                        if (driveNode.Items.Count == 1 && driveNode.Items[0] is string && (string)driveNode.Items[0] == "Loading...")
                        {
                            driveNode.Items.Clear();
                            try
                            {
                                var directories = await Task.Run(() => rootDir.GetDirectories());

                                foreach (var directory in directories)
                                {
                                    // ドットで始まるフォルダも除外するように変更
                                    if (ShouldShowDirectory(directory))
                                    {
                                        driveNode.Items.Add(CreateDirectoryNode(
                                            new NodeModel { Name = directory.FullName, FullPath = directory.FullName }));
                                    }
                                }
                            }
                            catch (IOException)
                            {
                                driveNode.Items.Add(new TreeViewItem { Header = "Error: IO Exception" });
                            }
                            catch (UnauthorizedAccessException)
                            {
                                driveNode.Items.Add(new TreeViewItem { Header = "Access Denied" });
                            }
                        }
                    };
                }
            }
            catch (Exception)
            {
                // アクセスできない場合などは展開不可として扱う
            }

            return driveNode;
        }

        private static TreeViewItem CreateDirectoryNode(NodeModel directoryNodeModel)
        {
            var directoryNode = new TreeViewItem
            {
                Header = Path.GetFileName(directoryNodeModel.Name),
                Tag = directoryNodeModel.FullPath,
                FontWeight = System.Windows.FontWeights.Bold // フォルダは太字で表示
            };

            // サブフォルダの有無をチェック
            try
            {
                var currentDir = new DirectoryInfo(directoryNodeModel.FullPath);
                // ドットで始まるフォルダも除外するように変更
                var hasSubDirectories = currentDir.GetDirectories().Any(dir => ShouldShowDirectory(dir));
                if (hasSubDirectories)
                {
                    directoryNode.Items.Add("Loading..."); // サブフォルダがある場合のみLoadingを追加
                    directoryNode.Expanded += async (sender, e) =>
                    {
                        if (directoryNode.Items.Count == 1 && directoryNode.Items[0] is string && (string)directoryNode.Items[0] == "Loading...")
                        {
                            directoryNode.Items.Clear();
                            try
                            {
                                var subDirectories = await Task.Run(() => currentDir.GetDirectories());

                                foreach (var directory in subDirectories)
                                {
                                    // ドットで始まるフォルダも除外するように変更
                                    if (ShouldShowDirectory(directory))
                                    {
                                        directoryNode.Items.Add(CreateDirectoryNode(
                                            new NodeModel { Name = directory.FullName, FullPath = directory.FullName }));
                                    }
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                directoryNode.Items.Add(new TreeViewItem { Header = "Access Denied" });
                            }
                            catch (IOException ex)
                            {
                                directoryNode.Items.Add(new TreeViewItem { Header = $"Error: {ex.Message}" });
                            }
                        }
                    };
                }
            }
            catch (Exception)
            {
                // アクセスできない場合などは展開不可として扱う
            }

            return directoryNode;
        }


        /// <summary>
        /// フォルダパスをフォルダ名の配列に分解します
        /// </summary>
        public static List<string> GetPathParts(string path)
        {
            var parts = new List<string>();

            // まずドライブ部分を取得
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                parts.Add(root);
                path = path.Substring(root.Length);
            }

            // 残りのパスを分解
            var folders = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            parts.AddRange(folders);

            return parts;
        }
    }
}
