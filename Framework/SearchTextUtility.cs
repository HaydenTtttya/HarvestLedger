using System.Globalization;
using System.Text;

namespace HarvestLedger.Framework;

public static class SearchTextUtility
{
    private static readonly Dictionary<char, string> PinyinByCharacter = new()
    {
        ['阿'] = "a", ['艾'] = "ai", ['安'] = "an", ['暗'] = "an", ['昂'] = "ang", ['八'] = "ba", ['白'] = "bai", ['百'] = "bai",
        ['柏'] = "bai", ['斑'] = "ban", ['板'] = "ban", ['棒'] = "bang", ['包'] = "bao", ['宝'] = "bao", ['鲍'] = "bao",
        ['卑'] = "bei", ['北'] = "bei", ['贝'] = "bei", ['背'] = "bei", ['苯'] = "ben", ['泵'] = "beng", ['比'] = "bi",
        ['扁'] = "bian", ['变'] = "bian", ['标'] = "biao", ['鳔'] = "biao", ['冰'] = "bing", ['饼'] = "bing", ['菠'] = "bo",
        ['玻'] = "bo", ['波'] = "bo", ['卜'] = "bo", ['布'] = "bu", ['菜'] = "cai", ['彩'] = "cai",
        ['参'] = "shen", ['草'] = "cao", ['茶'] = "cha", ['长'] = "chang", ['肠'] = "chang", ['朝'] = "chao", ['车'] = "che",
        ['橙'] = "cheng", ['翅'] = "chi", ['虫'] = "chong", ['丑'] = "chou", ['稠'] = "chou", ['出'] = "chu", ['川'] = "chuan",
        ['春'] = "chun", ['刺'] = "ci", ['葱'] = "cong", ['醋'] = "cu", ['脆'] = "cui", ['村'] = "cun", ['大'] = "da",
        ['淡'] = "dan", ['蛋'] = "dan", ['稻'] = "dao", ['灯'] = "deng", ['地'] = "di", ['帝'] = "di", ['点'] = "dian",
        ['雕'] = "diao", ['丁'] = "ding", ['冬'] = "dong", ['豆'] = "dou", ['毒'] = "du", ['短'] = "duan", ['鳄'] = "e",
        ['儿'] = "er", ['耳'] = "er", ['饭'] = "fan", ['风'] = "feng", ['蜂'] = "feng", ['凤'] = "feng", ['佛'] = "fo",
        ['鲋'] = "fu", ['干'] = "gan", ['甘'] = "gan", ['刚'] = "gang", ['高'] = "gao", ['糕'] = "gao", ['根'] = "gen",
        ['耕'] = "geng", ['菇'] = "gu", ['古'] = "gu", ['骨'] = "gu", ['瓜'] = "gua", ['怪'] = "guai", ['罐'] = "guan",
        ['光'] = "guang", ['鬼'] = "gui", ['桂'] = "gui", ['果'] = "guo", ['海'] = "hai", ['汉'] = "han", ['河'] = "he",
        ['黑'] = "hei", ['红'] = "hong", ['虹'] = "hong", ['猴'] = "hou", ['胡'] = "hu", ['花'] = "hua", ['黄'] = "huang",
        ['灰'] = "hui", ['火'] = "huo", ['鸡'] = "ji", ['吉'] = "ji", ['鲫'] = "ji", ['季'] = "ji", ['剂'] = "ji",
        ['尖'] = "jian", ['姜'] = "jiang", ['酱'] = "jiang", ['胶'] = "jiao", ['椒'] = "jiao", ['金'] = "jin", ['晶'] = "jing",
        ['鲸'] = "jing", ['酒'] = "jiu", ['爵'] = "jue", ['菌'] = "jun", ['咖'] = "ka", ['卡'] = "ka", ['开'] = "kai",
        ['可'] = "ke", ['空'] = "kong", ['恐'] = "kong", ['口'] = "kou", ['苦'] = "ku", ['矿'] = "kuang", ['蜡'] = "la",
        ['蓝'] = "lan", ['狼'] = "lang", ['酪'] = "lao", ['鲤'] = "li", ['梨'] = "li", ['李'] = "li", ['粒'] = "li",
        ['莲'] = "lian", ['亮'] = "liang", ['鳞'] = "lin", ['榴'] = "liu", ['龙'] = "long", ['萝'] = "luo", ['绿'] = "lv",
        ['麻'] = "ma", ['马'] = "ma", ['麦'] = "mai", ['鳗'] = "man", ['蔓'] = "man", ['芒'] = "mang", ['猫'] = "mao",
        ['毛'] = "mao", ['美'] = "mei", ['莓'] = "mei", ['檬'] = "meng", ['蜜'] = "mi", ['棉'] = "mian", ['面'] = "mian",
        ['苗'] = "miao", ['魔'] = "mo", ['蘑'] = "mo", ['木'] = "mu",
        ['牡'] = "mu", ['奶'] = "nai", ['南'] = "nan", ['泥'] = "ni", ['牛'] = "niu", ['农'] = "nong", ['女'] = "nv",
        ['柠'] = "ning", ['糯'] = "nuo", ['欧'] = "ou", ['排'] = "pai", ['片'] = "pian", ['漂'] = "piao", ['品'] = "pin", ['苹'] = "ping",
        ['啤'] = "pi",
        ['葡'] = "pu", ['七'] = "qi", ['奇'] = "qi", ['旗'] = "qi", ['茄'] = "qie", ['青'] = "qing", ['秋'] = "qiu",
        ['球'] = "qiu", ['热'] = "re", ['人'] = "ren", ['日'] = "ri", ['绒'] = "rong", ['肉'] = "rou", ['乳'] = "ru",
        ['蕊'] = "rui", ['伞'] = "san", ['鲨'] = "sha", ['山'] = "shan", ['扇'] = "shan", ['上'] = "shang", ['蛇'] = "she",
        ['神'] = "shen", ['生'] = "sheng", ['石'] = "shi", ['史'] = "shi", ['士'] = "shi", ['薯'] = "shu", ['树'] = "shu",
        ['霜'] = "shuang", ['水'] = "shui", ['丝'] = "si", ['松'] = "song", ['酥'] = "su", ['酸'] = "suan", ['笋'] = "sun",
        ['梭'] = "suo", ['苔'] = "tai", ['太'] = "tai", ['桃'] = "tao", ['藤'] = "teng", ['甜'] = "tian", ['条'] = "tiao",
        ['头'] = "tou", ['土'] = "tu", ['兔'] = "tu", ['豚'] = "tun", ['蛙'] = "wa", ['弯'] = "wan", ['王'] = "wang",
        ['尾'] = "wei", ['未'] = "wei", ['味'] = "wei", ['文'] = "wen", ['乌'] = "wu", ['无'] = "wu", ['虾'] = "xia",
        ['夏'] = "xia", ['仙'] = "xian", ['香'] = "xiang", ['小'] = "xiao", ['蟹'] = "xie", ['星'] = "xing", ['杏'] = "xing",
        ['虚'] = "xu", ['鲟'] = "xun", ['芽'] = "ya", ['鸭'] = "ya", ['盐'] = "yan", ['阳'] = "yang", ['洋'] = "yang", ['杨'] = "yang",
        ['羊'] = "yang", ['药'] = "yao", ['椰'] = "ye", ['叶'] = "ye", ['野'] = "ye", ['夜'] = "ye", ['一'] = "yi",
        ['银'] = "yin", ['樱'] = "ying", ['油'] = "you", ['鱼'] = "yu", ['玉'] = "yu", ['郁'] = "yu", ['芋'] = "yu",
        ['虞'] = "yu", ['雨'] = "yu", ['鸢'] = "yuan", ['月'] = "yue", ['越'] = "yue", ['杂'] = "za", ['藻'] = "zao", ['蔗'] = "zhe",
        ['汁'] = "zhi", ['柱'] = "zhu", ['猪'] = "zhu", ['籽'] = "zi", ['子'] = "zi", ['紫'] = "zi"
    };

