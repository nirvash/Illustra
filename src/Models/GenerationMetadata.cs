using System;
using System.Collections.Generic;

namespace Illustra.Models
{
    /// <summary>
    /// 動画等のメディアファイルに埋め込まれた生成メタデータを表すモデル。
    /// Stable Diffusion 固有の構造に依存せず、ComfyUI 等の各種生成ツールの
    /// メタデータを汎用的に保持する。
    /// </summary>
    public class GenerationMetadata
    {
        /// <summary>
        /// 生成ツールの種類（"ComfyUI" など）
        /// </summary>
        public string Generator { get; set; } = string.Empty;

        /// <summary>
        /// モデル名（チェックポイント / UNET 等）
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// プロンプト本文（長文テキストを想定）
        /// </summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// ネガティブプロンプト（存在する場合）
        /// </summary>
        public string NegativePrompt { get; set; } = string.Empty;

        /// <summary>
        /// 使用された LoRA のリスト
        /// </summary>
        public List<string> Loras { get; set; } = new List<string>();

        /// <summary>
        /// サンプラー等の生成パラメータ（steps / cfg / seed 等）
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 生の workflow JSON（ComfyUI への再読み込み用）
        /// </summary>
        public string RawWorkflowJson { get; set; } = string.Empty;

        /// <summary>
        /// workflow JSON が埋め込まれているかどうか
        /// （解析に失敗しても埋め込み自体は ComfyUI で開けるため表示対象になる）
        /// </summary>
        public bool HasWorkflow => !string.IsNullOrEmpty(RawWorkflowJson);

        /// <summary>
        /// プロンプトやモデル等の解析に成功したかどうか
        /// </summary>
        public bool ParseSuccess { get; set; }

        /// <summary>
        /// 解析失敗によるフォールバック表示（ワークフロー埋め込みのみ）が必要かどうか
        /// </summary>
        public bool NeedsWorkflowNotice => !ParseSuccess && HasWorkflow;

        /// <summary>
        /// 表示すべき内容を持つかどうか
        /// </summary>
        public bool HasContent => ParseSuccess || HasWorkflow;
    }
}
