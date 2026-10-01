/* ============================================================
 * 文件说明：内置 DNS 服务器库（120 条）
 * 项目：古月工具箱（GuyueBox）
 * 说明：覆盖公共 DNS（阿里/腾讯/114/百度/谷歌/Cloudflare/AdGuard 等）
 *       与各省电信/联通/移动运营商 DNS，支持按地区/关键词搜索过滤。
 * ============================================================ */

using System;
using System.Collections.Generic;

namespace GuyueBox.Core
{
    /// <summary>
    /// 内置 DNS 库：只读数据 + 搜索过滤。UI 侧按需取用。
    /// </summary>
    public static class DnsLibrary
    {
        public static readonly DnsPreset[] All = new DnsPreset[]
        {
            new DnsPreset { Region = "江苏省南京市", Name = "南京信风网络科技有限公司GreatbitDNS服务器", Primary = "114.114.114.114", Secondary = "114.114.115.115" },
            new DnsPreset { Region = "北京市", Name = "腾讯云", Primary = "119.29.29.29", Secondary = "182.254.116.116" },
            new DnsPreset { Region = "中国", Name = "电信/DNS派公共DNS", Primary = "101.226.4.6", Secondary = "218.30.118.6" },
            new DnsPreset { Region = "北京市", Name = "联通/上海聚流软件科技有限公司公共DNS服务器联通节点", Primary = "123.125.81.6", Secondary = "140.207.198.6" },
            new DnsPreset { Region = "中国", Name = "CNNIC权威云解析/全球Anycast节点", Primary = "1.2.4.8", Secondary = "210.2.4.8" },
            new DnsPreset { Region = "塞浦路斯", Name = "AdGuard", Primary = "94.140.14.140", Secondary = "94.140.14.141" },
            new DnsPreset { Region = "美国加利福尼亚州圣克拉拉县山景市", Name = "谷歌公司DNS服务器", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
            new DnsPreset { Region = "澳大利亚", Name = "APNIC/CloudFlare公共DNS服务器", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
            new DnsPreset { Region = "美国", Name = "Quad9(IBM)公共DNS", Primary = "9.9.9.9", Secondary = "149.112.112.112" },
            new DnsPreset { Region = "美国加利福尼亚州圣何塞", Name = "xTom/AS6233", Primary = "185.222.222.222", Secondary = "185.184.222.222" },
            new DnsPreset { Region = "美国加利福尼亚州旧金山市", Name = "OpenDNS有限公司公众DNS服务器", Primary = "208.67.222.222", Secondary = "208.67.220.220" },
            new DnsPreset { Region = "美国", Name = "加利福尼亚州洛杉矶市V2EX_DNS服务器", Primary = "199.91.73.222", Secondary = "178.79.131.110" },
            new DnsPreset { Region = "浙江省杭州市", Name = "阿里巴巴anycast公共DNS", Primary = "223.5.5.5", Secondary = "223.6.6.6" },
            new DnsPreset { Region = "广东省广州市", Name = "电信/腾讯云", Primary = "183.60.83.19", Secondary = "183.60.82.98" },
            new DnsPreset { Region = "北京市", Name = "北京百度网讯科技有限公司公共DNS服务器(电信节点)", Primary = "180.76.76.76", Secondary = "" },
            new DnsPreset { Region = "美国新泽西州纽瓦克市", Name = "Level3Communications", Primary = "4.2.2.1", Secondary = "4.2.2.2" },
            new DnsPreset { Region = "上海市", Name = "华为云", Primary = "122.112.208.1", Secondary = "139.9.23.90" },
            new DnsPreset { Region = "北京市", Name = "字节跳动", Primary = "180.184.1.1", Secondary = "180.184.2.2" },
            new DnsPreset { Region = "北京市", Name = "UCloud/电信/联通/移动/铁通/鹏博士", Primary = "117.50.11.11", Secondary = "52.80.66.66" },
            new DnsPreset { Region = "台湾省", Name = "台湾网路资讯中心DNS服务器", Primary = "101.101.101.101", Secondary = "101.102.103.104" },
            new DnsPreset { Region = "台湾省", Name = "中华电信(HiNet)数据中心DNS服务器", Primary = "168.95.192.1", Secondary = "168.95.1.1" },
            new DnsPreset { Region = "香港", Name = "城市电讯有限公司DNS服务器", Primary = "203.80.96.10", Secondary = "203.80.96.9" },
            new DnsPreset { Region = "美国", Name = "诺顿公共DNS", Primary = "199.85.126.10", Secondary = "199.85.127.10" },
            new DnsPreset { Region = "美国", Name = "Dyn公共DNS", Primary = "216.146.35.35", Secondary = "216.146.36.36" },
            new DnsPreset { Region = "美国加利福尼亚州", Name = "瑞信银行DNS", Primary = "64.6.64.6", Secondary = "64.6.65.6" },
            new DnsPreset { Region = "北京市", Name = "电信IDC机房", Primary = "219.141.136.10", Secondary = "219.141.140.10" },
            new DnsPreset { Region = "上海市", Name = "电信/DNS服务器", Primary = "202.96.209.133", Secondary = "116.228.111.118" },
            new DnsPreset { Region = "天津市", Name = "电信DNS服务器", Primary = "219.150.32.132", Secondary = "219.146.0.132" },
            new DnsPreset { Region = "重庆市", Name = "电信DNS服务器", Primary = "61.128.192.68", Secondary = "61.128.128.68" },
            new DnsPreset { Region = "安徽省", Name = "电信DNS服务器", Primary = "61.132.163.68", Secondary = "202.102.213.68" },
            new DnsPreset { Region = "福建省福州市", Name = "电信DNS服务器", Primary = "218.85.152.99", Secondary = "218.85.157.99" },
            new DnsPreset { Region = "甘肃省兰州市", Name = "电信局枢纽楼(西口电信局)", Primary = "202.100.64.68", Secondary = "61.178.0.93" },
            new DnsPreset { Region = "广东省广州市", Name = "电信/DNS服务器", Primary = "202.96.128.86", Secondary = "202.96.128.166" },
            new DnsPreset { Region = "广西柳州市", Name = "电信/DNS服务器", Primary = "202.103.225.68", Secondary = "202.103.224.68" },
            new DnsPreset { Region = "贵州省", Name = "电信DNS服务器", Primary = "202.98.192.67", Secondary = "202.98.198.167" },
            new DnsPreset { Region = "河南省洛阳市", Name = "电信DNS服务器", Primary = "222.88.88.88", Secondary = "222.85.85.85" },
            new DnsPreset { Region = "黑龙江省齐齐哈尔市", Name = "电信DNS服务器", Primary = "219.147.198.230", Secondary = "219.147.198.242" },
            new DnsPreset { Region = "湖北省武汉市", Name = "电信/DNS服务器", Primary = "202.103.24.68", Secondary = "202.103.0.68" },
            new DnsPreset { Region = "湖南省", Name = "电信DNS服务器", Primary = "59.51.78.211", Secondary = "59.51.78.210" },
            new DnsPreset { Region = "江苏省南京市", Name = "电信/公共DNS服务器", Primary = "218.2.2.2", Secondary = "218.4.4.4" },
            new DnsPreset { Region = "江西省", Name = "电信DNS服务器", Primary = "202.101.224.69", Secondary = "202.101.226.68" },
            new DnsPreset { Region = "内蒙古呼和浩特市", Name = "电信", Primary = "219.148.162.31", Secondary = "222.74.39.50" },
            new DnsPreset { Region = "山东省", Name = "电信DNS服务器(济南省际节点)", Primary = "219.146.1.66", Secondary = "219.147.1.66" },
            new DnsPreset { Region = "山西省太原市", Name = "电信", Primary = "59.49.49.49", Secondary = "" },
            new DnsPreset { Region = "陕西省西安市", Name = "电信/骨干网", Primary = "218.30.19.40", Secondary = "61.134.1.4" },
            new DnsPreset { Region = "四川省成都市", Name = "电信DNS服务器", Primary = "61.139.2.69", Secondary = "218.6.200.139" },
            new DnsPreset { Region = "云南省", Name = "电信DNS服务器", Primary = "222.172.200.68", Secondary = "61.166.150.123" },
            new DnsPreset { Region = "浙江省杭州市", Name = "电信DNS服务器", Primary = "202.101.172.35", Secondary = "202.101.172.47" },
            new DnsPreset { Region = "江西省九江市", Name = "电信DNS服务器", Primary = "202.101.226.69", Secondary = "" },
            new DnsPreset { Region = "河北省唐山市", Name = "电信", Primary = "222.222.202.202", Secondary = "" },
            new DnsPreset { Region = "海南省", Name = "电信DNS服务器", Primary = "202.100.192.68", Secondary = "" },
            new DnsPreset { Region = "辽宁省沈阳市", Name = "电信DNS服务器(全省通用)", Primary = "219.148.204.66", Secondary = "" },
            new DnsPreset { Region = "吉林省长春市", Name = "电信DNS服务器", Primary = "219.149.194.55", Secondary = "" },
            new DnsPreset { Region = "新疆乌鲁木齐市", Name = "电信", Primary = "61.128.114.167", Secondary = "" },
            new DnsPreset { Region = "北京市顺义区", Name = "联通", Primary = "123.123.123.123", Secondary = "123.123.123.124" },
            new DnsPreset { Region = "上海市", Name = "联通/NS服务器", Primary = "210.22.70.3", Secondary = "210.22.84.3" },
            new DnsPreset { Region = "天津市", Name = "联通DNS", Primary = "202.99.104.68", Secondary = "202.99.96.68" },
            new DnsPreset { Region = "重庆市", Name = "联通DNS服务器", Primary = "221.5.203.98", Secondary = "221.7.92.98" },
            new DnsPreset { Region = "广东省深圳市", Name = "联通/DNS服务器", Primary = "210.21.196.6", Secondary = "221.5.88.88" },
            new DnsPreset { Region = "河北省", Name = "联通/DNS服务器", Primary = "202.99.160.68", Secondary = "202.99.166.4" },
            new DnsPreset { Region = "河南省", Name = "联通/DNS服务器", Primary = "202.102.224.68", Secondary = "202.102.227.68" },
            new DnsPreset { Region = "黑龙江省", Name = "联通DNS服务器", Primary = "202.97.224.69", Secondary = "202.97.224.68" },
            new DnsPreset { Region = "吉林省长春市", Name = "联通DNS服务器", Primary = "202.98.0.68", Secondary = "202.98.5.68" },
            new DnsPreset { Region = "江苏省南京市", Name = "联通DNS服务器", Primary = "221.6.4.66", Secondary = "221.6.4.67" },
            new DnsPreset { Region = "内蒙古呼和浩特市", Name = "联通/DNS服务器(nmdns)", Primary = "202.99.224.68", Secondary = "202.99.224.8" },
            new DnsPreset { Region = "山东省青岛市", Name = "联通", Primary = "202.102.128.68", Secondary = "202.102.152.3" },
            new DnsPreset { Region = "山西省太原市", Name = "联通/DNS服务器", Primary = "202.99.192.66", Secondary = "202.99.192.68" },
            new DnsPreset { Region = "陕西省西安市", Name = "联通", Primary = "221.11.1.67", Secondary = "221.11.1.68" },
            new DnsPreset { Region = "四川省成都市", Name = "联通/DNS服务器", Primary = "119.6.6.6", Secondary = "124.161.87.155" },
            new DnsPreset { Region = "浙江省杭州市", Name = "联通DNS服务器", Primary = "221.12.1.227", Secondary = "221.12.33.227" },
            new DnsPreset { Region = "辽宁省大连市", Name = "联通/DNS服务器", Primary = "202.96.69.38", Secondary = "202.96.64.68" },
            new DnsPreset { Region = "贵州省贵阳市", Name = "联通", Primary = "221.13.30.242", Secondary = "" },
            new DnsPreset { Region = "甘肃省兰州市", Name = "联通", Primary = "221.7.34.11", Secondary = "" },
            new DnsPreset { Region = "宁夏银川市", Name = "联通/大博金网吧(胜利南街永春巷27号)", Primary = "221.199.12.157", Secondary = "" },
            new DnsPreset { Region = "江西省南昌市", Name = "联通DNS服务器", Primary = "220.248.192.12", Secondary = "" },
            new DnsPreset { Region = "广西南宁市", Name = "联通DNS服务器", Primary = "221.7.128.68", Secondary = "" },
            new DnsPreset { Region = "西藏", Name = "联通", Primary = "221.13.65.34", Secondary = "" },
            new DnsPreset { Region = "海南省海口市", Name = "联通DNS服务器", Primary = "221.11.132.2", Secondary = "" },
            new DnsPreset { Region = "湖南省", Name = "联通数据上网DNS服务器", Primary = "58.20.127.238", Secondary = "" },
            new DnsPreset { Region = "湖北省武汉市", Name = "联通DNS服务器", Primary = "218.104.111.122", Secondary = "" },
            new DnsPreset { Region = "安徽省合肥市", Name = "联通/中国科学技术大学联通", Primary = "218.104.78.2", Secondary = "58.242.2.2" },
            new DnsPreset { Region = "福建省", Name = "联通DNS服务器", Primary = "218.104.128.106", Secondary = "" },
            new DnsPreset { Region = "新疆", Name = "联通DNS服务器", Primary = "221.7.1.20", Secondary = "" },
            new DnsPreset { Region = "云南省", Name = "联通DNS服务器", Primary = "221.3.131.11", Secondary = "" },
            new DnsPreset { Region = "北京市", Name = "移动", Primary = "221.130.33.52", Secondary = "221.130.33.60" },
            new DnsPreset { Region = "上海市", Name = "移动DNS服务器", Primary = "211.136.112.50", Secondary = "211.136.150.66" },
            new DnsPreset { Region = "天津市", Name = "移动", Primary = "211.137.160.50", Secondary = "211.137.160.185" },
            new DnsPreset { Region = "重庆市", Name = "移动", Primary = "218.201.4.3", Secondary = "218.201.21.132" },
            new DnsPreset { Region = "安徽省合肥市", Name = "移动DNS服务器", Primary = "211.138.180.2", Secondary = "211.138.180.3" },
            new DnsPreset { Region = "山东省", Name = "移动", Primary = "218.201.96.130", Secondary = "211.137.191.26" },
            new DnsPreset { Region = "山西省", Name = "移动", Primary = "211.138.106.2", Secondary = "211.138.106.3" },
            new DnsPreset { Region = "江苏省南京市", Name = "移动/DNS服务器", Primary = "221.131.143.69", Secondary = "112.4.0.55" },
            new DnsPreset { Region = "浙江省杭州市", Name = "移动DNS服务器", Primary = "211.140.13.188", Secondary = "211.140.188.188" },
            new DnsPreset { Region = "湖南省长沙市", Name = "移动", Primary = "211.142.210.98", Secondary = "211.142.210.99" },
            new DnsPreset { Region = "湖北省武汉市", Name = "移动", Primary = "211.137.58.20", Secondary = "211.137.64.163" },
            new DnsPreset { Region = "江西省南昌市", Name = "移动专用DNS服务器(省内)", Primary = "211.141.90.68", Secondary = "211.141.90.69" },
            new DnsPreset { Region = "陕西省西安市", Name = "移动DNS服务器", Primary = "211.137.130.3", Secondary = "211.137.130.19" },
            new DnsPreset { Region = "四川省成都市", Name = "移动", Primary = "211.137.82.4", Secondary = "211.137.96.205" },
            new DnsPreset { Region = "广东省广州市", Name = "移动公用DNS服务器", Primary = "211.136.192.6", Secondary = "211.136.20.204" },
            new DnsPreset { Region = "广西", Name = "移动公众宽带DNS服务器", Primary = "211.138.245.180", Secondary = "211.136.17.108" },
            new DnsPreset { Region = "贵州省贵阳市", Name = "移动DNS服务器", Primary = "211.139.5.29", Secondary = "211.139.5.30" },
            new DnsPreset { Region = "福建省福州市", Name = "移动", Primary = "211.138.151.161", Secondary = "211.138.156.66" },
            new DnsPreset { Region = "河北省沧州市", Name = "移动", Primary = "211.143.60.56", Secondary = "211.138.13.66" },
            new DnsPreset { Region = "河南省郑州市", Name = "移动", Primary = "211.138.24.66", Secondary = "" },
            new DnsPreset { Region = "甘肃省兰州市", Name = "移动", Primary = "218.203.160.194", Secondary = "218.203.160.195" },
            new DnsPreset { Region = "中国–黑龙江", Name = "移动", Primary = "211.137.241.34", Secondary = "211.137.241.35" },
            new DnsPreset { Region = "吉林省", Name = "移动DNS服务器", Primary = "211.141.16.99", Secondary = "211.141.0.99" },
            new DnsPreset { Region = "辽宁省沈阳市", Name = "移动DNS服务器", Primary = "211.137.32.178", Secondary = "211.140.197.58" },
            new DnsPreset { Region = "云南省昆明市", Name = "移动", Primary = "211.139.29.68", Secondary = "211.139.29.69" },
            new DnsPreset { Region = "海南省海口市", Name = "移动", Primary = "221.176.88.95", Secondary = "211.138.164.6" },
            new DnsPreset { Region = "内蒙古呼和浩特市", Name = "移动DNS服务器", Primary = "211.138.91.1", Secondary = "211.138.91.2" },
            new DnsPreset { Region = "新疆", Name = "移动专用DNS服务器(省内)", Primary = "218.202.152.130", Secondary = "218.202.152.131" },
            new DnsPreset { Region = "西藏拉萨市", Name = "移动DNS服务器", Primary = "211.139.73.34", Secondary = "211.139.73.35" },
            new DnsPreset { Region = "青海省西宁市", Name = "移动DNS服务器", Primary = "211.138.75.123", Secondary = "" },
            new DnsPreset { Region = "宁夏", Name = "移动", Primary = "218.203.123.116", Secondary = "" },
            new DnsPreset { Region = "香港", Name = "中国移动香港有限公司DNS服务器", Primary = "203.142.100.18", Secondary = "203.142.100.21" },
            new DnsPreset { Region = "广东省广州市", Name = "中移铁通", Primary = "61.235.70.252", Secondary = "211.98.4.1" },
            new DnsPreset { Region = "广东省广州市", Name = "珠江宽频", Primary = "116.199.0.200", Secondary = "116.116.116.116" },
            new DnsPreset { Region = "广东省深圳市", Name = "鹏博士宽带", Primary = "211.162.78.1", Secondary = "211.162.78.2" },
            new DnsPreset { Region = "广东省深圳市", Name = "天威有线宽带(关内)", Primary = "211.148.192.141", Secondary = "" },
        };

        /// <summary>按地区/名称/IP 关键词过滤，忽略大小写；空关键词返回全部。</summary>
        public static List<DnsPreset> Search(string keyword)
        {
            List<DnsPreset> result = new List<DnsPreset>();
            string kw = keyword == null ? "" : keyword.Trim();
            if (kw.Length == 0)
            {
                result.AddRange(All);
                return result;
            }
            for (int i = 0; i < All.Length; i++)
            {
                DnsPreset p = All[i];
                if (p.Region.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Primary.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>列出所有出现过的地区（去重，用于地区下拉过滤）。</summary>
        public static List<string> Regions()
        {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Region.Length == 0) continue;
                if (seen.Add(All[i].Region)) list.Add(All[i].Region);
            }
            return list;
        }
    }
}