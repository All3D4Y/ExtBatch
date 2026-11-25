using System.Collections.Generic;

namespace CmOxeProvisioner.Models
{
    public class OxePreset
    {
        public string label { get; set; }
        public string entityNo { get; set; }
        public string entityName { get; set; }
        public string agentPg { get; set; }
        public string sampleExt { get; set; }
        public string sampleLoginId { get; set; }
        public string source { get; set; }
        public string dnRange { get; set; }
        public string ctiRange { get; set; }
        public string note { get; set; }
    }

    public class CustomerInfo
    {
        public string name { get; set; }
        public string floor { get; set; }
        public string[] agentLogin { get; set; }
        public string[] acdPosition { get; set; }
        public string[] agentDn { get; set; }
        public string[] place { get; set; }
        public string[] rsi { get; set; }
        public bool? useVQueue { get; set; }
        public bool? useIvr { get; set; }
        public string note { get; set; }
        public List<OxePreset> oxePresets { get; set; }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(floor) || floor == "(위치 없음)") return name ?? "";
                return string.Format("{0}  [{1}]", name, floor);
            }
        }
    }
}
