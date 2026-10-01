﻿/* ============================================================
 * 文件说明：系统还原点：枚举、创建与删除（WMI）
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Management;

namespace GuyueBox.Core
{
    /// <summary>一个系统还原点。</summary>
    public sealed class RestorePoint
    {
        public int Sequence;
        public string Description = "";
        public DateTime Created;
        public int Type;
        public bool CreatedParsed;

        public string TypeText
        {
            get
            {
                switch (Type)
                {
                    case 0: return "应用安装";
                    case 1: return "应用卸载";
                    case 10: return "驱动安装";
                    case 12: return "设置更改";
                    case 13: return "已取消";
                    default: return "其它 (" + Type + ")";
                }
            }
        }

        public string CreatedText
        {
            get { return CreatedParsed ? Created.ToString("yyyy-MM-dd HH:mm") : "—"; }
        }
    }
    /// <summary>通过 WMI (root\default:SystemRestore) 管理系统还原点。</summary>
    public static class RestorePoints
    {
        /// <summary>
        /// 谨慎项应用前的还原点闸门：24 小时内已有还原点则直接放行，否则创建一个。
        /// 返回给用户看的备注；ok=false 表示创建失败，调用方必须就此取消——
        /// 失败的谨慎项应用不能裸奔（#22 校验要求）。
        /// 原先是优化中心的私有方法；命令行 --apply 无人值守也要过同一道闸门，故下沉到 Core。
        /// </summary>
        public static string EnsureRecent(string title, out bool ok)
        {
            try
            {
                List<RestorePoint> points = List();
                for (int i = 0; i < points.Count; i++)
                {
                    if ((DateTime.Now - points[i].Created).TotalHours < 24) { ok = true; return ""; }
                }
            }
            catch
            {
            }
            string err;
            bool created = Create(title, out err);
            if (created) { ok = true; return "已创建系统还原点"; }
            ok = false;
            return string.IsNullOrEmpty(err) ? "还原点创建失败" : err;
        }

        public static List<RestorePoint> List()
        {
            string ignored;
            return List(out ignored);
        }

        /// <summary>枚举系统还原点；失败原因写入 error（页面据此区分"未开启系统还原"与"读取失败"）。</summary>
        public static List<RestorePoint> List(out string error)
        {
            error = "";
            List<RestorePoint> list = new List<RestorePoint>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    new ManagementScope(@"\\.\root\default"),
                    new ObjectQuery("SELECT * FROM SystemRestore"), null))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        try
                        {
                            RestorePoint p = new RestorePoint();
                            object seq = o["SequenceNumber"];
                            if (seq != null) p.Sequence = Convert.ToInt32(seq);
                            p.Description = (o["Description"] as string) ?? "";
                            object typ = o["RestorePointType"];
                            if (typ != null) p.Type = Convert.ToInt32(typ);
                            string ct = o["CreationTime"] as string;
                            DateTime dt;
                            if (TryParseWmiDate(ct, out dt))
                            {
                                p.Created = dt;
                                p.CreatedParsed = true;
                            }
                            list.Add(p);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            list.Sort(delegate (RestorePoint a, RestorePoint b)
            {
                if (!a.CreatedParsed && !b.CreatedParsed) return 0;
                if (!a.CreatedParsed) return 1;
                if (!b.CreatedParsed) return -1;
                return b.Created.CompareTo(a.Created);
            });
            return list;
        }

        public static bool Create(string description, out string error)
        {
            error = "";
            try
            {
                using (ManagementClass mc = new ManagementClass(@"\\.\root\default:SystemRestore"))
                using (ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 12;  // MODIFY_SETTINGS
                    inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
                    using (ManagementBaseObject ret = mc.InvokeMethod("CreateRestorePoint", inParams, null))
                    {
                        // ReturnValue=0 才是成功——系统保护被关闭时 WMI 不抛异常但返回错误码，
                        // 若不检查会让"高危项前置还原点闸门"形同虚设。
                        // 拿不到返回对象或 ReturnValue 一律按失败处理（此前折算成 0=成功，是假成功）
                        object rv = ret == null ? null : ret["ReturnValue"];
                        if (rv == null)
                        {
                            error = "创建还原点失败：WMI 未返回结果。请检查「系统保护」是否已开启，或安全软件是否拦截。";
                            TweakExecutor.RecordShell("系统还原点", "创建：" + description, false, error);
                            return false;
                        }
                        uint code = Convert.ToUInt32(rv);
                        if (code != 0)
                        {
                            error = "创建还原点失败（WMI ReturnValue=" + code + "）。请检查「系统保护」是否已开启。";
                            TweakExecutor.RecordShell("系统还原点", "创建：" + description, false, error);
                            return false;
                        }
                    }
                }
                TweakExecutor.RecordShell("系统还原点", "创建：" + description, true, "已创建");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Delete(int sequence, out string error)
        {
            error = "";
            try
            {
                using (ManagementClass mc = new ManagementClass(@"\\.\root\default:SystemRestore"))
                using (ManagementBaseObject inParams = mc.GetMethodParameters("DeleteRestorePoint"))
                {
                    inParams["SequenceNumber"] = sequence;
                    using (ManagementBaseObject ret = mc.InvokeMethod("DeleteRestorePoint", inParams, null))
                    {
                        // 同 Create：拿不到结果一律按失败处理，不能当作 ReturnValue=0（假成功）
                        object rv = ret == null ? null : ret["ReturnValue"];
                        if (rv == null)
                        {
                            error = "删除还原点失败：WMI 未返回结果。";
                            TweakExecutor.RecordShell("系统还原点", "删除：" + sequence, false, error);
                            return false;
                        }
                        uint code = Convert.ToUInt32(rv);
                        if (code != 0)
                        {
                            error = "删除还原点失败（WMI ReturnValue=" + code + "）。";
                            TweakExecutor.RecordShell("系统还原点", "删除：" + sequence, false, error);
                            return false;
                        }
                    }
                }
                TweakExecutor.RecordShell("系统还原点", "删除：" + sequence, true, "已删除");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryParseWmiDate(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrEmpty(s) || s.Length < 14) return false;
            try
            {
                int y = int.Parse(s.Substring(0, 4));
                int mo = int.Parse(s.Substring(4, 2));
                int d = int.Parse(s.Substring(6, 2));
                int h = int.Parse(s.Substring(8, 2));
                int mi = int.Parse(s.Substring(10, 2));
                int se = int.Parse(s.Substring(12, 2));
                dt = new DateTime(y, mo, d, h, mi, se);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
