﻿using Microsoft.AspNet.Identity;
using Rhino.Licensing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using System.Text;
using Mvc5StarterKit.IzendaBoundary;
using System.Threading.Tasks;
using Mvc5StarterKit.Models;
using Mvc5StarterKit.IzendaBoundary.Models;
using Mvc5StarterKit.IzendaBoundary.Models.Criteria;
using log4net;
using Mvc5StarterKit.IzendaBoundary.Models.Permissions;
using IzendaFramework = Izenda.BI.Framework.Models.DBStructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Izenda.BI.RDBMS.Constants;

namespace Mvc5StarterKit.Controllers
{
    public class HomeController : Controller
    {
        private static readonly ILog logger = LogManager.GetLogger("MVC5KitLogger");

        public ActionResult Index()
        {
            return View();
        }

       
        public ActionResult ReadMe()
        {
            return View();
        }

        public ActionResult API()
        {
            var model = new APIModel();
            model.AvailableMethods.Add(new SelectListItem { Value = "1", Text = "Add Role", Selected = model.APIMethodId == 1 });
            model.AvailableMethods.Add(new SelectListItem { Value = "2", Text = "Add Table/View/SP", Selected = model.APIMethodId == 2 });
            return View(model);
        }

        /// <summary>
        /// The action will be called when click 'Submit' button on API page.
        /// APIModel is the view model of the page. 
        /// APIModel.APIMethodId = 1 correspond to 'Add Role' in the ddl.
        /// APIModel.APIMethodId = 2 correspond to 'Add Table/View/SP' in the ddl.
        /// We will hardcode the TenantUniqueName to add role,table,view,sp,function to.
        /// The token for calling API will be generated from the configuration: izusername, iztenantname
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> API(APIModel model)
        {
            bool isSuccess = true;

            try
            {
                //On UI, If we select 'Add Role' in the ddl
                if (model.APIMethodId == 1)
                {
                    isSuccess = await AddRole();
                }
                //On UI, If we select Add Table/View/SP
                else if (model.APIMethodId == 2)
                {
                    isSuccess = await AddTableViewSPFunction();
                }
            }
            catch (WebApiException ex)
            {
                logger.Error($"Error occurred when calling Izenda REST Api, url: {ex.RequestedUrl}, HttpStatusCode: {ex.StatusCode}", ex);
                isSuccess = false;
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
                isSuccess = false;
            }

            //buid model again for view
            model.AvailableMethods.Add(new SelectListItem { Value = "1", Text = "Add Role", Selected = model.APIMethodId == 1 });
            model.AvailableMethods.Add(new SelectListItem { Value = "2", Text = "Add Table/View/SP", Selected = model.APIMethodId == 2 });

            if (isSuccess == true)
            {
                model.Message = "DONE. Please see the log for information.";
            }
            else
            {
                model.Message = "ERROR. Please see the log for information.";
            }
            return View(model);
        }

