using System;
using System.Collections.Generic;
using System.Management;

namespace SysToolbox.Core
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
        public static List<RestorePoint> List()
        {
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
            catch
            {
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
                {
                    mc.InvokeMethod("CreateRestorePoint", new object[] { description, 12, 100 });
                }
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
                {
                    mc.InvokeMethod("DeleteRestorePoint", new object[] { sequence });
                }
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