    public static string Build(params string?[] values)
    {
        List<string> tokens = new();
        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            string normalized = Normalize(value);
            if (normalized.Length > 0)
                tokens.Add(normalized);

            string compact = Compact(value);
            if (compact.Length > 0)
                tokens.Add(compact);

            string pinyin = ToPinyin(value);
            if (pinyin.Length > 0)
            {
                tokens.Add(pinyin);
                tokens.Add(Compact(pinyin));
            }
        }

        return string.Join(' ', tokens.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static string[] GetQueryTerms(string query)
    {
        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Compact)
            .Where(term => term.Length > 0)
            .ToArray();
    }

    private static string ToPinyin(string text)
    {
        StringBuilder withSpaces = new();
        foreach (char ch in text)
        {
            if (PinyinByCharacter.TryGetValue(ch, out string? pinyin))
            {
                if (withSpaces.Length > 0)
                    withSpaces.Append(' ');

                withSpaces.Append(pinyin);
            }
            else if (ch <= 127 && char.IsLetterOrDigit(ch))
            {
                if (withSpaces.Length > 0)
                    withSpaces.Append(' ');

                withSpaces.Append(char.ToLowerInvariant(ch));
            }
        }

        return withSpaces.ToString();
    }

    private static string Normalize(string text)
    {
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLower(CultureInfo.InvariantCulture);
    }

    private static string Compact(string text)
    {
        StringBuilder builder = new();
        foreach (char ch in text.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch) || ch > 127)
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
