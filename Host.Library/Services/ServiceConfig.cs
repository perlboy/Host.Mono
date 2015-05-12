using Integrator.Hub.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Library.Services
{
    public enum PersistOptions { None, Optimistic, Pessimistic }
    public class ServiceTypes
    {
        public const string onewayreceiveadapter = "onewayreceiveadapter";
        public const string onewaysendadapter = "onewaysendadapter";
        public const string twowayreceiveadapter = "twowayreceiveadapter";
        public const string twowaysendadapter = "twowaysendadapter";
        public const string javascriptaction = "javascriptaction";
    }
    
    public class ServiceConfig
    {
        public ServiceConfig()
        {
            config = new ConfigSection();
            config.generalConfig = new List<ConfigProperty>();
            config.securityConfig = new List<ConfigProperty>();
            config.staticConfig = new List<ConfigProperty>();
        }
        public string id { get; set; }
        public string type { get; set; }
        public string baseType { get; set; }
        public string assemblyType { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string image { get; set; }
        public string host { get; set; }
        public ConfigSection config { get; set; }
        public Guid integrationId { get; set; }
        public Guid itineraryId { get; set; }
    }
    public class ConfigSection
    {
        public List<ConfigProperty> generalConfig { get; set; }
        public List<ConfigProperty> staticConfig { get; set; }
        public List<ConfigProperty> dynamicConfig { get; set; }
        public List<ConfigProperty> securityConfig { get; set; }
    }
    public class ConfigProperty
    {
        public ConfigProperty()
        {
            this.category = "Misc";
            this.acceptableValues = new List<string>();
            this.visibility = "visible";
        }
        public string category { get; set; }
        public string id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public object value { get; set; }
        public string type { get; set; }
        public string visibility { get; set; }
        public List<string> acceptableValues { get; set; }
    }
    public class SvgImage
    {
        public List<SvgPath> Paths{ get; set; }
    }
    public class SvgPath
    {
        public string Path { get; set; }
        public string Stroke { get; set; }
        public decimal StrokeWidth { get; set; }
        public string Fill { get; set; }
        public decimal Opacity { get; set; }
    }
}