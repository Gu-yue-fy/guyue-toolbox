using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI;
using GuyueBox.UI.Views;

namespace GuyueBox.Test
{
    /// <summary>
    /// 控制台冒烟测试：只做只读操作，用来验证核心逻辑与界面构造不会抛异常。
    /// 不会修改任何系统设置，也不会删除任何文件。
    /// </summary>
    internal static class SmokeTest
    {
        private static int _pass;
        private static int _fail;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Console.WriteLine("==== 古月工具包 · 冒烟测试 ====");
            Console.WriteLine();

            TestCore();
            TestScanner();
            TestStartup();
            TestProcess();
            TestNetwork();
            TestTweaks();
            TestUi();

            Console.WriteLine();
            Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项。");
            return _fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------

        private static void TestCore()
        {
            Section("系统信息");

            Run("FormatSize", delegate
            {
                Assert(SysInfo.FormatSize(0) == "0 B", "0 字节格式化异常：" + SysInfo.FormatSize(0));
                Assert(SysInfo.FormatSize(1024) == "1.0 KB", "1024 格式化异常：" + SysInfo.FormatSize(1024));
                Assert(SysInfo.FormatSize(1536).StartsWith("1.5"), "1536 格式化异常：" + SysInfo.FormatSize(1536));
                Assert(SysInfo.FormatSize(1024L * 1024 * 1024) == "1.0 GB",
                    "1GB 格式化异常：" + SysInfo.FormatSize(1024L * 1024 * 1024));
                Console.WriteLine("      示例：" + SysInfo.FormatSize(3221225472L));
            });

            Run("内存信息", delegate
            {
                MemoryInfo m = SysInfo.GetMemory();
                Assert(m.TotalBytes > 0, "未读取到物理内存总量。");
                Assert(m.UsedPercent >= 0 && m.UsedPercent <= 100, "内存占用率超出范围：" + m.UsedPercent);
                Console.WriteLine("      总量 " + SysInfo.FormatSize(m.TotalBytes) +
                    "，已用 " + m.UsedPercent.ToString("0.0") + "%");
            });

            Run("磁盘信息", delegate
            {
                List<DiskInfo> disks = SysInfo.GetDisks(true);
                Assert(disks.Count > 0, "未检测到本地磁盘。");
                for (int i = 0; i < disks.Count; i++)
                {
                    Console.WriteLine("      " + disks[i].Name + " " + disks[i].Label +
                        "  可用 " + SysInfo.FormatSize(disks[i].FreeBytes) +
                        " / 共 " + SysInfo.FormatSize(disks[i].TotalBytes));
                }
            });

            Run("已安装程序", delegate
            {
                List<ProgramEntry> list = Programs.Scan(false);
                Assert(list.Count > 0, "未从注册表读取到任何已安装程序。");
                int withSize = 0;
                int withLoc = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    Assert(!string.IsNullOrEmpty(list[i].Name), "存在空名称的程序条目。");
                    if (list[i].SizeBytes > 0) withSize++;
                    if (list[i].HasLocation) withLoc++;
                }
                ProgramEntry sample = list[0];
                Console.WriteLine("      已读取 " + list.Count + " 项，其中 " +
                    withSize + " 项有大小、 " + withLoc + " 项有安装目录。");
                Console.WriteLine("      示例：" + sample.Name +
                    (string.IsNullOrEmpty(sample.Publisher) ? "" : "（" + sample.Publisher + "）"));
                Assert(Programs.LaunchUninstall(null, false) == false, "空条目卸载应返回 false。");
            });

            Run("系统报告", delegate
            {
                string report = SystemReport.Build();
                Assert(!string.IsNullOrEmpty(report), "系统报告生成为空。");
                Assert(report.IndexOf("操作系统") >= 0, "系统报告缺少操作系统信息。");
                Assert(report.IndexOf("Windows 服务") >= 0, "系统报告缺少服务统计。");
                Console.WriteLine("      报告长度 " + report.Length + " 字符。");
            });

