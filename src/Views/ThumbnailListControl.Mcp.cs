using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using Illustra.Events;
using Illustra.Helpers;
using Illustra.Models;
using Prism.Events;

namespace Illustra.Views
{
    /// <summary>
    /// ThumbnailListControl のうち MCP v2 ツール連携に関する partial クラス。
    /// </summary>
    public partial class ThumbnailListControl : UserControl, IActiveAware, IFileSystemChangeHandler, INotifyPropertyChanged
    {
        /// <summary>
        /// MCP ツール（select_file / list_files / get_selected_files）用のイベント購読。
        /// </summary>
        private void SubscribeMcpEvents()
        {
            _eventAggregator.GetEvent<McpSelectFilesEvent>().Subscribe(OnMcpSelectFiles, ThreadOption.UIThread);
            _eventAggregator.GetEvent<McpGetFileListEvent>().Subscribe(OnMcpGetFileList, ThreadOption.UIThread);
            _eventAggregator.GetEvent<McpGetSelectedFilesEvent>().Subscribe(OnMcpGetSelectedFiles, ThreadOption.UIThread);
            _eventAggregator.GetEvent<McpGetAppStatusEvent>().Subscribe(OnMcpGetAppStatus, ThreadOption.UIThread);
            _eventAggregator.GetEvent<McpSetViewFilterEvent>().Subscribe(OnMcpSetViewFilter, ThreadOption.UIThread);
            _eventAggregator.GetEvent<McpShowViewerEvent>().Subscribe(OnMcpShowViewer, ThreadOption.UIThread);
            _eventAggregator.GetEvent<McpCloseViewerEvent>().Subscribe(OnMcpCloseViewer, ThreadOption.UIThread);
        }

        /// <summary>
        /// アクティブビューのフィルタを変更する（MCP set_view_filter 用）。
        /// 既存の FilterChangedEvent フローへ橋渡しし、UI 表示・ViewModel と同期する。
        /// </summary>
        private void OnMcpSetViewFilter(McpSetViewFilterEventArgs args)
        {
            try
            {
                var builder = new FilterChangedEventArgsBuilder("mcp-v2");
                FilterChangedEventArgs filterArgs;

                if (args.Clear)
                {
                    filterArgs = builder.SetClear().Build();
                }
                else
                {
                    if (args.Rating.HasValue) builder.WithRatingFilter(args.Rating.Value);
                    if (args.PromptFilterEnabled.HasValue) builder.WithPromptFilter(args.PromptFilterEnabled.Value);
                    // 空配列は「拡張子フィルタの単独解除」として扱う
                    if (args.Extensions != null) builder.WithExtensionFilter(args.Extensions.Count > 0, args.Extensions);

                    filterArgs = builder.Build();
                }

                // SourceId が CONTROL_ID でないため、自身の OnFilterChanged で適用される
                _eventAggregator.GetEvent<FilterChangedEvent>().Publish(filterArgs);

                // UIThread への反映は非同期のため、要求適用後の状態をここで算出して返す
                args.AppliedFilterState = ComputeFilterStateAfterRequest(args);
                args.ResultCompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP set_view_filter 処理中にエラーが発生しました", ex);
                args.ErrorMessage = ex.Message;
                args.ResultCompletionSource?.TrySetResult(false);
            }
        }

        /// <summary>
        /// 要求適用後のフィルタ状態を算出する（OnFilterChanged の適用ロジックと同一の規則）。
        /// </summary>
        private ViewFilterStateModel ComputeFilterStateAfterRequest(McpSetViewFilterEventArgs args)
        {
            if (args.Clear)
            {
                return new ViewFilterStateModel();
            }

            var state = new ViewFilterStateModel
            {
                Rating = args.Rating ?? _viewModel.CurrentRatingFilter,
                IsPromptFilterEnabled = args.PromptFilterEnabled ?? _isPromptFilterEnabled,
                IsTagFilterEnabled = _isTagFilterEnabled,
                TagFilters = new List<string>(_currentTagFilters)
            };

            if (args.Extensions is { Count: > 0 })
            {
                state.IsExtensionFilterEnabled = true;
                state.ExtensionFilters = new List<string>(args.Extensions);
            }
            else if (args.Extensions != null)
            {
                // 空配列指定は拡張子フィルタの明示的な解除
                state.IsExtensionFilterEnabled = false;
                state.ExtensionFilters = new List<string>();
            }
            else
            {
                state.IsExtensionFilterEnabled = _isExtensionFilterEnabled;
                state.ExtensionFilters = new List<string>(_currentExtensionFilters);
            }

            return state;
        }

