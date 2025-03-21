using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Illustra.Models;

namespace Illustra.Helpers
{
    public static class FolderSortHelper
    {
        public static IEnumerable<FileSystemItemModel> Sort(IEnumerable<FileSystemItemModel> items, SortType type, bool ascending)
        {
            if (type == SortType.Name)
            {
                var comparer = Comparer<string>.Create((a, b) => NaturalSortHelper.Compare(a, b));
                return ascending
                    ? items.OrderBy(x => x.Name, comparer)
                    : items.OrderByDescending(x => x.Name, comparer);
            }
            else // Created
            {
                return ascending
                    ? items.OrderBy(x => new DirectoryInfo(x.FullPath).CreationTime)
                    : items.OrderByDescending(x => new DirectoryInfo(x.FullPath).CreationTime);
            }
        }
    }
}