            Run("重复文件查找", delegate
            {
                string dir = Path.Combine(Path.GetTempPath(), "GuyueBoxDup_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "a.txt"), "hello-world");
                    File.WriteAllText(Path.Combine(dir, "b.txt"), "hello-world");
                    File.WriteAllText(Path.Combine(dir, "c.txt"), "different-content");
                    List<DuplicateGroup> groups = DuplicateFinder.Find(dir, false, null, null);
                    Assert(groups.Count == 1, "应找到 1 组重复，实际 " + groups.Count);
                    Assert(groups[0].Files.Count == 2, "该组应有 2 个文件，实际 " + groups[0].Files.Count);
                    Console.WriteLine("      重复组 " + groups.Count + " 个，每个 " + groups[0].Files.Count + " 文件。");
                }
                finally
                {
                    try { Directory.Delete(dir, true); }
                    catch { }
                }
            });

            Run("电源计划", delegate
            {
                List<PowerPlan> list = PowerPlans.List();
                Assert(list != null, "电源计划列表返回空引用。");
                Assert(list.Count > 0, "未读取到任何电源计划。");
                int active = 0;
                for (int i = 0; i < list.Count; i++) if (list[i].Active) active++;
                Assert(active == 1, "应有且仅有一个激活的电源计划，实际 " + active + " 个。");
                Console.WriteLine("      读取到 " + list.Count + " 个电源计划，激活 " + active + " 个。");
            });

            Run("右键菜单", delegate
            {
                List<ContextEntry> list = GuyueBox.Core.ContextMenu.List();
                Assert(list != null, "右键菜单列表返回空引用。");
                Console.WriteLine("      读取到 " + list.Count + " 个右键菜单项。");
            });

            Run("计划任务", delegate
            {
                List<ScheduledTask> list = ScheduledTasks.List();
                Assert(list != null, "计划任务列表返回空引用。");
                Console.WriteLine("      读取到 " + list.Count + " 个计划任务。");
            });

            Run("DNS 切换", delegate
            {
                List<string> adapters = DnsSwitch.ListAdapters();
                Assert(adapters != null, "网卡列表返回空引用。");
                Console.WriteLine("      读取到 " + adapters.Count + " 个网卡，预设 " + DnsSwitch.Presets.Length + " 个。");
            });

            Run("性能基准", delegate
            {
                double probe = Benchmark.QuickCpuProbe();
                Assert(probe > 0, "CPU 基准探测应返回正值。");
                Console.WriteLine("      单核探测 " + probe.ToString("0.00") + " M ops/s。");

                BenchmarkResult r = new BenchmarkResult();
                r.CpuSingleMops = 12.34;
                r.CpuMultiMops = 56.78;
                r.Score = 999;
                string line = r.ToLine();
                BenchmarkResult r2 = BenchmarkResult.FromLine(line);
                Assert(r2.Score == 999, "历史序列化/反序列化评分不一致。");
                Assert(Math.Abs(r2.CpuSingleMops - 12.34) < 0.01, "序列化 CPU 单核值不一致。");
            });

            Run("操作系统版本", delegate
            {
                SysInfo.OSVersionInfo os = SysInfo.GetOsVersion();
                Assert(!string.IsNullOrEmpty(os.Name), "未读取到系统名称。");
                Console.WriteLine("      " + os.Name + " / " + os.Version + " / " + os.Arch);
            });

            Run("CPU 负载采样", delegate
            {
                SysInfo.CpuLoadMeter meter = new SysInfo.CpuLoadMeter();
                meter.Sample();
                Thread.Sleep(220);
                double load = meter.Sample();
                Assert(load >= 0 && load <= 100, "CPU 负载超出范围：" + load);
                Console.WriteLine("      当前负载 " + load.ToString("0.0") + "%");
            });

            Run("完整快照（含 WMI）", delegate
            {
                SystemSnapshot snap = SysInfo.Capture(true, 10);
                Assert(!string.IsNullOrEmpty(snap.ComputerName), "计算机名为空。");
                Assert(!string.IsNullOrEmpty(snap.CpuName), "CPU 名称为空。");
                Console.WriteLine("      " + snap.ComputerName + " / " + snap.CpuName);
                Console.WriteLine("      " + snap.CpuCores + " 核 " + snap.CpuThreads + " 线程 / 显卡 " + snap.GpuName);
                Console.WriteLine("      运行时间 " + (int)snap.Uptime.TotalHours + " 小时 / 管理员=" + snap.Elevated);
            });
        }

        // ------------------------------------------------------------

