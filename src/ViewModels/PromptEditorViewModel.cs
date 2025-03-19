using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Illustra.Commands;
using Prism.Mvvm;

namespace Illustra.ViewModels
{
    public class PromptEditorViewModel : BindableBase
    {
        private string _title;
        private int _order;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public int Order
        {
            get => _order;
            set => SetProperty(ref _order, value);
        }

        public ObservableCollection<PromptTagViewModel> Tags { get; }
        public ObservableCollection<TagCategoryViewModel> Categories { get; }

        public ICommand AddTagCommand { get; }
        public ICommand RemoveTagCommand { get; }
        public ICommand EnableTagCommand { get; }
        public ICommand DisableTagCommand { get; }
        public ICommand MoveTagCommand { get; }

        public PromptEditorViewModel()
        {
            Tags = new ObservableCollection<PromptTagViewModel>();
            Categories = new ObservableCollection<TagCategoryViewModel>();

            // コマンドの初期化
            AddTagCommand = new RelayCommand<string>(ExecuteAddTag);
            RemoveTagCommand = new RelayCommand<PromptTagViewModel>(ExecuteRemoveTag);
            EnableTagCommand = new RelayCommand<PromptTagViewModel>(ExecuteEnableTag);
            DisableTagCommand = new RelayCommand<PromptTagViewModel>(ExecuteDisableTag);
            MoveTagCommand = new RelayCommand<(int oldIndex, int newIndex)>(tuple => MoveTag(tuple.oldIndex, tuple.newIndex));
        }

        private void ExecuteAddTag(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            Tags.Add(new PromptTagViewModel
            {
                Text = text,
                Order = Tags.Count,
                IsEnabled = true
            });
        }

        private void ExecuteRemoveTag(PromptTagViewModel tag)
        {
            if (tag != null)
            {
                Tags.Remove(tag);
                // タグの順序を更新
                for (int i = 0; i < Tags.Count; i++)
                {
                    Tags[i].Order = i;
                }
            }
        }

        private void ExecuteEnableTag(PromptTagViewModel tag)
        {
            if (tag != null)
            {
                tag.IsEnabled = true;
            }
        }

        private void ExecuteDisableTag(PromptTagViewModel tag)
        {
            if (tag != null)
            {
                tag.IsEnabled = false;
            }
        }

        public void MoveTag(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= Tags.Count ||
                newIndex < 0 || newIndex >= Tags.Count)
                return;

            var tag = Tags[oldIndex];
            Tags.RemoveAt(oldIndex);
            Tags.Insert(newIndex, tag);

            // タグの順序を更新
            for (int i = 0; i < Tags.Count; i++)
            {
                Tags[i].Order = i;
            }
        }

        public string ExportToText()
        {
            var enabledTags = Tags.Where(t => t.IsEnabled)
                                .OrderBy(t => t.Order)
                                .Select(t => t.Text);
            return string.Join(", ", enabledTags);
        }

        public void ImportFromText(string text)
        {
            // カンマで区切られたテキストをタグとして追加
            var tags = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(t => t.Trim())
                         .Where(t => !string.IsNullOrWhiteSpace(t));

            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(new PromptTagViewModel
                {
                    Text = tag,
                    Order = Tags.Count,
                    IsEnabled = true
                });
            }
        }
    }
}
