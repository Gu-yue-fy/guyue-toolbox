﻿/* ============================================================
 * 文件说明：优化中心 · 方案库与批量操作：方案保存/导入导出、一键推荐、全部还原。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public partial class OptimizeView
    {

        // ---------- 内置方案库 ----------

        private const string ProfilesRoot = @"Software\GuyueBox\Profiles";

        private static string[] ListProfiles()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(ProfilesRoot))
                {
                    if (k == null) return new string[0];
                    return k.GetSubKeyNames();
                }
            }
            catch { return new string[0]; }
        }

        private static string ReadProfile(string name)
        {
            try
            {
                return Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\" + ProfilesRoot + "\\" + name, "ids", "") as string ?? "";
            }
            catch { return ""; }
        }

        private void OnSaveProfileClick(object sender, EventArgs e)
        {
            if (_groupFilter != null)
            {
                SetSubtitle("当前只显示「" + _groupFilter + "」分类——请先切回「全部」再保存方案，否则方案不完整。", Theme.Warning);
                return;
            }
            List<string> ids = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsApplied) ids.Add(_rows[i].Tweak.Id);
            }
            if (ids.Count == 0)
            {
                Dialog.Info(this, "空方案", "当前没有任何已启用的优化项，没有可保存的内容。");
                return;
            }
            string name = Dialog.Input(this, "保存方案", "方案名称（如：游戏模式 / 日常）：", "");
            if (name == null || name.Trim().Length == 0) return;
            name = name.Trim();
            if (name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0) name = name.Replace('\\', '_').Replace('/', '_');

            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\" + ProfilesRoot + "\\" + name, "ids",
                string.Join(",", ids.ToArray()));
            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\" + ProfilesRoot + "\\" + name, "saved",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            BuildProfileChips();
            SetSubtitle("方案「" + name + "」已保存（" + ids.Count + " 项）。点击方案名即可一键切换。", Theme.Success);
        }

        private void ApplyProfile(string name)
        {
            if (_busy) return;
            if (_groupFilter != null)
            {
                SetSubtitle("当前只显示「" + _groupFilter + "」分类——请先切回「全部」再应用方案，否则其余分类不会被同步。", Theme.Warning);
                return;
            }
            string raw = ReadProfile(name);
            if (raw.Length == 0)
            {
                Dialog.Info(this, "空方案", "方案「" + name + "」没有记录任何优化项。");
                return;
            }
            Dictionary<string, bool> want = new Dictionary<string, bool>();
            foreach (string id in raw.Split(','))
            {
                if (id.Trim().Length > 0) want[id.Trim()] = true;
            }

            List<TweakRow> toApply = new List<TweakRow>();
            List<TweakRow> toRevert = new List<TweakRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                TweakRow row = _rows[i];
                bool inProfile;
                if (!want.TryGetValue(row.Tweak.Id, out inProfile))
                {
                    if (row.IsApplied) toRevert.Add(row);
                    continue;
                }
                if (!row.IsApplied) toApply.Add(row);
            }

            if (toApply.Count == 0 && toRevert.Count == 0)
            {
                SetSubtitle("当前状态与方案「" + name + "」一致。", Theme.TextSecondary);
                return;
            }
            if (!Dialog.Confirm(this, "应用方案",
                "将同步到方案「" + name + "」：\r\n\r\n  · 启用 " + toApply.Count + " 项\r\n  · 还原 " + toRevert.Count + " 项\r\n\r\n继续吗？"))
            {
                return;
            }

            List<TweakRow> all = new List<TweakRow>();
            all.AddRange(toApply);
            all.AddRange(toRevert);
            ImportRun(all, toApply.Count, toRevert.Count);
        }

        private void DeleteProfile(string name)
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ProfilesRoot + "\\" + name, false);
            }
            catch { }
            BuildProfileChips();
            SetSubtitle("方案「" + name + "」已删除。", Theme.TextSecondary);
        }

        /// <summary>重建方案 chips（有方案才显示整行区域）。左键应用，右键删除。</summary>
        private void BuildProfileChips()
        {
            _profileChips.FlowDirection = FlowDirection.LeftToRight;
            _profileChips.WrapContents = false;
            _profileChips.BackColor = Theme.WindowBg;
            _profileChips.Height = 28;
            _profileChips.AutoSize = true; // 宽度随内容自适应，避免提示文字被默认 200px 宽度裁剪
            _profileChips.Controls.Clear();

            string[] names = ListProfiles();
            if (names.Length == 0)
            {
                Label empty = new Label();
                empty.Text = "暂无保存的方案——先按需要开好优化，再点「＋保存方案」";
                empty.ForeColor = Theme.TextMuted;
                empty.Font = Theme.FontSmall;
                empty.AutoSize = true;
                empty.Margin = new Padding(0, 6, 0, 0);
                _profileChips.Controls.Add(empty);
                return;
            }

            Label cap = new Label();
            cap.Text = "我的方案:";
            cap.ForeColor = Theme.TextMuted;
            cap.Font = Theme.FontSmall;
            cap.AutoSize = true;
            cap.Margin = new Padding(16, 6, 8, 0);
            _profileChips.Controls.Add(cap);

            foreach (string name in names)
            {
                string captured = name;
                AccentButton chip = new AccentButton();
                chip.Text = captured;
                chip.Variant = ButtonVariant.Ghost;
                chip.Height = 28;
                chip.FitToText(72);
                chip.Margin = new Padding(0, 0, 8, 0);
                chip.Click += delegate { ApplyProfile(captured); };
                chip.MouseUp += delegate (object s, MouseEventArgs me)
                {
                    if (me.Button == MouseButtons.Right &&
                        Dialog.Confirm(this, "删除方案", "删除方案「" + captured + "」吗？"))
                    {
                        DeleteProfile(captured);
                    }
                };
                _profileChips.Controls.Add(chip);
            }
        }

        // ---------- 优化方案导出 / 导入 ----------

        /// <summary>把当前所有「已启用」的优化项 Id 快照保存为方案文件。</summary>
        private void OnExportProfile(object sender, EventArgs e)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsApplied) ids.Add(_rows[i].Tweak.Id);
            }
            if (ids.Count == 0)
            {
                Dialog.Info(this, "空方案", "当前没有任何已启用的优化项，没有可保存的内容。");
                return;
            }

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "优化方案 (*.stprofile)|*.stprofile";
                d.FileName = "我的优化方案.stprofile";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string saveError = ProfileFile.Write(d.FileName, ids);
                if (saveError == null)
                {
                    SetSubtitle("方案已保存（" + ids.Count + " 项）：" + d.FileName, Theme.Success);
                }
                else
                {
                    Dialog.Error(this, "保存失败", saveError);
                }
            }
        }

        /// <summary>读取方案文件，把优化中心同步到方案记录的状态（应用缺失的、还原多余的）。</summary>
        private void OnImportProfile(object sender, EventArgs e)
        {
            if (_busy) return;

            string file;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "优化方案 (*.stprofile)|*.stprofile";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }

            // 解析收口到 Core（ProfileFile.Read）：此前在 UI 层用正则从文本抽 ids，
            // 不校验 app/版本、转义与非法字符会静默读坏；命令行 --apply 也用同一实现。
            string parseError;
            ProfileDocument doc = ProfileFile.Read(file, out parseError);
            if (doc == null)
            {
                Dialog.Error(this, "文件无效", parseError);
                return;
            }
            string[] ids = doc.Ids.ToArray();

            // 与当前状态求差
            Dictionary<string, bool> want = new Dictionary<string, bool>();
            foreach (string id in ids) want[id] = true;

            List<TweakRow> toApply = new List<TweakRow>();
            List<TweakRow> toRevert = new List<TweakRow>();
            int unknown = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                TweakRow row = _rows[i];
                bool inProfile;
                if (!want.TryGetValue(row.Tweak.Id, out inProfile))
                {
                    if (!row.IsApplied) continue; // 未启用且不在方案里：无需动
                    // 方案里没有但当前已启用 → 还原
                    toRevert.Add(row);
                    continue;
                }
                if (row.IsApplied) continue; // 已与方案一致
                toApply.Add(row);
            }
            // 统计方案中本库不存在的项
            Dictionary<string, bool> known = new Dictionary<string, bool>();
            for (int i = 0; i < _rows.Count; i++) known[_rows[i].Tweak.Id] = true;
            foreach (string id in ids)
            {
                if (!known.ContainsKey(id)) unknown++;
            }

            if (toApply.Count == 0 && toRevert.Count == 0)
            {
                SetSubtitle("当前状态与方案一致，无需变更" + (unknown > 0 ? "（方案中 " + unknown + " 项在本库不存在，已跳过）" : "") + "。",
                    Theme.TextSecondary);
                return;
            }

            string msg = "将把优化中心同步到该方案：\r\n\r\n  · 启用 " + toApply.Count + " 项\r\n  · 还原 " + toRevert.Count + " 项" +
                (unknown > 0 ? "\r\n\r\n注意：方案中 " + unknown + " 项在本库不存在，将跳过。" : "");
            for (int i = 0; i < doc.Warnings.Count; i++) msg += "\r\n\r\n提示：" + doc.Warnings[i];
            msg += "\r\n\r\n继续执行吗？";
            if (!Dialog.Confirm(this, "导入方案", msg)) return;

            List<TweakRow> all = new List<TweakRow>();
            all.AddRange(toApply);
            all.AddRange(toRevert);
            ImportRun(all, toApply.Count, toRevert.Count);
        }

        /// <summary>后台线程逐项执行方案同步。</summary>
        private void ImportRun(List<TweakRow> rows, int applyCount, int revertCount)
        {
            _busy = true;
            _loadGen++;              // 会话令牌：批量期间用户切分类/强制刷新，本批次的 UI 回调整体作废
            int gen = _loadGen;

            // 高危闸门（与单项开关一致）：批次含「谨慎项启用」时先确认/创建还原点
            bool hasRiskyApply = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Tweak.Risky && wantApply(rows, rows[i], applyCount))
                {
                    hasRiskyApply = true;
                    break;
                }
            }
            if (hasRiskyApply)
            {
                SetSubtitle("批次含谨慎项，正在确认系统还原点…", Theme.Warning);
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    bool rpOk;
                    string note = EnsureRecentRestorePoint("GuyueBox - 方案同步前", out rpOk);
                    try
                    {
                        Post(delegate
                        {
                            if (gen != _loadGen || IsDisposed || Disposing) return;
                            if (!rpOk)
                            {
                                // #22：Risky 项的还原点必须确认创建成功，失败即取消整批应用
                                SetSubtitle("还原点创建失败，已取消本次方案同步：" + note, Theme.Danger);
                                Dialog.Warn(this, "还原点创建失败",
                                    "谨慎项应用前必须确保有系统还原点兜底，但创建失败了：\r\n" + note +
                                    "\r\n\r\n本次同步已取消。请检查系统保护是否开启（系统属性 → 系统保护）后重试。");
                                return;
                            }
                            SetSubtitle(note.Length > 0 ? note + "，开始同步方案…" : "开始同步方案…", Theme.Warning);
                            ImportRunCore(rows, applyCount, revertCount, gen);
                        });
                    }
                    catch { }
                });
                return;
            }
            ImportRunCore(rows, applyCount, revertCount, gen);
        }

        /// <summary>24 小时内已有还原点则跳过，否则创建一个（实现已下沉到 Core 的
        /// RestorePoints.EnsureRecent，命令行 --apply 无人值守走同一道闸门）。
        /// 返回给用户看的备注；ok=false 表示还原点创建失败（Risky 项应用必须就此取消——#22 校验要求）。</summary>
        private static string EnsureRecentRestorePoint(string title, out bool ok)
        {
            return RestorePoints.EnsureRecent(title, out ok);
        }

        private void ImportRunCore(List<TweakRow> rows, int applyCount, int revertCount, int gen)
        {
            SetSubtitle("正在同步方案（0/" + rows.Count + "）…", Theme.Warning);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int done = 0, failed = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    TweakRow row = rows[i];
                    bool target = wantApply(rows, row, applyCount);
                    try
                    {
                        bool ok = target ? TweakExecutor.Apply(row.Tweak).IsOk : TweakExecutor.Revert(row.Tweak).IsOk;
                        if (!ok) failed++;
                    }
                    catch
                    {
                        failed++;
                    }
                    done++;
                    try
                    {
                        Post(delegate
                        {
                            if (gen != _loadGen) return; // 界面已重建，不再触碰旧行
                            if (row.IsDisposed) return;
                            row.SetState(row.Tweak.IsApplied());
                            _states[row.Tweak.Id] = row.Tweak.IsApplied();
                            SetSubtitle("正在同步方案（" + done + "/" + rows.Count + "）…", Theme.Warning);
                        });
                    }
                    catch
                    {
                    }
                }
                try
                {
                    Post(delegate
                    {
                        if (gen != _loadGen) return; // 界面已重建：状态以新会话的探测为准
                        _busy = false;
                        UpdateSummary();
                        SetSubtitle("方案同步完成：启用 " + applyCount + "、还原 " + revertCount +
                            (failed > 0 ? "，" + failed + " 项失败（详见状态列）" : "，全部成功") + "。",
                            failed > 0 ? Theme.Warning : Theme.Success);
                    });
                }
                catch
                {
                }
            });
        }

        private static bool wantApply(List<TweakRow> rows, TweakRow row, int applyCount)
        {
            // ImportRun 的 rows 前 applyCount 个是待应用，其余是待还原
            return rows.IndexOf(row) < applyCount;
        }

        private void OnRecommendedClick(object sender, EventArgs e)
        {
            if (_busy) return;

            // 单一数据源：与「推荐」筛选一致，直接以 ITweak.Recommended 为准
            List<ITweak> targets = new List<ITweak>();
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool applied;
                if (!_states.TryGetValue(_tweaks[i].Id, out applied)) applied = false;
                if (applied) continue;
                if (_tweaks[i].Recommended) targets.Add(_tweaks[i]);
            }

            if (targets.Count == 0)
            {
                Dialog.Info(this, "无需优化", "推荐项目都已经处于启用状态。");
                return;
            }

            string list = "";
            for (int i = 0; i < targets.Count; i++) list += "· " + targets[i].Name + "\r\n";

            if (!Dialog.Confirm(this, "一键推荐优化",
                "将启用以下 " + targets.Count + " 项推荐优化：\r\n\r\n" + list +
                "\r\n所有改动都会被备份，可随时单独还原。是否继续？"))
                return;

            int okCount = 0;
            int failCount = 0;
            int adminNeeded = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                ITweak t = targets[i];
                if (!TweakApplicability.IsApplicable(t)) continue; // 不适用本机硬件的专属项直接跳过
                if (t.AdminOnly && !Native.IsElevated())
                {
                    adminNeeded++;
                    failCount++;
                    continue;
                }
                if (TweakExecutor.Apply(t).IsOk)
                {
                    okCount++;
                    _states[t.Id] = true;
                }
                else
                {
                    failCount++;
                }
            }

            SyncRows();
            UpdateSummary();

            string text = "成功启用 " + okCount + " 项优化。";
            if (failCount > 0) text += "\r\n有 " + failCount + " 项未能应用。";
            if (adminNeeded > 0)
            {
                text += "\r\n\r\n其中 " + adminNeeded + " 项需要管理员权限，请以管理员身份重新运行后再试。";
            }
            text += "\r\n\r\n部分设置需要重启或重新登录后才会生效。";

            SetSubtitle("一键优化完成：" + okCount + " 项成功。", failCount > 0 ? Theme.Warning : Theme.Success);
            Dialog.Success(this, "一键优化", text);
        }

        private void OnRestoreAllClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<ITweak> appliedList = new List<ITweak>();
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_tweaks[i].Id, out a);
                if (a) appliedList.Add(_tweaks[i]);
            }

            if (appliedList.Count == 0)
            {
                Dialog.Info(this, "无需还原", "当前没有已启用的优化项。");
                return;
            }

            if (!Dialog.Confirm(this, "全部还原",
                "将把 " + appliedList.Count + " 项优化全部还原为原状态。\r\n\r\n" +
                "还原依据的是启用时自动保存的备份，因此可以准确恢复。是否继续？"))
                return;

            int okCount = 0;
            int failCount = 0;
            for (int i = 0; i < appliedList.Count; i++)
            {
                if (TweakExecutor.Revert(appliedList[i]).IsOk)
                {
                    okCount++;
                    _states[appliedList[i].Id] = false;
                }
                else
                {
                    failCount++;
                }
            }

            SyncRows();
            UpdateSummary();

            SetSubtitle("已还原 " + okCount + " 项。", failCount > 0 ? Theme.Warning : Theme.Success);
            Dialog.Success(this, "还原完成",
                "成功还原 " + okCount + " 项。" +
                (failCount > 0 ? "\r\n" + failCount + " 项没有备份记录，已保持当前状态。" : "") +
                "\r\n\r\n部分设置需要重启后生效。");
        }

    }
}