        /// <summary>
        /// アクティブビューに現在有効なフィルタ状態を取得する。
        /// </summary>
        private ViewFilterStateModel GetActiveViewFilterState()
        {
            return new ViewFilterStateModel
            {
                Rating = _viewModel.CurrentRatingFilter,
                IsPromptFilterEnabled = _isPromptFilterEnabled,
                IsTagFilterEnabled = _isTagFilterEnabled,
                TagFilters = new List<string>(_currentTagFilters),
                IsExtensionFilterEnabled = _isExtensionFilterEnabled,
                ExtensionFilters = new List<string>(_currentExtensionFilters)
            };
        }

        /// <summary>
        /// 指定パスのファイルをアクティブタブで選択状態にする。
        /// フィルタで非表示のファイルは選択対象外とし、反映件数を ResultCompletionSource 経由で返す。
        /// （select_file ツールは selectedCount と requestedCount の比較で部分成功を検出できる）
        /// </summary>
        private void OnMcpSelectFiles(McpSelectFilesEventArgs args)
        {
            try
            {
                var pathSet = new HashSet<string>(args.Paths ?? [], StringComparer.OrdinalIgnoreCase);

                // 現在フィルタリングされて表示中のアイテムのみを選択対象にする。
                // 非表示アイテムを選択状態にすると、UI 上は未選択なのに copy/delete 等が
                // 見えないファイルへ作用するため許可しない。
                var matches = GetFilteredItemsList().Where(x => pathSet.Contains(x.FullPath)).ToList();

                foreach (var path in (args.Paths ?? []).Where(p => !matches.Any(m => string.Equals(m.FullPath, p, StringComparison.OrdinalIgnoreCase))))
                {
                    Debug.WriteLine($"[MCP] 選択をスキップ（フィルタで非表示または未読み込み）: {path}");
                }

                ThumbnailItemsControl.SelectedItems.Clear();
                _viewModel.SelectedItems.Clear();
                foreach (var match in matches)
                {
                    ThumbnailItemsControl.SelectedItems.Add(match);
                    _viewModel.SelectedItems.Add(match);
                }
                if (matches.Count > 0)
                {
                    ThumbnailItemsControl.ScrollIntoView(matches[0]);
                }

                args.ResultCompletionSource?.TrySetResult(matches.Count);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP select_file 処理中にエラーが発生しました", ex);
                args.ResultCompletionSource?.TrySetException(new InvalidOperationException($"Failed to select files in Illustra: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// アクティブタブのフォルダパスと読み込み済みファイル一覧を返す。
        /// </summary>
        private void OnMcpGetFileList(McpGetFileListEventArgs args)
        {
            try
            {
                args.FolderPath = _mainWindowViewModel.SelectedTab?.State?.FolderPath;
                args.Files = _viewModel.Items.Cast<FileNodeModel>()
                    .Select(n => new FileListItemModel
                    {
                        Path = n.FullPath,
                        FileName = n.FileName,
                        FileSize = n.FileSize,
                        LastModified = n.LastModified,
                        Rating = n.Rating
                    })
                    .ToList();
                args.ResultCompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP list_files 処理中にエラーが発生しました", ex);
                args.ErrorMessage = ex.Message;
                args.ResultCompletionSource?.TrySetResult(false);
            }
        }

        /// <summary>
        /// アクティブタブの選択中ファイル一覧を返す。
        /// </summary>
        private void OnMcpGetSelectedFiles(McpGetSelectedFilesEventArgs args)
        {
            try
            {
                args.Files = _viewModel.SelectedItems
                    .Select(n => new SelectedFileInfoModel
                    {
                        Path = n.FullPath,
                        FileName = n.FileName
                    })
                    .ToList();
                args.ResultCompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP get_selected_files 処理中にエラーが発生しました", ex);
                args.ErrorMessage = ex.Message;
                args.ResultCompletionSource?.TrySetResult(false);
            }
        }

        /// <summary>
        /// アクティブタブのフォルダ・選択中ファイルなどアプリ全体のステータスを返す。
        /// </summary>
        private void OnMcpGetAppStatus(McpGetAppStatusEventArgs args)
        {
            try
            {
                args.CurrentFolder = _mainWindowViewModel.SelectedTab?.State?.FolderPath;
                args.LoadedFileCount = _viewModel.Items.Count;
                args.SelectedFiles = _viewModel.SelectedItems
                    .Select(n => new SelectedFileInfoModel
                    {
                        Path = n.FullPath,
                        FileName = n.FileName
                    })
                    .ToList();
                args.OpenTabs = _mainWindowViewModel.Tabs
                    .Select(t => t.State?.FolderPath ?? string.Empty)
                    .ToList();
                args.FilterState = GetActiveViewFilterState();
                args.ResultCompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP get_app_status 処理中にエラーが発生しました", ex);
                args.ErrorMessage = ex.Message;
                args.ResultCompletionSource?.TrySetResult(false);
            }
        }

        /// <summary>
        /// ビューワでファイルを表示する（MCP show_viewer 用）。
        /// FilePath が空の場合はアクティブビューの選択中ファイルを使用する。
        /// </summary>
        private void OnMcpShowViewer(McpShowViewerEventArgs args)
        {
            try
            {
                var path = args.FilePath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = _viewModel.SelectedItems.Count > 0 ? _viewModel.SelectedItems[0].FullPath : string.Empty;
                }

                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    args.ErrorMessage = "No file specified or the file does not exist.";
                    args.ResultCompletionSource?.TrySetResult(false);
                    return;
                }

                args.FilePath = path;
                ShowImageViewer(path);
                if (args.BringToFront)
                {
                    BringMcpViewerToFront();
                }
                args.Shown = true;
                args.VisibleInCurrentFilter = GetFilteredItemsList().Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase));
                args.ResultCompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP show_viewer 処理中にエラーが発生しました", ex);
                args.ErrorMessage = ex.Message;
                args.ResultCompletionSource?.TrySetResult(false);
            }
        }

