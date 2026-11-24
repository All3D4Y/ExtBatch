using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    public class CustomerRepository
    {
        public List<CustomerInfo> LoadAll(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException("고객사 매핑 파일을 찾을 수 없습니다.", jsonPath);
            }
            string raw = File.ReadAllText(jsonPath, Encoding.UTF8);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 10485760;
            List<CustomerInfo> list = ser.Deserialize<List<CustomerInfo>>(raw);
            if (list == null) return new List<CustomerInfo>();
            return list;
        }
    }
}