        private static void TestScanner()
        {
            Section("垃圾清理");

            Run("通配符匹配", delegate
            {
                Assert(JunkScanner.MatchWildcard("thumbcache_96.db", "thumbcache_*.db"), "通配符 * 匹配失败。");
                Assert(JunkScanner.MatchWildcard("thumbcache_1024.db", "thumbcache_*.db"), "长文件名 * 匹配失败。");
                Assert(JunkScanner.MatchWildcard("iconcache_2.db", "iconcache_?.db"), "通配符 ? 匹配失败。");
                Assert(!JunkScanner.MatchWildcard("iconcache_32.db", "iconcache_?.db"), "? 不应匹配两个字符。");
                Assert(!JunkScanner.MatchWildcard("a.txt", "*.db"), "通配符误匹配。");
                Assert(JunkScanner.MatchWildcard("any.log", ""), "空模式应匹配全部。");
                Assert(JunkScanner.MatchWildcard("screen.PNG", "scr??n.png"), "大小写不敏感匹配失败。");
                Assert(JunkScanner.MatchWildcard("a-b-c.dat", "a*c.dat"), "多段 * 匹配失败。");
            });

            Run("构建清理类别", delegate
            {
                List<JunkCategory> cats = JunkScanner.BuildDefaultCategories();
                Assert(cats.Count >= 8, "清理类别数量异常：" + cats.Count);
                for (int i = 0; i < cats.Count; i++)
                {
                    Console.WriteLine("      " + cats[i].Name +
                        "  目录 " + cats[i].Directories.Count + " 个" +
                        (cats[i].Advanced ? "  [高级]" : ""));
                }
            });

            Run("扫描用户临时文件与回收站", delegate
            {
                List<JunkCategory> cats = JunkScanner.BuildDefaultCategories();
                JunkScanner scanner = new JunkScanner();

                JunkCategory recycle = null;
                JunkCategory userTemp = null;
                for (int i = 0; i < cats.Count; i++)
                {
                    if (cats[i].Id == "recyclebin") recycle = cats[i];
                    if (cats[i].Id == "usertemp") userTemp = cats[i];
                }

                Assert(recycle != null && userTemp != null, "未找到目标清理类别。");

                scanner.Scan(recycle);
                Assert(recycle.Scanned, "回收站扫描未完成。");
                Console.WriteLine("      回收站：" + recycle.FileCount + " 个对象，" + recycle.SizeText);

                Stopwatch sw = Stopwatch.StartNew();
                scanner.Scan(userTemp);
                sw.Stop();
                Assert(userTemp.Scanned, "临时目录扫描未完成。");
                Console.WriteLine("      用户临时文件：" + userTemp.FileCount + " 个文件，" +
                    userTemp.SizeText + "，耗时 " + sw.ElapsedMilliseconds + " ms");
            });
        }

        // ------------------------------------------------------------

        private static void TestStartup()
        {
            Section("启动项管理");

            Run("读取启动项", delegate
            {
                List<StartupItem> items = StartupManager.Load();
                Assert(items != null, "启动项列表为 null。");
                Console.WriteLine("      共 " + items.Count + " 项");
                int shown = 0;
                for (int i = 0; i < items.Count && shown < 5; i++, shown++)
                {
                    Console.WriteLine("      [" + items[i].StatusText + "] " + items[i].Name +
                        "  (" + items[i].SourceText + ")");
                }
                for (int i = 0; i < items.Count; i++)
                {
                    Assert(!string.IsNullOrEmpty(items[i].Name), "启动项名称为空。");
                    Assert(items[i].ApprovedName != null, "ApprovedName 为 null。");
                }
            });

            Run("解析命令行", delegate
            {
                Assert(StartupManager.ExtractExecutable("\"C:\\a b\\x.exe\" -arg") == "C:\\a b\\x.exe",
                    "带引号路径解析失败。");
                Assert(StartupManager.ExtractExecutable("C:\\a\\b.exe -silent") == "C:\\a\\b.exe",
                    "普通路径解析失败。");
                Assert(StartupManager.ExtractExecutable("") == "", "空字符串解析失败。");
            });
        }

        // ------------------------------------------------------------

