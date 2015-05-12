using Host.Library.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace Host.Library.Model
{
    public class Activity
    {
        public Activity()
        {
            this.Successors = new HashSet<Activity>();
            this.Predecessors = new HashSet<Activity>();
            this.IntegrationHosts = new HashSet<string>();
        }
        public System.Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Config { get; set; }
        public string Type { get; set; }
        public string BaseType { get; set; }
        public string Host { get; set; }
        public virtual ICollection<Activity> Successors { get; set; }
        public virtual ICollection<Activity> Predecessors { get; set; }
        public virtual ICollection<string> IntegrationHosts { get; set; }
    }
    
}