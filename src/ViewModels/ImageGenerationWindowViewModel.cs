using System.Collections.ObjectModel;
using System.Windows.Input;
using Prism.Mvvm;
using Illustra.Commands;

namespace Illustra.ViewModels
{
    public class ImageGenerationWindowViewModel : BindableBase
    {
        private string _serverUrl;
        private PromptEditorViewModel _activeEditor;

        public string ServerUrl
        {
            get => _serverUrl;
            set => SetProperty(ref _serverUrl, value);
        }
        public string ReforgePath { get; set; } = string.Empty;

        public ObservableCollection<PromptEditorViewModel> Editors { get; }

        public PromptEditorViewModel ActiveEditor
        {
            get => _activeEditor;
            set => SetProperty(ref _activeEditor, value);
        }

        public ICommand AddEditorCommand { get; }
        public ICommand CloseEditorCommand { get; }
        public ICommand GenerateCommand { get; }

        public ImageGenerationWindowViewModel()
        {
            Editors = new ObservableCollection<PromptEditorViewModel>();

            // コマンドの初期化
            AddEditorCommand = new RelayCommand(ExecuteAddEditor);
            CloseEditorCommand = new RelayCommand<PromptEditorViewModel>(ExecuteCloseEditor);
            GenerateCommand = new RelayCommand(ExecuteGenerate);

            // 初期エディタを作成
            CreateInitialEditor();

            // テスト用のサンプルタグを追加
            if (ActiveEditor != null)
            {
                ActiveEditor.Tags.Add(new PromptTagViewModel
                {
                    Text = "1girl",
                    Order = 0,
                    IsEnabled = true
                });

                ActiveEditor.Tags.Add(new PromptTagViewModel
                {
                    Text = "solo",
                    Order = 1,
                    IsEnabled = true
                });

                ActiveEditor.Tags.Add(new PromptTagViewModel
                {
                    Text = "looking_at_viewer",
                    Order = 2,
                    IsEnabled = true
                });
            }
        }

        private void CreateInitialEditor()
        {
            var editor = new PromptEditorViewModel
            {
                Title = "メインプロンプト",
                Order = 0
            };
            Editors.Add(editor);
            ActiveEditor = editor;
        }

        private void ExecuteAddEditor()
        {
            var editor = new PromptEditorViewModel
            {
                Title = $"追加プロンプト {Editors.Count}",
                Order = Editors.Count
            };
            Editors.Add(editor);
            ActiveEditor = editor;
        }

        private void ExecuteCloseEditor(PromptEditorViewModel editor)
        {
            if (editor == null || Editors.Count <= 1) return;

            int index = Editors.IndexOf(editor);
            Editors.Remove(editor);

            // エディタの順序を更新
            UpdatePromptOrder();

            // アクティブエディタを設定
            if (ActiveEditor == editor)
            {
                ActiveEditor = Editors[Math.Max(0, index - 1)];
            }
        }

        private void ExecuteGenerate()
        {
            // プロンプトの生成処理
            string prompt = GetIntegratedPrompt();
            // TODO: 生成処理の実装
        }

        public void UpdatePromptOrder()
        {
            int order = 0;
            foreach (var editor in Editors)
            {
                editor.Order = order++;
            }
        }

        public string GetIntegratedPrompt()
        {
            return string.Join(", ",
                Editors.OrderBy(e => e.Order)
                      .SelectMany(e => e.Tags
                          .Where(t => t.IsEnabled)
                          .OrderBy(t => t.Order)
                          .Select(t => t.Text)));
        }
    }
}
