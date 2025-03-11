using System.Collections.Generic;
using System.Windows.Input;

namespace Illustra.Models
{
    public class KeyboardShortcutMetadata
    {
        public string FunctionId { get; set; }
        public string ResourceKey { get; set; } // 多言語用リソースキー
        public List<Key> DefaultKeys { get; set; }
        public Dictionary<Key, ModifierKeys> DefaultModifiers { get; set; }
    }
}
