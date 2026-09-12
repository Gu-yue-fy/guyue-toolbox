using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>一个可清理的隐私痕迹项。</summary>
    public sealed class PrivacyItem
    {
        public string Id;
        public string Name;
        public string Description;
        public int Count;
        public bool Scanned;
        public bool Selected = true;
        public string LastError;
    }

    /// <summary>
    /// 清理使用痕迹（最近文档、运行历史、对话框历史、地址栏历史、剪贴板）。
    /// 只删除记录本身，不影响任何程序功能；相关列表会在后续使用中自动重建。
    /// </summary>
    public static class PrivacyCleaner
    {
        public static List<PrivacyItem> BuildItems()
        {
            List<PrivacyItem> list = new List<PrivacyItem>();
            list.Add(Item("recent", "最近使用的文档",
                "资源管理器「最近使用」列表及其快捷方式记录。"));
            list.Add(Item("runmru", "「运行」对话框历史",
                "Win+R 输入过的命令与路径记录。"));
            list.Add(Item("comdlg", "打开 / 保存对话框历史",
                "各程序「打开 / 保存」窗口记录的最近访问位置。"));
            list.Add(Item("typedpaths", "地址栏输入历史",
                "资源管理器地址栏中手动输入过的路径。"));
            list.Add(Item("clipboard", "剪贴板内容",
                "当前驻留在系统剪贴板中的内容（可能含敏感文本）。"));
            return list;
        }

        private static PrivacyItem Item(string id, string name, string desc)
        {
            PrivacyItem it = new PrivacyItem();
            it.Id = id;
            it.Name = name;
            it.Description = desc;
            return it;
        }

        private static string RecentDir()
        {
            try
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Recent");
            }
            catch
            {
                return "";
            }
        }

        // --------------------------------------------------------------
        // 扫描（剪贴板项由视图在 UI 线程处理）
        // --------------------------------------------------------------

        public static void Scan(PrivacyItem it)
        {
            it.Count = 0;
            it.LastError = null;

            try
            {
                switch (it.Id)
                {
                    case "recent":
                        it.Count = CountFiles(RecentDir()) + CountRegValues(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs");
                        break;
                    case "runmru":
                        it.Count = CountRegValues(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
                        break;
                    case "comdlg":
                        it.Count = CountRegSubKeys(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32", true);
                        break;
                    case "typedpaths":
                        it.Count = CountRegValues(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths");
                        break;
                }
            }
            catch (Exception ex)
            {
                it.LastError = ex.Message;
            }
            it.Scanned = true;
        }

        /// <summary>剪贴板扫描（必须在 UI 线程调用）。</summary>
        public static int ScanClipboard()
        {
            try
            {
                System.Windows.Forms.IDataObject data = System.Windows.Forms.Clipboard.GetDataObject();
                if (data == null) return 0;
                string[] formats = data.GetFormats();
                return formats == null ? 0 : formats.Length;
            }
            catch
            {
                return 0;
            }
        }

        // --------------------------------------------------------------
        // 清理（剪贴板项由视图在 UI 线程处理）
        // --------------------------------------------------------------

        public static bool Clean(PrivacyItem it, out string error)
        {
            error = "";
            try
            {
                switch (it.Id)
                {
                    case "recent":
                        DeleteFiles(RecentDir());
                        DeleteRegChildren(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs");
                        break;
                    case "runmru":
                        DeleteRegValues(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
                        break;
                    case "comdlg":
                        DeleteRegChildren(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32");
                        break;
                    case "typedpaths":
                        DeleteRegValues(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths");
                        break;
                }
                it.Count = 0;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>清空剪贴板（必须在 UI 线程调用）。</summary>
        public static bool CleanClipboard(out string error)
        {
            error = "";
            try
            {
                System.Windows.Forms.Clipboard.Clear();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // --------------------------------------------------------------
        // 工具
        // --------------------------------------------------------------

        private static int CountFiles(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            try { return Directory.GetFiles(dir).Length; }
            catch { return 0; }
        }

        private static void DeleteFiles(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    File.SetAttributes(files[i], FileAttributes.Normal);
                    File.Delete(files[i]);
                }
                catch
                {
                }
            }
        }

        private static RegistryKey OpenCur(string path, bool writable)
        {
            try { return Registry.CurrentUser.OpenSubKey(path, writable); }
            catch { return null; }
        }

        private static int CountRegValues(string path)
        {
            using (RegistryKey k = OpenCur(path, false))
            {
                if (k == null) return 0;
                int n = k.GetValueNames().Length;
                n += k.GetSubKeyNames().Length;
                return n;
            }
        }

        private static int CountRegSubKeys(string path, bool recursive)
        {
            int total = 0;
            using (RegistryKey k = OpenCur(path, false))
            {
                if (k == null) return 0;
                string[] subs = k.GetSubKeyNames();
                total += subs.Length;
                if (recursive)
                {
                    for (int i = 0; i < subs.Length; i++)
                    {
                        total += CountRegSubKeys(path + "\\" + subs[i], true);
                    }
                }
            }
            return total;
        }

        private static void DeleteRegValues(string path)
        {
            using (RegistryKey k = OpenCur(path, true))
            {
                if (k == null) return;
                string[] names = k.GetValueNames();
                for (int i = 0; i < names.Length; i++)
                {
                    try { k.DeleteValue(names[i], false); }
                    catch { }
                }
            }
        }

        private static void DeleteRegChildren(string path)
        {
            DeleteRegValues(path);
            using (RegistryKey k = OpenCur(path, true))
            {
                if (k == null) return;
                string[] subs = k.GetSubKeyNames();
                for (int i = 0; i < subs.Length; i++)
                {
                    try { k.DeleteSubKeyTree(subs[i], false); }
                    catch { }
                }
            }
        }
    }
}