        /// <summary>
        /// Windows のフォアグラウンド制限下でも MCP から要求されたビューワを前面へ移動する。
        /// 常時最前面にはせず、元が Topmost でなければ UI 処理後に解除する。
        /// </summary>
        private void BringMcpViewerToFront()
        {
            var viewer = _imageViewerWindow;
            if (viewer == null)
                return;

            if (viewer.WindowState == System.Windows.WindowState.Minimized)
            {
                viewer.WindowState = System.Windows.WindowState.Normal;
            }

            bool wasTopmost = viewer.Topmost;
            viewer.Topmost = true;
            viewer.Activate();
            viewer.Focus();

            if (!wasTopmost)
            {
                viewer.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(() =>
                    {
                        if (ReferenceEquals(_imageViewerWindow, viewer))
                        {
                            viewer.Topmost = false;
                        }
                    }));
            }
        }

        /// <summary>
        /// ビューワを閉じる（MCP close_viewer 用）。
        /// </summary>
        private void OnMcpCloseViewer(McpCloseViewerEventArgs args)
        {
            try
            {
                args.WasOpen = _imageViewerWindow != null;
                _imageViewerWindow?.Close();
                args.Closed = true;
                args.ResultCompletionSource?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("MCP close_viewer 処理中にエラーが発生しました", ex);
                args.ErrorMessage = ex.Message;
                args.ResultCompletionSource?.TrySetResult(false);
            }
        }
    }
}
