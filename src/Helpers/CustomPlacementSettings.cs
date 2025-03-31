using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MahApps.Metro.Controls;
using ControlzEx.Standard;
using Newtonsoft.Json;

namespace Illustra.Helpers
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }


    public class CustomPlacementSettings : IWindowPlacementSettings
    {
        private static readonly string SettingsFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Illustra", "WindowPlacements.json");

#pragma warning disable CS0618 // Type or member is obsolete
        private static Dictionary<string, WINDOWPLACEMENT> _placements;
#pragma warning restore CS0618 // Type or member is obsolete

        public string SettingsIdentifier { get; set; } = "Default";

#pragma warning disable CS0618 // Type or member is obsolete
        private WINDOWPLACEMENT? _placement;

#pragma warning restore CS0618 // Type or member is obsolete
// Pragma moved to cover the entire property definition
#pragma warning disable CS0618 // Type or member is obsolete
        public WINDOWPLACEMENT Placement
        {
            get
            {
                if (_placement == null)
                {
                    LoadPlacements(); // 🔥 初回アクセス時にロード！

                    if (_placements.TryGetValue(SettingsIdentifier, out var saved))
                    {
                        _placement = saved;
                    }
                    else
                    {
                        _placement = new WINDOWPLACEMENT();
                    }

                    _placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                }

                return _placement;
            }
            set => _placement = value;
        }
// Pragma moved to cover the entire property definition
#pragma warning restore CS0618 // Type or member is obsolete

        public bool UpgradeSettings { get; set; } = false;

        public CustomPlacementSettings()
        {
        }

        public void Save()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            Placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
#pragma warning restore CS0618 // Type or member is obsolete
            _placements[SettingsIdentifier] = this.Placement;
            SavePlacements();
        }

        public void Reload()
        {
            LoadPlacements();

            if (_placements.TryGetValue(SettingsIdentifier, out var saved))
            {
                Placement = saved;
            }
        }

        public void Upgrade()
        {
            // 今は何もしない（必要なら古い形式から移行処理を書く）
        }

        private static void LoadPlacements()
        {
            if (_placements != null) return;

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
#pragma warning disable CS0618 // Type or member is obsolete
                    _placements = JsonConvert.DeserializeObject<Dictionary<string, WINDOWPLACEMENT>>(json)
                                  ?? new Dictionary<string, WINDOWPLACEMENT>();
#pragma warning restore CS0618 // Type or member is obsolete
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    _placements = new Dictionary<string, WINDOWPLACEMENT>();
#pragma warning restore CS0618 // Type or member is obsolete
                }
            }
            catch
            {
#pragma warning disable CS0618 // Type or member is obsolete
                _placements = new Dictionary<string, WINDOWPLACEMENT>();
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        private static void SavePlacements()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonConvert.SerializeObject(_placements, Formatting.Indented);

                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                // 保存エラーは握りつぶすかログ出す
                System.Diagnostics.Debug.WriteLine($"Window placement save failed: {ex.Message}");
            }
        }
    }
}
