using System.Collections.Generic;

namespace Illustra.Models
{
    public class NodeModel
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public List<NodeModel> Directories { get; set; } = new List<NodeModel>();
        public List<FileNodeModel> Files { get; set; } = new List<FileNodeModel>();
    }

    public class FileNodeModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