        private static void TestProcess()
        {
            Section("进程管理");

            Run("进程快照", delegate
            {
                ProcManager mgr = new ProcManager();
                List<ProcInfo> first = mgr.Snapshot();
                Assert(first.Count > 0, "未获取到任何进程。");
                Thread.Sleep(400);
                List<ProcInfo> second = mgr.Snapshot();
                Assert(second.Count > 0, "第二次快照为空。");

                int withPath = 0;
                long totalMem = 0;
                for (int i = 0; i < second.Count; i++)
                {
                    if (!string.IsNullOrEmpty(second[i].Path)) withPath++;
                    totalMem += second[i].WorkingSet;
                    Assert(second[i].CpuPercent >= 0 && second[i].CpuPercent <= 100,
                        "CPU 百分比越界：" + second[i].CpuPercent);
                }
                Console.WriteLine("      进程 " + second.Count + " 个，其中 " + withPath +
                    " 个可读取路径，内存合计 " + SysInfo.FormatSize(totalMem));

                second.Sort(delegate (ProcInfo a, ProcInfo b) { return b.WorkingSet.CompareTo(a.WorkingSet); });
                for (int i = 0; i < 3 && i < second.Count; i++)
                {
                    Console.WriteLine("      内存 TOP" + (i + 1) + "：" + second[i].Name +
                        "  " + second[i].MemoryText + "  CPU " + second[i].CpuPercentText);
                }
            });

            Run("查找当前进程", delegate
            {
                string name = Process.GetCurrentProcess().ProcessName;
                Process[] found = ProcManager.FindByName(name);
                Assert(found.Length > 0, "未能找到当前进程：" + name);
                for (int i = 0; i < found.Length; i++) found[i].Dispose();
            });
        }

        // ------------------------------------------------------------

        private static void TestNetwork()
        {
            Section("网络工具");

            Run("枚举网络适配器", delegate
            {
                List<AdapterInfo> list = NetTools.ListAdapters();
                Console.WriteLine("      适配器 " + list.Count + " 个");
                for (int i = 0; i < list.Count; i++)
                {
                    AdapterInfo a = list[i];
                    Assert(a.Name != null && a.Status != null, "适配器字段为 null。");
                    Console.WriteLine("      " + a.Name + " [" + a.Status + "] " +
                        (a.IPv4.Count > 0 ? a.IPv4[0] : "无 IPv4") + "  " + a.SpeedText);
                }
            });

            Run("本机 DNS 与端口监听", delegate
            {
                List<string> dns = NetTools.GetLocalDnsServers();
                Console.WriteLine("      DNS：" + (dns.Count > 0 ? string.Join("，", dns.ToArray()) : "无"));

                List<PortInfo> ports = NetTools.GetListeningPorts();
                Assert(ports != null, "端口列表为 null。");
                Console.WriteLine("      活动连接 / 监听端口 " + ports.Count + " 条");
            });

            Run("Ping 127.0.0.1", delegate
            {
                PingResult r = NetTools.Ping("127.0.0.1", 2, 2000);
                Assert(r.Sent == 2, "发送计数异常。");
                Assert(r.Received == 2, "本机回环 Ping 失败，收到 " + r.Received + " 个响应。");
                Console.WriteLine("      回环平均耗时 " + r.AvgMs + " ms");
            });
        }

        // ------------------------------------------------------------

        private static void TestTweaks()
        {
            Section("系统优化");

            Run("加载优化项库", delegate
            {
                List<ITweak> all = TweakLibrary.All();
                Assert(all.Count >= 20, "优化项数量偏少：" + all.Count);

                Dictionary<string, bool> groups = new Dictionary<string, bool>();
                for (int i = 0; i < all.Count; i++)
                {
                    Assert(!string.IsNullOrEmpty(all[i].Id), "存在 Id 为空的优化项。");
                    Assert(!string.IsNullOrEmpty(all[i].Name), "存在名称为空的优化项。");
                    Assert(!string.IsNullOrEmpty(all[i].Description), all[i].Name + " 缺少说明。");
                    groups[all[i].Group] = true;
                }
                Console.WriteLine("      优化项 " + all.Count + " 项，分组 " + groups.Count + " 个");
            });

            Run("读取每项当前状态（只读）", delegate
            {
                List<ITweak> all = TweakLibrary.All();
                int applied = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    bool state = all[i].IsApplied();
                    if (state) applied++;
                }
                Console.WriteLine("      当前已应用 " + applied + " / " + all.Count);
            });

            Run("推荐项列表", delegate
            {
                List<string> ids = TweakLibrary.RecommendedIds();
                Assert(ids.Count > 0, "推荐项为空。");
                List<ITweak> all = TweakLibrary.All();
                for (int i = 0; i < ids.Count; i++)
                {
                    bool found = false;
                    for (int j = 0; j < all.Count; j++)
                    {
                        if (all[j].Id == ids[i]) { found = true; break; }
                    }
                    Assert(found, "推荐项 Id 不存在于库中：" + ids[i]);
                }
            });

