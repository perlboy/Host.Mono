using Host.Library.Services;
using Integrator.Hub.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Services.Core.Adapters
{
    public class MSSQLOutboundService : TwowayOutboundService
    {
        private string _name = "mssqlOutboundService";

        public override string Name
        {
            get
            {
                return _name;
            }
        }
        public override async Task Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception ex)
            {
                this.HostChannel.LogEvent(string.Format("Send Adapter {0} was could not be started. {1}", this.Name, ex.Message));
            }
        }
        public override async Task Stop()
        {
            base.Stop();
        }
        public override List<ValidationError> Validate()
        {
            return base.Validate();
        }
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            // Get base config
            var actionConfig = base.GetAdapterConfigMetadata();

            actionConfig.id = this.Name;
            actionConfig.type = this.Name;
            actionConfig.baseType = "twowaysendadapter";
            actionConfig.title = "Send SQL";
            actionConfig.assemblyType = this.GetType().FullName;
            actionConfig.description = "Transmits messages to a SOAP endpoint.";

            // Set collections
            actionConfig.config.staticConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                                new ConfigProperty{id="server", name = "Server", description = "Name of ther server", type= "string", value = "."},
                                new ConfigProperty{id="database", name = "Database", description = "Initial cataloge", type = "string", value = "master" },
                          
                            });
            actionConfig.config.securityConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                                new ConfigProperty{id="authType", name = "Autorization Type", description = "", type= "string", acceptableValues = new List<string>{"INTEGRATED", "USERNAME/PASSWORD"}, value="INTEGRATED"},
                                new ConfigProperty{id="userName", name = "User name", description = "User name", type= "string"},
                                new ConfigProperty{id="password", name = "Password", description = "Password", type= "password"}
                            });


            return actionConfig;

        }

        public override async Task ProcessMessage(IntegrationMessage message, bool isFirstAction)
        {
            string errorStatement = string.Empty;
            try
            {
                message = RunInboundScript(message);

                string jsonResponse = string.Empty;
                var actionConfig = this.GetActionConfig(message.Variables);
                var dataSource = actionConfig.config.staticConfig.FirstOrDefault(c => c.id == "server").value.ToString();
                var database = actionConfig.config.staticConfig.FirstOrDefault(c => c.id == "database").value.ToString();
                var authType = actionConfig.config.securityConfig.FirstOrDefault(c => c.id == "authType").value.ToString();
                var username = actionConfig.config.securityConfig.FirstOrDefault(c => c.id == "userName").value.ToString();
                var password = actionConfig.config.securityConfig.FirstOrDefault(c => c.id == "password").value.ToString();

                var json = message.message.ToString();
                var sqlStatement = JsonConvert.DeserializeObject<SqlStatement>(json);
                

                var connectionString = new SqlConnectionStringBuilder{
                    DataSource = dataSource,
                    InitialCatalog = database,
                    IntegratedSecurity = authType == "INTEGRATED",
                    UserID = username,
                    Password = password
                }.ConnectionString;

                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    if (sqlStatement.type.ToUpper() == "SELECT" || sqlStatement.type.ToUpper() == "EXEC")
                    {
                        var selectStatement = JsonConvert.DeserializeObject<SelectStatement>(json);
                        using (SqlDataAdapter da = new SqlDataAdapter(selectStatement.command, connection))
                        {
                            DataTable dt = new DataTable();
                            da.Fill(dt);
                            var lst = GetTableRows(dt);
                            jsonResponse = JsonConvert.SerializeObject(lst);
                        }

                    }
                    else if (sqlStatement.type.ToUpper() == "INSERT")
                    {
                        var isertStatement = JsonConvert.DeserializeObject<InsertStatement>(json);
                        var command = new SqlCommand(isertStatement.command, connection);
                        var count = command.ExecuteNonQuery();
                        jsonResponse = JsonConvert.SerializeObject(new { Count = count });
                    }
                    else if (sqlStatement.type.ToUpper() == "DELETE")
                    {
                        var deleteStatement = JsonConvert.DeserializeObject<DeleteStatement>(json);
                        var command = new SqlCommand(deleteStatement.command, connection);
                        var count = command.ExecuteNonQuery();
                        jsonResponse = JsonConvert.SerializeObject(new { Count = count });
                    }
                    else if (sqlStatement.type.ToUpper() == "UPDATE")
                    {
                        var updateStatement = JsonConvert.DeserializeObject<UpdateStatement>(json);
                        var command = new SqlCommand(updateStatement.command, connection);
                        var count = command.ExecuteNonQuery();
                        jsonResponse = JsonConvert.SerializeObject(new { Count = count });
                    }
                    else
                    {
                        throw new ApplicationException("Unsupported SQL method");
                    }
                }
                var msg = message.Clone();
                msg.CreatedBy = this.Config.id;
                msg.AddMessage(Encoding.UTF8.GetBytes(jsonResponse));

                message = RunOutboundScript(msg);

                this.HostChannel.SubmitMessage(msg, this.PersistOptions, this);

            }
            catch (Exception ex)
            {
                this.HostChannel.LogEvent(string.Format("Unable to submit request to: {0}", ex.Message));
                message.IsFault = true;
                message.FaultCode = string.Empty;
                message.FaultDescripton = ex.Message;
                this.HostChannel.SubmitMessage(message, this.PersistOptions, this);
            }
        }
        public override byte[] GetImage()
        {
            return Host.Services.Core.Resources.SqlSendAdapter;
        }
        private List<Dictionary<string, object>> GetTableRows(DataTable dtData)
        {
            List<Dictionary<string, object>>
            lstRows = new List<Dictionary<string, object>>();
            Dictionary<string, object> dictRow = null;

            foreach (DataRow dr in dtData.Rows)
            {
                dictRow = new Dictionary<string, object>();
                foreach (DataColumn col in dtData.Columns)
                {
                    dictRow.Add(col.ColumnName, dr[col]);
                }
                lstRows.Add(dictRow);
            }
            return lstRows;
        }
    }
    public class SqlStatement
    {
        public string type { get; set; }
    }
    public class SelectStatement : SqlStatement 
    {
        public string command { get; set; }
    }
    public class InsertStatement : SqlStatement
    {
        public string table { get; set; }
        public JArray dataRows { get; set; }
        public string command
        {
            get
            {
                string commands = string.Empty;

                var columnString = string.Empty;
                var firstRow = this.dataRows.FirstOrDefault() as JObject;
                if (firstRow == null)
                    return string.Empty;
                var columns = firstRow.Children();

                // Get Columns
                foreach (JProperty column in firstRow.Children())
                {
                    columnString += column.Name + ", ";
                }
                columnString = columnString.Substring(0, columnString.LastIndexOf(", "));
                var command = string.Format("INSERT INTO {0} ({1})", this.table, columnString);

                // Get values
                foreach (var row in this.dataRows)
                {
                    string rowString = " VALUES(";
                    foreach (var col in row.Children())
                    {
                        var val = ((JProperty)col).Value;
                        rowString += IsNumeric(val.ToString()) ? val.ToString() : string.Format("'{0}'", val.ToString()) + ", ";
                    }
                    rowString = rowString.Substring(0, rowString.LastIndexOf(", ")) + ")";

                    commands += command + rowString + "\n";
                }


                return commands;
            }
        }
        private bool IsNumeric(string val)
        {
            float output;
            return float.TryParse(val, out output);
        }
    }
    public class DeleteStatement : SqlStatement
    {
        public string table { get; set; }
        public JArray dataRows { get; set; }
        public JArray idColumns { get; set; }
        public string command
        {
            get
            {
                string commands = string.Empty;

                // Get values
                foreach (var row in this.dataRows)
                {
                    string whereClause = string.Empty;
                    
                    foreach (var col in this.idColumns)
                    {
                        var val = row.Children<JProperty>().FirstOrDefault(c=>c.Name == col.ToString()).Value.ToString();
                        whereClause += string.Format("[{0}] = {1} AND",
                            col.ToString(),
                            IsNumeric(val) ? val : string.Format(@"'{0}'", val));
                    }

                    whereClause = whereClause.Substring(0, whereClause.LastIndexOf(" AND")) + "";

                    var command = string.Format("DELETE FROM {0} WHERE {1}", this.table, whereClause);
                    commands += command  + "\n";
                }

                return commands;
            }
        }
        private bool IsNumeric(string val)
        {
            float output;
            return float.TryParse(val, out output);
        }
    }
    public class UpdateStatement : SqlStatement
    {
        public string table { get; set; }
        public JArray dataRows { get; set; }
        public JArray idColumns { get; set; }
        public string command
        {
            get
            {
                string commands = string.Empty;

                // Get values
                foreach (var row in this.dataRows)
                {
                    string whereClause = string.Empty;

                    // SET Statement
                    string setSection = string.Empty;
                    foreach (JProperty col in row.Children<JProperty>())
                    {
                        var val = ((JProperty)col).Value;
                        setSection += IsNumeric(val.ToString()) ?
                            string.Format("[{0}] = {1}, ", col.Name, val.ToString()) :
                            string.Format("[{0}] = '{1}', ", col.Name, val.ToString());
                    }
                    setSection = setSection.Substring(0, setSection.LastIndexOf(", "));

                    // WHERE Statement
                    foreach (var col in this.idColumns)
                    {
                        var val = row.Children<JProperty>().FirstOrDefault(c => c.Name == col.ToString()).Value.ToString();
                        whereClause += string.Format("[{0}] = {1} AND",
                            col.ToString(),
                            IsNumeric(val) ? val : string.Format(@"'{0}'", val));
                    }

                    whereClause = whereClause.Substring(0, whereClause.LastIndexOf(" AND"));

                    var command = string.Format("UPDATE {0} SET {1} WHERE {2}", this.table, setSection, whereClause);
                    commands += command + "\n";
                }

                return commands;
            }
        }
        private bool IsNumeric(string val)
        {
            float output;
            return float.TryParse(val, out output);
        }
    }
}
