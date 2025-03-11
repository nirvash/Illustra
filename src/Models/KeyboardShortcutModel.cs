using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Illustra.Functions;
using System.Text.Json;
using Illustra.Helpers;
using Prism.Mvvm;
using System.Windows;

namespace Illustra.Models
{
    public class KeyboardShortcutModel : BindableBase
    {
        private FuncId _functionId;
        private ObservableCollection<Key> _keys;
        private ObservableCollection<KeyWrapper> _wrappedKeys;
        private Dictionary<Key, ModifierKeys> _modifiers = new();

        private static readonly Dictionary<string, FuncId> FuncIdMap = typeof(FuncId)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(FuncId))
            .ToDictionary(f => ((FuncId)(f.GetValue(null) ?? throw new InvalidOperationException())).Value,
                         f => (FuncId)(f.GetValue(null) ?? throw new InvalidOperationException()));

        public string FunctionName
        {
            get => Application.Current.FindResource(ResourceKey) as string ?? ResourceKey;
        }

        public string DisplayName
        {
            get => $"{FunctionName} ({FunctionId.Value})";
        }

        private string _resourceKey;
        public string ResourceKey
        {
            get => _resourceKey;
            set => SetProperty(ref _resourceKey, value);
        }

        public FuncId FunctionId
        {
            get => _functionId;
            set => SetProperty(ref _functionId, value);
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<KeyWrapper> WrappedKeys
        {
            get => _wrappedKeys ??= new ObservableCollection<KeyWrapper>();
            private set => SetProperty(ref _wrappedKeys, value);
        }

        public ObservableCollection<Key> Keys
        {
            get => _keys ??= new ObservableCollection<Key>();
            set
            {
                if (_keys != null)
                {
                    _keys.CollectionChanged -= OnKeysCollectionChanged;
                }
                SetProperty(ref _keys, value);
                if (_keys != null)
                {
                    _keys.CollectionChanged += OnKeysCollectionChanged;
                    UpdateWrappedKeys();
                }
            }
        }

        public Dictionary<Key, ModifierKeys> Modifiers
        {
            get => _modifiers;
            set
            {
                SetProperty(ref _modifiers, value);
                UpdateWrappedKeys();
            }
        }

        public void UpdateModifiers(Key key, ModifierKeys modifiers)
        {
            _modifiers[key] = modifiers;
            UpdateWrappedKeys();
        }

        public KeyboardShortcutModel(FuncId functionId)
        {
            FunctionId = functionId;
            var metadata = KeyboardShortcutDefinitions.AllShortcuts.FirstOrDefault(m => m.FunctionId == functionId.Value);
            if (metadata != null)
            {
                ResourceKey = metadata.ResourceKey;
            }
        }

        private void OnKeysCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateWrappedKeys();
        }

        private void UpdateWrappedKeys()
        {
            var currentKeys = WrappedKeys.Select(w => w.Key).ToList();
            WrappedKeys.Clear();

            // 既存のキーの順序を維持
            foreach (var key in Keys)
            {
                WrappedKeys.Add(new KeyWrapper(this, key));
            }
        }

        public void ReplaceKey(Key oldKey, Key newKey)
        {
            var index = Keys.IndexOf(oldKey);
            if (index != -1)
            {
                Keys.RemoveAt(index);
                Keys.Insert(index, newKey);
            }
            else
            {
                Keys.Add(newKey);
            }

            UpdateWrappedKeys();
        }

        public static ObservableCollection<KeyboardShortcutModel> GetDefaultShortcuts()
        {
            return new ObservableCollection<KeyboardShortcutModel>(
                KeyboardShortcutDefinitions.GetDefaultSettings().Select(static setting => new KeyboardShortcutModel(FuncIdMap[setting.FunctionId])
                {
                    Keys = new ObservableCollection<Key>(setting.Keys),
                    Modifiers = setting.Modifiers ?? new Dictionary<Key, ModifierKeys>()
                })
            );
        }

        public static ObservableCollection<KeyboardShortcutModel> LoadShortcuts()
        {
            var settings = SettingsHelper.GetSettings();
            var shortcuts = new ObservableCollection<KeyboardShortcutModel>();

            // デフォルトのショートカット定義を保持
            var defaultShortcuts = KeyboardShortcutDefinitions.AllShortcuts;

            if (!string.IsNullOrEmpty(settings.KeyboardShortcuts))
            {
                try
                {
                    var serialized = JsonSerializer.Deserialize<List<KeyboardShortcutSetting>>(settings.KeyboardShortcuts);
                    if (serialized != null)
                    {
                        // 保存されているショートカットを読み込む
                        foreach (var s in serialized)
                        {
                            if (!FuncIdMap.TryGetValue(s.FunctionId, out var funcId))
                            {
                                funcId = FuncId.None;
                            }

                            var shortcut = new KeyboardShortcutModel(funcId)
                            {
                                Keys = new ObservableCollection<Key>(s.Keys),
                                Modifiers = s.Modifiers ?? []
                            };
                            shortcuts.Add(shortcut);
                        }

                        // デフォルト定義にあって保存データにない機能を追加
                        foreach (var def in defaultShortcuts)
                        {
                            if (!shortcuts.Any(s => s.FunctionId.Value == def.FunctionId))
                            {
                                if (FuncIdMap.TryGetValue(def.FunctionId, out var funcId))
                                {
                                    shortcuts.Add(new KeyboardShortcutModel(funcId)
                                    {
                                        Keys = new ObservableCollection<Key>(def.DefaultKeys),
                                        Modifiers = def.DefaultModifiers ?? []
                                    });
                                }
                            }
                        }

                        return shortcuts;
                    }
                }
                catch
                {
                    // デシリアライズに失敗した場合はデフォルト値を返す
                }
            }

            return GetDefaultShortcuts();
        }

        public static void SaveShortcuts(ObservableCollection<KeyboardShortcutModel> shortcuts)
        {
            var settings = SettingsHelper.GetSettings();
            var serialized = shortcuts.Select(static s => new KeyboardShortcutSetting
            {
                FunctionId = s.FunctionId.Value,
                Keys = s.Keys.ToList(),
                Modifiers = s.Modifiers
            }).ToList();

            settings.KeyboardShortcuts = JsonSerializer.Serialize(serialized);
            SettingsHelper.SaveSettings(settings);
        }
    }
}