            Run("注册表备份读写", delegate
            {
                const string testId = "__smoketest__";
                // BeginBackup 为保留式：不删除已有备份组（防重复 Apply / 共享组污染原值）。
                // 因此先显式清理本次冒烟残留，再验证完整备份→写入→还原闭环。
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\GuyueBox\Backup\" + testId, false); } catch { }
                RegHelper.BeginBackup(testId);
                Assert(!RegHelper.HasBackup(testId), "清理后 BeginBackup 不应留下记录。");
                RegHelper.SetValue(Microsoft.Win32.RegistryHive.CurrentUser,
                    @"Software\GuyueBox\__smoketest__", "probe", 123, Microsoft.Win32.RegistryValueKind.DWord, testId);
                Assert(RegHelper.HasBackup(testId), "写入后备份记录应存在。");
                int v = RegHelper.GetInt(Microsoft.Win32.RegistryHive.CurrentUser,
                    @"Software\GuyueBox\__smoketest__", "probe", 0);
                Assert(v == 123, "读取刚写入的值失败：" + v);
                Assert(RegHelper.Restore(testId), "还原失败。");
                object after = RegHelper.GetValue(Microsoft.Win32.RegistryHive.CurrentUser,
                    @"Software\GuyueBox\__smoketest__", "probe");
                Assert(after == null, "还原后值应被删除，实际为：" + after);
            });

            Run("服务启动类型读取", delegate
            {
                int start = RegHelper.GetServiceStart("Schedule");
                Assert(start >= 0, "读取 Schedule 服务启动类型失败。");
                Console.WriteLine("      Schedule 服务启动类型：" + RegHelper.ServiceStartText(start));
            });
        }

        // ------------------------------------------------------------

        private static void TestUi()
        {
            Section("界面构造");

            Run("创建全部页面", delegate
            {
                ViewBase[] views = new ViewBase[]
                {
                    new DashboardView(),
                    new CleanerView(),
                    new StartupView(),
                    new ProcessView(),
                    new OptimizeView(),
                    new NetworkView()
                };

                for (int i = 0; i < views.Length; i++)
                {
                    views[i].CreateControl();
                    views[i].SetBounds(0, 0, 980, 560);
                    Application.DoEvents();
                    Console.WriteLine("      " + views[i].TitleText + "  构造正常");
                }

                for (int i = 0; i < views.Length; i++) views[i].Dispose();
            });

            Run("创建主窗体并进入各页面", delegate
            {
                MainForm form = new MainForm();
                form.CreateControl();
                Application.DoEvents();

                string[] keys = new string[]
                {
                    "dashboard", "clean", "space", "programs", "startup", "process", "optimize", "network"
                };
                for (int i = 0; i < keys.Length; i++)
                {
                    form.NavigateTo(keys[i]);
                    Application.DoEvents();
                    Console.WriteLine("      页面切换正常：" + keys[i]);
                }

                // 让后台加载线程有机会回投到 UI 线程
                for (int i = 0; i < 30; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(60);
                }

                form.Dispose();
            });

            Run("主题字体与图标绘制", delegate
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(64, 64))
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                {
                    string[] icons = new string[]
                    {
                        "dashboard", "clean", "startup", "process", "tune", "network", "space", "programs",
                        "shield", "recycle", "temp", "image", "globe", "bug", "list",
                        "bolt", "share", "check", "close", "min", "max", "restore",
                        "refresh", "admin", "info", "trash", "folder", "cpu", "memory",
                        "disk", "clock", "lock", "power", "unknown-icon"
                    };
                    for (int i = 0; i < icons.Length; i++)
                    {
                        IconPainter.Draw(g, icons[i], new System.Drawing.Rectangle(8, 8, 48, 48),
                            System.Drawing.Color.White);
                    }
                    Assert(Theme.FontBody != null, "主题字体为 null。");
                    Console.WriteLine("      " + icons.Length + " 个图标绘制完成，无异常");
                }
            });

        }

        // ------------------------------------------------------------

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name + " --");
        }

        private static void Run(string name, Action action)
        {
            try
            {
                action();
                _pass++;
                Console.WriteLine("[通过] " + name);
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine("[失败] " + name);
                Console.WriteLine("       " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine("       " + ex.StackTrace);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
