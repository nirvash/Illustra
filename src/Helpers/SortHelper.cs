using Illustra.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Illustra.Helpers;

/// <summary>
/// ファイルノードのソート処理を提供するヘルパークラス
/// </summary>
public static class SortHelper
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    /// <summary>
    /// ファイルノードのリストをソートします
    /// </summary>
    /// <param name="nodes">ソート対象のファイルノードリスト</param>
    /// <param name="sortByDate">日付でソートする場合はtrue、名前でソートする場合はfalse</param>
    /// <param name="ascending">昇順でソートする場合はtrue、降順でソートする場合はfalse</param>
    public static void SortFileNodes(List<FileNodeModel> nodes, bool sortByDate, bool ascending)
    {
        if (nodes == null || nodes.Count == 0) return;

        nodes.Sort((a, b) =>
        {
            int compareResult;
            if (sortByDate)
            {
                // 日付でソート
                compareResult = a.LastModified.CompareTo(b.LastModified);

                // 日付が同じ場合はファイル名で二次ソート（Windowsのナチュラルソート）
                if (compareResult == 0)
                {
                    compareResult = StrCmpLogicalW(a.FileName, b.FileName);
                }
            }
            else
            {
                // 名前でソート（Windowsのナチュラルソート）
                compareResult = StrCmpLogicalW(a.FileName, b.FileName);
            }

            // 昇順/降順の設定を反映
            return ascending ? compareResult : -compareResult;
        });
    }
}

/// <summary>
/// 文字列の自然な並び順での比較を提供するクラス
/// </summary>
internal static class NaturalStringComparer
{
    public static int Compare(string? a, string? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        int pos1 = 0, pos2 = 0;

        while (pos1 < a.Length && pos2 < b.Length)
        {
            // 数字部分の開始位置を取得
            if (char.IsDigit(a[pos1]) && char.IsDigit(b[pos2]))
            {
                // 数字部分を比較
                int numStart1 = pos1;
                int numStart2 = pos2;

                while (pos1 < a.Length && char.IsDigit(a[pos1])) pos1++;
                while (pos2 < b.Length && char.IsDigit(b[pos2])) pos2++;

                int num1 = int.Parse(a[numStart1..pos1]);
                int num2 = int.Parse(b[numStart2..pos2]);

                if (num1 != num2)
                {
                    return num1.CompareTo(num2);
                }
            }
            else
            {
                // 文字部分を比較
                if (a[pos1] != b[pos2])
                {
                    return a[pos1].CompareTo(b[pos2]);
                }
                pos1++;
                pos2++;
            }
        }

        // 残りの長さで比較
        return (a.Length - pos1).CompareTo(b.Length - pos2);
    }
}