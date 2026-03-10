#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartOpen.Utils
{
    public static class ThumbnailProvider
    {
        /// <summary>
        /// ファイルパスからサムネイル（なければ関連付けアイコン）を取得。返り値は Freeze 済み。
        /// </summary>
        /// <param name="path">対象ファイルのフルパス</param>
        /// <param name="size">希望サイズ（ピクセル）</param>
        public static ImageSource? GetThumbnail(string path, int size)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return new DrawingImage();

                // 1) Windows Shell のサムネイル（WindowsAPICodePack があれば利用）
                var shell = TryGetShellThumbnail(path, size);
                if (shell != null)
                    return shell;
            }
            catch
            {
                // Shell まわりの例外は無視してフォールバック
            }

            // 2) 関連付けアイコン
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    // HICON から直接 WPF の ImageSource に変換
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(size, size));

                    src.Freeze();
                    return src;
                }
            }
            catch
            {
                // ここもフォールバック
            }

            // 3) 何も取れなかった場合でも null ではなく空の Image を返す
            return new DrawingImage();
        }

        /// <summary>
        /// WindowsAPICodePack.Shell を使って OS のサムネイルを取得（あれば）。
        /// 失敗したら null を返す。
        /// </summary>
        private static ImageSource? TryGetShellThumbnail(string path, int size)
        {
            try
            {
                // 型を直接指定して取得（アセンブリ名も含めて）
                var shellFileType = Type.GetType(
                    "Microsoft.WindowsAPICodePack.Shell.ShellFile, Microsoft.WindowsAPICodePack.Shell");

                if (shellFileType == null)
                    return null;

                // ShellFile.FromFilePath(string) を static メソッドとして取得
                var fromFilePath = shellFileType.GetMethod(
                    "FromFilePath",
                    new[] { typeof(string) });

                if (fromFilePath == null)
                    return null;

                // ShellFile インスタンス生成
                var shellFile = fromFilePath.Invoke(null, new object[] { path });
                if (shellFile == null)
                    return null;

                // Thumbnail プロパティ取得
                var thumbProp = shellFile.GetType().GetProperty("Thumbnail");
                var thumb = thumbProp?.GetValue(shellFile);
                if (thumb == null)
                    return null;

                // ExtraLargeBitmap / LargeBitmap のどちらかを順に試す
                var thumbType = thumb.GetType();
                var extraProp = thumbType.GetProperty("ExtraLargeBitmap");
                var largeProp = thumbType.GetProperty("LargeBitmap");

                Bitmap? bmp =
                    (Bitmap?)extraProp?.GetValue(thumb)
                    ?? (Bitmap?)largeProp?.GetValue(thumb);

                if (bmp == null)
                    return null;

                // ここでは ToImageSource を使って変換
                return ToImageSource(bmp, size);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 互換用ヘルパー。System.Drawing.Bitmap → WPF ImageSource 変換。
        /// 既存コードから ThumbnailProvider.ToImageSource(bmp) を呼べるようにしておく。
        /// </summary>
        internal static ImageSource? ToImageSource(Bitmap bmp, int size = 0)
        {
            try
            {
                using (bmp)
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;

                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    if (size > 0)
                    {
                        bi.DecodePixelWidth = size;
                    }
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
