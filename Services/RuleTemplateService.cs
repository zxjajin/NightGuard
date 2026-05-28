using NightGuard.Models;

namespace NightGuard.Services;

public sealed class RuleTemplateService
{
    public IReadOnlyList<RuleTemplate> GetTemplates()
    {
        return
        [
            new RuleTemplate
            {
                Name = "Steam",
                ProcessNames = ["steam.exe", "steamwebhelper.exe"],
                Domains = ["store.steampowered.com", "steamcommunity.com", "steampowered.com"],
                Note = "Steam 商店和社区"
            },
            new RuleTemplate
            {
                Name = "WeGame",
                ProcessNames = ["WeGame.exe", "tgp_daemon.exe"],
                Domains = ["wegame.com.cn", "tgp.qq.com"],
                Note = "腾讯 WeGame"
            },
            new RuleTemplate
            {
                Name = "抖音",
                ProcessNames = ["douyin.exe"],
                Domains = ["douyin.com", "www.douyin.com", "live.douyin.com", "snssdk.com", "amemv.com"],
                Note = "抖音网页和客户端"
            },
            new RuleTemplate
            {
                Name = "B站",
                ProcessNames = ["bilibili.exe"],
                Domains = ["bilibili.com", "www.bilibili.com", "live.bilibili.com", "api.bilibili.com", "bilivideo.com", "hdslb.com"],
                Note = "哔哩哔哩网页和客户端"
            },
            new RuleTemplate
            {
                Name = "快手",
                ProcessNames = ["kuaishou.exe"],
                Domains = ["kuaishou.com", "www.kuaishou.com", "gifshow.com"],
                Note = "快手网页和客户端"
            },
            new RuleTemplate
            {
                Name = "小红书",
                ProcessNames = ["xiaohongshu.exe"],
                Domains = ["xiaohongshu.com", "www.xiaohongshu.com", "xhscdn.com"],
                Note = "小红书网页和客户端"
            },
            new RuleTemplate
            {
                Name = "虎牙",
                ProcessNames = ["huya.exe"],
                Domains = ["huya.com", "www.huya.com", "live.huya.com"],
                Note = "虎牙直播"
            },
            new RuleTemplate
            {
                Name = "斗鱼",
                ProcessNames = ["douyu.exe"],
                Domains = ["douyu.com", "www.douyu.com", "live.douyu.com"],
                Note = "斗鱼直播"
            }
        ];
    }
}