        /// <summary>
        /// Add a new role if the role does not exist.
        /// We will hardcode the role name, some permissions and assign an existing user to this role.
        /// 
        /// Step 1: Check if the hardcoded role name is already exist by
        ///         - Call API to get all roles of the tenant and then search by name to check exist.
        /// Step 2: If the hardcoded role name does not exist then
        ///         - Setup a new RoleDetail model
        ///         - Hardcoded some permissions
        ///         - Hardcoded an existing user name, check if this user is already existed by calling API
        ///           to get all users of the tenant and then search by name.
        ///           If user is not existed, log an error and return.
        ///           If user is existed, setup a new UserDetail model and add it to RoleDetail.
        /// Step 3: Call API to save role.
        /// </summary>
        private async Task<bool> AddRole()
        {
            //HARDCODED tenantUniqueName, we will add role to this tenant
            //If tenantUniqueName is empty, data will be added to the default 'System' tenant with tenantId = null
            var tenantUniqueName = "DELDG";

            //HARDCODED role name
            var roleName = "role8";

            //HARDCODED an existing user
            var userName = "manager@deldg.com";


            //Get the encrypted token from the configuration: izusername, iztenantname
            //Use this token when calling Izenda Rest API
            var token = GetToken();


            logger.Info("----------- Start adding role -----------");

            //Get tenantId by calling API to get list of tenants then search by tenantUniqueName
            //If tenantUniqueName is not existed then throw exception.
            //Use this tenantId when calling Izenda Rest API
            Guid? tenantId = null;

            //GET /tenant/allTenants
            var tenants = await IzendaUtility.GetTenants(token);

            if (!string.IsNullOrEmpty(tenantUniqueName))
            {
                var tenant = tenants.Where(t => t.Name == tenantUniqueName).FirstOrDefault();
                if (tenant == null)
                {
                    logger.ErrorFormat("Tenant = {0} is not existed.", tenantUniqueName);
                    throw new ArgumentException("Invalid tenantUniqueName.");
                }
                else
                {
                    tenantId = tenant.Id;
                }
            }


            //Setup RoleDetail model to pass to API
            //only need to pass TenantUniqueName to POST /role/intergration/saveRole, no need tenantId
            var role = new RoleDetail();
            role.TenantUniqueName = tenantUniqueName;
            role.Name = roleName;
            role.Active = true;
            role.NotAllowSharing = false;
            role.Permission = new Permission();

            //HARDCODED some permissions
            role.Permission.DataSetup.DataModel.Value = true;
            role.Permission.RoleSetup.Actions.Create = true;
            role.Permission.RoleSetup.Actions.Edit = true;
            role.Permission.RoleSetup.Actions.Del = true;

            //Check if hardcoded user is existed by get all users of tenant and search by name
            //GET user/all/(tenant_id)
            var users = await IzendaUtility.GetUsers(tenantId, token);
            if (!string.IsNullOrEmpty(userName))
            {
                var user = users.Where(u => u.UserName == userName).FirstOrDefault();
                if (user == null)
                {
                    logger.ErrorFormat("user = {0} is not existed", userName);
                    return false;
                }
                else
                {
                    //new UserDetail model
                    var userDetail = new UserDetail
                    {
                        Id = user.Id,
                        UserName = user.UserName
                    };

                    //add to RoleDetail model
                    role.Users.Add(userDetail);
                }
            }

            //Call API to create role
            //POST /role/intergration/saveRole
            await IzendaUtility.SaveRole(role, token);

            logger.InfoFormat("Role = {0} created.", roleName);

            logger.Info("----------- End adding role -----------");

            return true;
        }

