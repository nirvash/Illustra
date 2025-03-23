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

        private static Dictionary<string, WINDOWPLACEMENT> _placements;

        public string SettingsIdentifier { get; set; } = "Default";

        private WINDOWPLACEMENT? _placement;

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

        public bool UpgradeSettings { get; set; } = false;

        public CustomPlacementSettings()
        {
        }

        public void Save()
        {
            Placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
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
                    _placements = JsonConvert.DeserializeObject<Dictionary<string, WINDOWPLACEMENT>>(json)
                                  ?? new Dictionary<string, WINDOWPLACEMENT>();
                }
                else
                {
                    _placements = new Dictionary<string, WINDOWPLACEMENT>();
                }
            }
            catch
            {
                _placements = new Dictionary<string, WINDOWPLACEMENT>();
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
