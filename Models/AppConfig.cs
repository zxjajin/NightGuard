namespace NightGuard.Models;

public sealed class AppConfig
{
    public string RestrictionStart { get; set; } = "23:00";
    public string RestrictionEnd { get; set; } = "08:00";
    public bool BlockAllAppsDuringRestriction { get; set; } = true;
    public List<string> AlwaysAllowedProcesses { get; set; } =
    [
        "explorer.exe",
        "cmd.exe",
        "powershell.exe",
        "WindowsTerminal.exe",
        "notepad.exe",
        "idea64.exe",
        "idea.exe",
        "jetbrains-toolbox.exe",
        "WINWORD.EXE",
        "EXCEL.EXE",
        "POWERPNT.EXE",
        "ONENOTE.EXE",
        "OUTLOOK.EXE",
        "WPS.exe",
        "wps.exe",
        "et.exe",
        "wpp.exe",
        "chrome.exe",
        "msedge.exe",
        "msedgewebview2.exe",
        "Code.exe",
        "Cursor.exe",
        "Another Redis Desktop Manager.exe",
        "Apifox.exe",
        "Docker Desktop.exe",
        "docker.exe",
        "docker-sandbox.exe",
        "com.docker.backend.exe",
        "com.docker.build.exe",
        "com.docker.service.exe",
        "com.docker.proxy.exe",
        "com.docker.cli.exe",
        "eclipse.exe",
        "eNSP.exe",
        "DBeaver.exe",
        "HBuilderX.exe",
        "GoLand64.exe",
        "GoLand.exe",
        "Navicat.exe",
        "MobaXterm.exe",
        "VirtualBox.exe",
        "VirtualBoxVM.exe",
        "vmware.exe",
        "vmware-vmx.exe",
        "pycharm64.exe",
        "pycharm.exe",
        "SQLyog.exe",
        "WeChatDevTools.exe",
        "wampmanager.exe",
        "wampserver64.exe",
        "httpd.exe",
        "mysqld.exe",
        "php.exe"
    ];
    public List<string> BlockedProcesses { get; set; } = ["steam.exe", "game.exe"];
    public List<string> BlockedDomains { get; set; } = ["youtube.com", "www.youtube.com"];
    public int UnlockDelayMinutes { get; set; } = 5;
    public int TemporaryAllowanceMinutes { get; set; } = 10;
    public int MaxUnlocksPerNight { get; set; } = 2;
    public string SettingsPasswordHash { get; set; } = "";
    public bool StartWithWindows { get; set; }
}
