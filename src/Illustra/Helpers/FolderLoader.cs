using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using Illustra.Models;

namespace Illustra.Helpers
{
    public static class FileSystemHelper
    {
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
                    if ((directory.Attributes & FileAttributes.Hidden) == 0)
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
            driveNode.Expanded += async (sender, e) =>
            {
                if (driveNode.Items.Count == 1 && driveNode.Items[0] is string && (string)driveNode.Items[0] == "Loading...")
                {
                    driveNode.Items.Clear();
                    DirectoryInfo[] directories = Array.Empty<DirectoryInfo>();
                    try
                    {
                        directories = await Task.Run(() => new DirectoryInfo(driveNodeModel.FullPath).GetDirectories());
                    }
                    catch (IOException)
                    {
                        // Handle IO exceptions (e.g., device not ready)
                        // Log or handle the exception as needed
                        driveNode.Items.Add(new TreeViewItem { Header = "Error: IO Exception" });
                        return;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Handle access denied errors
                        driveNode.Items.Add(new TreeViewItem { Header = "Access Denied" });
                        return;
                    }

                    foreach (var directory in directories)
                    {
                        if ((directory.Attributes & FileAttributes.Hidden) == 0)
                        {
                            driveNode.Items.Add(CreateDirectoryNode(new NodeModel { Name = directory.FullName, FullPath = directory.FullName }));
                        }
                    }
                }
            };
            driveNode.Items.Add("Loading...");
            return driveNode;
        }

        private static TreeViewItem CreateDirectoryNode(NodeModel directoryNodeModel)
        {
            var directoryNode = new TreeViewItem { Header = Path.GetFileName(directoryNodeModel.Name), Tag = directoryNodeModel.FullPath };
            var directories = new DirectoryInfo(directoryNodeModel.FullPath).GetDirectories();

            if (directories.Length > 0)
            {
                directoryNode.Expanded += async (sender, e) =>
                {
                    if (directoryNode.Items.Count == 1 && directoryNode.Items[0] is string && (string)directoryNode.Items[0] == "Loading...")
                    {
                        directoryNode.Items.Clear();
                        var subDirectories = await Task.Run(() => new DirectoryInfo(directoryNodeModel.FullPath).GetDirectories());
                        foreach (var directory in subDirectories)
                        {
                            if ((directory.Attributes & FileAttributes.Hidden) == 0)
                            {
                                directoryNode.Items.Add(CreateDirectoryNode(new NodeModel { Name = directory.FullName, FullPath = directory.FullName }));
                            }
                        }
                    }
                };
                directoryNode.Items.Add("Loading...");
            }

            return directoryNode;
        }

        /// <summary>
        /// 指定されたパスに対応するTreeViewItemを見つけて選択状態にします
        /// </summary>
        /// <param name="treeView">ターゲットとなるTreeView</param>
        /// <param name="fullPath">選択したいフォルダの完全パス</param>
        /// <returns>パスが見つかって選択できた場合はtrue、それ以外はfalse</returns>
        public static async Task<bool> SelectPathInTreeViewAsync(TreeView treeView, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || treeView == null || treeView.Items.Count == 0)
                return false;

            try
            {
                // パスの各部分を取得
                var pathParts = GetPathParts(fullPath);
                if (pathParts.Count == 0) return false;

                // ルート（ドライブ）を探す
                string rootPart = pathParts[0];
                TreeViewItem rootNode = null;

                foreach (var item in treeView.Items)
                {
                    if (item is TreeViewItem tvi && tvi.Tag is string tag &&
                        tag.StartsWith(rootPart, StringComparison.OrdinalIgnoreCase))
                    {
                        rootNode = tvi;
                        break;
                    }
                }

                if (rootNode == null) return false;

                // ルートノードを展開
                rootNode.IsExpanded = true;

                // UIスレッドの更新を待機
                await Task.Delay(150);

                // 残りのパスを再帰的に探索
                TreeViewItem currentNode = rootNode;

                // 最初のパーツはドライブなので、それ以降を処理
                for (int i = 1; i < pathParts.Count; i++)
                {
                    string part = pathParts[i];
                    bool found = false;

                    // 子ノードをロードするために展開を確実に
                    if (!currentNode.IsExpanded)
                    {
                        currentNode.IsExpanded = true;
                        // 子ノードが非同期的にロードされる場合があるので少し待つ
                        await Task.Delay(150);
                    }

                    foreach (var item in currentNode.Items)
                    {
                        if (item is TreeViewItem childNode && childNode.Tag is string childPath)
                        {
                            string folderName = Path.GetFileName(childPath);
                            if (string.Equals(folderName, part, StringComparison.OrdinalIgnoreCase))
                            {
                                currentNode = childNode;
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found) return false;

                    // 最後のノード以外は展開する
                    if (i < pathParts.Count - 1)
                    {
                        currentNode.IsExpanded = true;
                        await Task.Delay(150); // UIの更新を待つ
                    }
                }

                // 最終ノードを選択状態にする - より効果的な選択を確保
                currentNode.IsSelected = true;
                currentNode.Focus(); // フォーカスを与える

                // スクロールして見えるようにする
                currentNode.BringIntoView();

                // UIスレッドの処理を確実にするため少し待つ
                await Task.Delay(50);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"パスの選択中にエラーが発生: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// フォルダパスをフォルダ名の配列に分解します
        /// </summary>
        private static List<string> GetPathParts(string path)
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
