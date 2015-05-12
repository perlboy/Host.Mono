using Host.Library.Services;
using Host.Library.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Integrator.Hub.Models;
namespace Host.Library.Helpers
{
    public class ItineraryHelper
    {
        public List<Activity> OrderActions(dynamic itinerary)
        {
            // CACHE THIS!

            var jActivities = (from a in itinerary["activities"] as JArray
                               where a["type"].ToString() != "draw2d.Connection"
                               select a).ToList();

            // Get all activities
            var jConnections = from a in itinerary["activities"] as JArray
                               where a["type"].ToString() == "draw2d.Connection"
                               select a;

            // Get all activities with no source connections
            var targets = (from t in jConnections
                           where t["target"]["port"].ToString().StartsWith("input")
                           select t["target"]["node"].ToString()).ToList();

            targets.AddRange((from t in jConnections
                              where t["source"]["port"].ToString().StartsWith("input")
                              select t["source"]["node"].ToString()).ToList());


            var activities = (from a in jActivities
                              select new Activity
                              {
                                  Id = Guid.Parse(a["id"].ToString()),
                                  BaseType = a["type"].ToString(),
                                  Name = a["userData"]["id"].ToString(),
                                  Config = a["userData"]["config"]["generalConfig"].ToString(),
                                  Type = a["userData"]["type"].ToString(),
                                  Host = GetHost(a)
                              }).ToList();
            // Add Hosts
            foreach (var a in activities)
            {
                var config = JsonConvert.DeserializeObject<List<ConfigProperty>>(a.Config);
                var hostName = config.First(c => c.name == "Host").value.ToString();
                a.IntegrationHosts.Add(hostName);
            }

            // Get all activities with no source
            var startActivities = (from a in activities.Where(a => !targets.Contains(a.Id.ToString()))
                                   select a).ToList();

            foreach (var activity in startActivities)
            {
                PopulateActivities(activity, activities.ToList(), jConnections);
            }

            return activities.ToList();
        }

        public void PopulateActivities(Activity parent, List<Activity> activities, IEnumerable<JToken> connections)
        {
            var childActivities = (from c in connections
                                       where Guid.Parse(c["source"]["node"].ToString()) == parent.Id
                                       select activities.FirstOrDefault(a => a.Id.ToString() == c["target"]["node"].ToString())).ToList();



            //var childActivities = activities.Where(a => connectedActivities.Contains(a.Id.ToString()));

            foreach (Activity childActivity in childActivities)
            {
                parent.Successors.Add(childActivity);
                if (childActivity.BaseType != "twowayreceiveadapter")
                    PopulateActivities(childActivity, activities, connections);
            }
        }

        private string GetHost(JToken o)
        {
            var host = o["userData"]["config"]["generalConfig"].FirstOrDefault(c => c["id"].ToString() == "host");

            return host == null ? string.Empty : host["value"].ToString();
        }

        public void SetStatus(dynamic itinerary, Guid activityId, string status)
        {
            // CACHE THIS!

            var activities = (from a in itinerary["activities"] as JArray
                               where a["id"].ToString() == activityId.ToString()
                               select a).ToList();

            foreach (var activity in activities)
            {
                activity["status"] = status;
            }
            string s = JsonConvert.SerializeObject(itinerary);
        }
    }
}