        /// <summary>
        /// Add new table/view/sp/function to visible data sources of a connection
        /// and then attach it to an existing role.
        /// We will hardcode the ConnectionName, TableName, ViewName, SPName, FunctionName, RoleName
        /// 
        /// Step 1: Check if the hardcoded connection name is already existed by
        ///         - Call API to get all connections of the tenant and then search by name to check existed.
        ///         - If not existed, log error and return.
        /// Step 2: Get connection detail by calling API to reload the remote schema.
        ///         This will support the case when user add a table, view, ... to database, 
        ///         but in Izenda it still not refresh in the Available Data Sources yet.
        /// Step 3: Validate the connection detail's data. If not valid, log error and return
        /// Step 4: For hardcoded TableName, ViewName, SPName, FunctionName, check if it existed in the
        ///         connection Available Data Sources.
        ///         If not, log error and return.
        ///         If existed, set the Selected flag to true to move it to Visible Data Sources.
        /// Step 5: Call API to update the connection (add table,view,sp,function to Visible Data Sources).
        /// Step 6: Check if the hardcoded role name is existed.
        ///         If not, log error and return.
        ///         If existed, call API to get role detail.
        /// Step 7: Check if the table,view,sp,function is already in the Visible Data Sources of the role.
        ///         If not, add it to the role's Visible Data Sources.
        /// Step 8: Call API to save role (attach table,view,sp,function to role).
        /// </summary>
        private async Task<bool> AddTableViewSPFunction()
        {
            //HARDCODED tenantUniqueName, we will add table, view, sp, function to this tenant
            //If tenantUniqueName is empty, data will be added to the default 'System' tenant with tenantId = null
            var tenantUniqueName = "DELDG";

            //HARDCODED values
            var connectionName = "retail";
            var tableName = "Employee";
            var viewName = "OrdersByRegion";
            var spName = "";
            var functionName = "";
            var roleName = "role8";

            //private variables
            bool needToUpdate = false;
            logger.Info("----------- Start adding Table/View/SP/Function -----------");

            //Get the encrypted token from the configuration: izusername, iztenantname
            //Use this token when calling Izenda Rest API
            var token = GetToken();

            //Get tenantId by calling API to get list of tenants then search by tenantUniqueName
            //If tenantUniqueName is not existed then throw exception.
            //Use this tenantId when calling Izenda Rest API
            Guid? tenantId = null;

            //GET /tenant/allTenants
            var tenants = await IzendaUtility.GetTenants(token);

            if (!string.IsNullOrEmpty(tenantUniqueName))
            {
                var tenant = tenants.Where(t => t.Name == tenantUniqueName).FirstOrDefault();
                if (tenant == null)
                {
                    logger.ErrorFormat("Tenant = {0} is not existed.", tenantUniqueName);
                    throw new ArgumentException("Invalid tenantUniqueName.");
                }
                else
                {
                    tenantId = tenant.Id;
                }
            }


            QuerySourceModel table = null;
            QuerySourceModel view = null;
            QuerySourceModel sp = null;
            QuerySourceModel function = null;

            //Get connection by name by calling API to get all connections then search by name.
            //GET /connection/(tenant_id)
            var connections = await IzendaUtility.GetConnections(tenantId, token);
            var conn = connections.SingleOrDefault(x => x.Name.Equals(connectionName, StringComparison.InvariantCultureIgnoreCase));
            if(conn == null)
            {
                logger.ErrorFormat("The connection = {0} is not existed.", connectionName);
                return false;
            }

            //Get connection detail by reloading the remote schema
            //This will support the case when a user adds a table to the database 
            //But we must perform additional steps to successfully move the table from the available data sources to the visible data sources
            var connectionModel = await IzendaUtility.ReloadRemoteSchema(
                new
                {
                    ConnectionId = conn.Id,
                    ConnectionString = conn.ConnectionString,
                    ServerTypeId = conn.ServerTypeId
                }, token);


            #region update Connection Detail Model

            //The DBSource has the QuerySources sorted into categories based on schema and each schema has QuerySources sorted into types (table, view, stored procedure, function)
            var querySources = connectionModel.DBSource.QuerySources.SelectMany(qs => qs.QuerySources);

            foreach (var source in querySources)
            {
                //Create a new QuerySourceFieldPagedRequestModel as criteria for obtaining the fields for the QuerySource using the QuerySource ID and type
                var req = new QuerySourceFieldPagedRequestModel()
                {
                    QuerySource = new QuerySourceModel()
                    {
                        Id = source.Id,
                        Type = source.Type
                    }
                };

                var result = await IzendaUtility.LoadQuerySourceFields(req, token);
                source.QuerySourceFields = result.Result;
                
                if (new string[] { tableName, viewName, spName, functionName }.Any(s => !string.IsNullOrWhiteSpace(s) && s.Equals(source.Name)))
                {
                    switch(source.Type)
                    {
                        case SQLQuerySourceType.Table: table = source;
                            break;
                        case SQLQuerySourceType.View: view = source;
                            break;
                        case SQLQuerySourceType.Procedure: sp = source;
                            break;
                        case SQLQuerySourceType.Function: function = source;
                            break;
                        default:
                            break;
                    }
                    //To add table,view,sp,function to visible Data Sources, we only need to set the Selected flag to true
                    source.Selected = true;
                    needToUpdate = true;
                    //optionally set the query sources' fields' visibility and filterability if the automatic visibility/filterability option is not enabled
                    //foreach (var field in source.QuerySourceFields)
                    //{
                    //    field.Visible = true;
                    //    field.Filterable = true;
                    //}
                }
            }

            #endregion

            if (needToUpdate == true)
            {
                conn.DBSource = connectionModel.DBSource;

                //update connnection visible data sources
                //POST /connection
                await IzendaUtility.UpdateConnectionDetail(conn, token);

                logger.Info($"Succesfully updated visible data sources for connection [{connectionModel.Id}].");

                //If we input RoleName, we will attach the added table,view,sp,function to this existing role
                if (!string.IsNullOrEmpty(roleName))
                {
                    //Get roles of tenant
                    //GET role/all/(tenant_id)
                    var roles = await IzendaUtility.GetRoles(tenantId, token);

                    //Check if role is existed
                    var existingRole = roles.Where(r => r.Name == roleName).FirstOrDefault();
                    if (existingRole != null)
                    {
                        logger.Info($"Role {roleName} is already exists => continue to update Data Model Permissions");

                        //Get role detail
                        //GET /role/{role_id}
                        var role = await IzendaUtility.GetRole(existingRole.Id, token);

                        #region update Data Model Permissions of role

                        //if role doesn't have any VisibleQuerySources, instantiate it
                        if (role.VisibleQuerySources == null)
                        {
                            role.VisibleQuerySources = new List<QuerySourceModel>();
                        }

                        foreach(var source in new List<QuerySourceModel> { table, view, sp, function})
                        {
                            //Check if the added table, view, sp, function already in VisibleQuerySources. If it doesn't exist, attach it to the role
                            if (source != null)
                            {
                                if(role.VisibleQuerySources.Where(q => q.Id == source.Id).FirstOrDefault() == null)
                                    role.VisibleQuerySources.Add(BuildQuerySourceForRole(source, querySources.ToList()));
                                else
                                    logger.Info($"{source.Type} {source.Name} is already in the VisibleQuerySources of Role {roleName}");
                            }
                        }

                        #endregion

                        //need to set TenantUniqueName to use POST /role/intergration/saveRole
                        role.TenantUniqueName = tenantUniqueName;

                        //Call API to update role
                        //POST /role/intergration/saveRole
                        await IzendaUtility.SaveRole(role, token);
                    }
                    else
                    {
                        logger.Error($"Role {roleName} does not exist. Please specify an existing role to update Data Model Permissions");
                        return false;
                    }
                }
            }

            logger.Info("----------- End adding Table/View/SP/Function -----------");

            return true;
        }

