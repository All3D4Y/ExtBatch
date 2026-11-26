using System;

namespace CmOxeProvisioner.Models
{
    public class AppSettings
    {
        public string ThemeKey { get; set; }
        public string CmPrimaryHost { get; set; }
        public int CmPrimaryPort { get; set; }
        public string CmBackupHost { get; set; }
        public int CmBackupPort { get; set; }
        public string CmClientName { get; set; }
        public string CmUserId { get; set; }
        public string CmPasswordEnc { get; set; }
        public int BackendPort { get; set; }

        public string EmsUrl { get; set; }
        public string EmsServerKeyword { get; set; }
        public string EmsUserId { get; set; }
        public string EmsPasswordEnc { get; set; }
        public bool EmsAutoLogin { get; set; }
        public bool EmsTerminalOnly { get; set; }

        public int OxeNodeNumber { get; set; }
        public string DefaultProfileExt { get; set; }
        public string DefaultProfileLogin { get; set; }
        public int DnWarnThreshold { get; set; }

        public static AppSettings CreateDefault()
        {
            AppSettings s = new AppSettings();
            s.ThemeKey = "antigravity-light";
            s.CmPrimaryHost = "10.62.130.67";
            s.CmPrimaryPort = 2020;
            s.CmBackupHost = "10.62.130.68";
            s.CmBackupPort = 2020;
            s.CmClientName = "default";
            s.CmUserId = "default";
            s.CmPasswordEnc = "";
            s.BackendPort = 18080;

            s.EmsUrl = "https://lightgate.lbucess.com/terminal";
            s.EmsServerKeyword = "9. PBX_A";
            s.EmsUserId = "";
            s.EmsPasswordEnc = "";
            s.EmsAutoLogin = true;
            s.EmsTerminalOnly = true;

            s.OxeNodeNumber = 1;
            s.DefaultProfileExt = "31090";
            s.DefaultProfileLogin = "71090";
            s.DnWarnThreshold = 8;
            return s;
        }
    }
}
