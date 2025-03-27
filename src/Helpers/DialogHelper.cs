using System;
using System.Threading.Tasks;
using System.Windows;
using Illustra.Models;
using Illustra.ViewModels; // MainWindowViewModel を使うために追加
using Illustra.Views;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace Illustra.Helpers
{
    // 通常クラスから静的クラスに戻す
    public static class DialogHelper
    {
        // 静的な DialogCoordinator インスタンスは不要

        /// <summary>
        /// カスタム進捗ダイアログを表示し、進捗報告用の IProgress とダイアログを閉じるための Action を返します。
        /// <b>このメソッドは UI スレッドから呼び出す必要があります。</b>
        /// </summary>
        /// <param name="owner">ダイアログのオーナーウィンドウ (MetroWindow であり、DataContext が MainWindowViewModel である必要があります)。</param>
        /// <param name="title">ダイアログのタイトル。</param>
        /// <returns>
        /// 進捗報告用の <see cref="IProgress{T}"/> インスタンスと、ダイアログを閉じるための <see cref="Action"/> を含むタプル。
        /// 返される <c>progress</c> のコールバックは、このメソッドが呼び出された <b>UI スレッド</b> で実行されます。
        /// 返される <c>closeDialog</c> は任意のスレッドから呼び出し可能ですが、実際のクローズ処理は UI スレッドで行われます。
        /// </returns>
        // インスタンスメソッドから静的メソッドに戻す
        public static async Task<(IProgress<FileOperationProgressInfo> progress, Action closeDialog)> ShowProgressDialogAsync(
            Window owner,
            string title,
            CancellationTokenSource cts) // Add CancellationTokenSource parameter
        {
            if (cts == null) throw new ArgumentNullException(nameof(cts)); // Ensure cts is provided
            // owner が MetroWindow であり、DataContext が MainWindowViewModel であることを確認
            if (!(owner is MetroWindow metroWindow))
            {
                throw new ArgumentException("Owner window must be a MetroWindow.", nameof(owner));
            }
            if (!(metroWindow.DataContext is MainWindowViewModel viewModel))
            {
                throw new InvalidOperationException("Owner window's DataContext must be MainWindowViewModel.");
            }
            // ViewModel から DialogCoordinator を取得
            var dialogCoordinator = viewModel.MahAppsDialogCoordinator;
            if (dialogCoordinator == null)
            {
                throw new InvalidOperationException("MainWindowViewModel.MahAppsDialogCoordinator is not set.");
            }


            CustomProgressDialog customDialog = null;

            // UI スレッドでの実行は呼び出し元で保証されている前提
            // (Dispatcher.InvokeAsync を削除)
            customDialog = new CustomProgressDialog(metroWindow)
            {
                Title = title,
            };
            customDialog.SetCancellationTokenSource(cts); // Pass the CancellationTokenSource
            // ViewModel から取得した DialogCoordinator を使用
            await dialogCoordinator.ShowMetroDialogAsync(metroWindow.DataContext, customDialog);


            if (customDialog == null) // ShowMetroDialogAsync は非同期だが、customDialog は直後に null ではないはず
            {
                // このパスは通常通らないはずだが念のため
                throw new InvalidOperationException("Failed to create or show CustomProgressDialog.");
            }

            // closeDialog アクションを先に定義 (ラムダ内で参照するため)
            Action closeDialog = null; // 初期化

            var progress = new Progress<FileOperationProgressInfo>(info =>
            {
                // UI スレッドで実行される
                customDialog.UpdateProgress(
                     string.Format((string)Application.Current.FindResource("String_Dialog_FileProgressCountFormat"),
                         info.ProcessedFiles, info.TotalFiles),
                     info.CurrentFileName,
                     info.ProgressPercentage);

                // 処理が完了し、かつキャンセルされていなければダイアログを閉じる
                // Check IsCancelled property on the dialog instance
                if (!customDialog.IsCancelled && info.ProcessedFiles >= info.TotalFiles && info.TotalFiles > 0)
                {
                    // closeDialog が null でないことを確認してから実行
                    closeDialog?.Invoke();
                }
            });

            // closeDialog アクションの定義 (UI スレッドで直接実行)
            closeDialog = async () => // async を追加
            {
                try
                {
                    // ViewModel から取得した DialogCoordinator を使用
                    var context = metroWindow?.DataContext; // DataContext を取得
                    if (context != null && customDialog != null && dialogCoordinator != null) // context もチェック
                    {
                        // Progress<T> コールバックは UI スレッドで実行されるため、Dispatcher 不要
                        await dialogCoordinator.HideMetroDialogAsync(context, customDialog); // 第1引数を context に変更
                    }
                }
                catch (Exception ex)
                {
                    // ダイアログクローズ中のエラーログ
                    System.Diagnostics.Debug.WriteLine($"Error closing progress dialog automatically: {ex.Message}"); // ログ出力先を Debug に変更
                }
            };

            return (progress, closeDialog);
        }
    }
}
