using UnityEditor;
using UnityEngine;

namespace Ilves.EditorTools
{
    /// <summary>
    /// 主界面美术与图标的导入规格（AssetPostprocessor 强制定规矩，不依赖手改 .meta）。
    /// 为什么要写这个：Unity 对 Resources 里的 PNG 默认开 mipmap + 平台压缩，
    /// 标题字标是带柔和 alpha 边的橄榄绿线稿，一压就成了锯齿脏边；
    /// 主界面背景是同色调暗照片，默认压缩也会出色带。
    /// 这里把三张图的导入参数钉死，换图不用再去 Inspector 里手工调。
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        public const string TitleLogoPath = "Assets/Resources/art/title-logo.png";
        public const string TitleBgPath = "Assets/Resources/art/title-bg.jpg";
        public const string IconPath = "Assets/Art/icon.png";

        /// <summary>需要按 UI 贴图规矩导入的三张图（构建前会强制重导一次）。</summary>
        public static readonly string[] ArtPaths = { TitleLogoPath, TitleBgPath, IconPath };

        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            bool known = p == TitleLogoPath || p == TitleBgPath || p == IconPath;
            if (!known) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;                 // 界面贴图，近距离直绘，不要 mipmap
            ti.npotScale = TextureImporterNPOTScale.None;   // 1671×941 / 1363×349 都不是 2 的幂，不能缩放
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.sRGBTexture = true;
            ti.maxTextureSize = 2048;
            ti.isReadable = false;

            if (p == TitleLogoPath)
            {
                // 字标靠 alpha 过渡做柔边，压缩会把过渡带压出一圈灰边 → 不压缩
                ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else if (p == TitleBgPath)
            {
                ti.alphaIsTransparency = false;
                // 暗调照片，用高质量压缩（桌面走 BC7），体积约为未压缩的 1/4 而色带不可见
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.compressionQuality = 100;
            }
            else
            {
                // 应用图标母版：不压缩，给构建期生成 .ico / mipmap 留足信息量
                ti.alphaIsTransparency = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
            }
        }
    }
}
