using System.Collections.Generic;
using System.Windows.Input;

namespace Illustra.Models
{
    public class KeyboardShortcutSetting
    {
        public string FunctionId { get; set; }
        public List<Key> Keys { get; set; }
        public Dictionary<Key, ModifierKeys> Modifiers { get; set; }
    }
}
