using Prism.Mvvm;
using System.Windows.Input;

namespace Illustra.Models
{
    public class KeyWrapper : BindableBase
    {
        private Key _key;
        private KeyboardShortcutModel _parentShortcut;
        private ModifierKeys _modifiers;

        public Key Key
        {
            get => _key;
            set => SetProperty(ref _key, value);
        }

        public KeyboardShortcutModel ParentShortcut
        {
            get => _parentShortcut;
            set => SetProperty(ref _parentShortcut, value);
        }

        public ModifierKeys Modifiers
        {
            get => _modifiers;
            set
            {
                if (SetProperty(ref _modifiers, value))
                {
                    RaisePropertyChanged(nameof(DisplayText));
                    if (ParentShortcut?.Modifiers != null)
                    {
                        ParentShortcut.Modifiers[Key] = value;
                    }
                }
            }
        }

        public string DisplayText
        {
            get
            {
                var modifiers = new List<string>();
                if ((Modifiers & ModifierKeys.Control) == ModifierKeys.Control) modifiers.Add("Ctrl");
                if ((Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) modifiers.Add("Alt");
                if ((Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) modifiers.Add("Shift");
                if ((Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) modifiers.Add("Win");

                return modifiers.Count > 0
                    ? $"{string.Join(" + ", modifiers)} + {Key}"
                    : Key.ToString();
            }
        }

        public KeyWrapper(KeyboardShortcutModel parent, Key key)
        {
            ParentShortcut = parent;
            Key = key;
            Modifiers = parent.Modifiers.TryGetValue(key, out var mods) ? mods : ModifierKeys.None;
        }
    }
}