        /// <summary>
        /// From the table, view, sp, function added, we will build a new QuerySourceModel with all QuerySourceFields to attach it to the Role
        /// </summary>
        /// <param name="model">table, view, sp, function QuerySourceModel</param>
        /// <param name="availableQuerySources">Connection Available Query Sources</param>
        /// <returns>A new QuerySourceModel to attach to Role</returns>
        private QuerySourceModel BuildQuerySourceForRole(QuerySourceModel model, List<QuerySourceModel> availableQuerySources)
        {
            var result = new QuerySourceModel();
            result.Id = model.Id;
            result.QuerySourceFields = new List<QuerySourceFieldModel>();

            //add all QuerySourceFields
            var querySource = availableQuerySources.Where(q => q.Id == model.Id).FirstOrDefault();
            if (querySource != null)
            {
                foreach (var field in querySource.QuerySourceFields)
                {
                    result.QuerySourceFields.Add(new QuerySourceFieldModel { Id = field.Id });
                }
            }

            return result;
        }


        /// <summary>
        /// Get user/pwd and tenant info from web config file to authorize with Izenda Api
        /// In all (backend and front end) are integrated mode, authentication information will get from hosting web and send to izenda to authenticate.
        /// In standalone mode, hosting app will need to send user/pwd to izenda to authenticate.
        /// </summary>
        /// <returns></returns>
        private string GetToken()
        {
            var username = System.Configuration.ConfigurationManager.AppSettings["izusername"];
            var tenantUniqueName = System.Configuration.ConfigurationManager.AppSettings["iztenantuniquename"];
            if (string.IsNullOrEmpty(tenantUniqueName))
            {
                tenantUniqueName = "System";
            }
            var token = IzendaTokenAuthorization.GetToken(new UserInfo { UserName = username, TenantUniqueName = tenantUniqueName });
            return token;
        }

        #region Izenda Actions

        [Route("izenda/settings")]
        [Route("izenda/new")]
        [Route("izenda/dashboard")]
        [Route("izenda/report")]
        [Route("izenda/reportviewer")]
        [Route("izenda/reportviewerpopup")]
        [Route("izenda")]
        public ActionResult Izenda()
        {
            return View();
        }

        //[Route("izendasetting")]
        public ActionResult Settings()
        {
            return View();
        }

        public ActionResult Reports()
        {
            return View();
        }


        public ActionResult ReportDesigner()
        {
            return View();
        }

        public ActionResult Dashboards()
        {
            return View();
        }

        public ActionResult DashboardDesigner()
        {
            return View();
        }

        public ActionResult ReportPart(Guid id, string token)
        {
            ViewBag.Id = id;
            ViewBag.Token = token;
            return View();
        }

        /// <summary>
        /// Create a custom route to intercept login requests for the Izenda API. This is needed for the 
        /// Izenda Copy Console as it will only authenticate against "api/user/login".
        /// </summary>
        /// <param name="username">the username</param>
        /// <param name="password">the password</param>
        /// <returns>a json result indicating success or failure</returns>
        public ActionResult CustomAuth(string username, string password)
        {
            OperationResultModel authResult;
            var serializerSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
            var jsonResult = "";

            //validate login (more complex logic can be added here)
            #warning CAUTION!! Update this method to use your authentication scheme or remove it entirely if the copy console will not be used.
            if (username == "IzendaAdmin@system.com" && password == "Izenda@123")
            {
                var user = new UserInfo { UserName = username, TenantUniqueName = "System" };
                var token = IzendaTokenAuthorization.GetToken(user);

                var accessToken = new IzendaFramework.AccessToken
                {
                    CultureName = "en-US",
                    Tenant = null,
                    IsExpired = false,
                    NotifyDuringDay = null,
                    DateFormat = "DD/MM/YYYY",
                    Token = token
                };

                authResult = new OperationResultModel { Success = true, Messages = null, Data = accessToken };
                jsonResult = JsonConvert.SerializeObject(authResult, serializerSettings);
                return Content(jsonResult, "application/json");
            }

            authResult = new OperationResultModel { Success = false, Messages = null, Data = null };
            jsonResult = JsonConvert.SerializeObject(authResult, serializerSettings);
            return Content(jsonResult, "application/json");
        }
        #endregion
    }
}