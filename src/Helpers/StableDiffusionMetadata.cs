using System;
using System.Collections.Generic;

namespace StableDiffusionTools
{
    /// <summary>
    /// Stable Diffusionメタデータの解析結果を表す基本クラス
    /// </summary>
    public class StableDiffusionMetadata
    {
        /// <summary>
        /// ポジティブプロンプト
        /// </summary>
        public string PositivePrompt { get; set; } = string.Empty;

        /// <summary>
        /// ネガティブプロンプト
        /// </summary>
        public string NegativePrompt { get; set; } = string.Empty;

        /// <summary>
        /// モデル名
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// モデルハッシュ
        /// </summary>
        public string ModelHash { get; set; } = string.Empty;

        /// <summary>
        /// 元のメタデータ文字列
        /// </summary>
        public string RawMetadata { get; set; } = string.Empty;

        /// <summary>
        /// メタデータを含むかどうか
        /// </summary>
        public bool HasMetadata => !string.IsNullOrEmpty(RawMetadata);

        /// <summary>
        /// 生成元の種類（WebUI、ComfyUIなど）
        /// </summary>
        public string Generator { get; set; } = string.Empty;

        /// <summary>
        /// 解析できたかどうか
        /// </summary>
        public bool ParseSuccess { get; set; } = false;

        /// <summary>
        /// シード値
        /// </summary>
        public long Seed { get; set; } = -1;

        /// <summary>
        /// ステップ数
        /// </summary>
        public int Steps { get; set; } = 0;

        /// <summary>
        /// サンプラー名
        /// </summary>
        public string Sampler { get; set; } = string.Empty;

        /// <summary>
        /// CFG値
        /// </summary>
        public float CfgScale { get; set; } = 0;

        /// <summary>
        /// プロンプトから推測される主題
        /// </summary>
        public string EstimatedSubject { get; set; } = string.Empty;

        /// <summary>
        /// 使用されたLoRAのリスト
        /// </summary>
        public List<string> ModelLoras { get; set; } = new List<string>();
    }
}