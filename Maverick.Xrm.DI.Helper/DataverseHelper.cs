﻿using Maverick.Xrm.DI.DataObjects;
using Maverick.Xrm.DI.Extensions;
using Maverick.XTB.DI.DataObjects;
using Maverick.XTB.DI.Extensions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;


namespace Maverick.Xrm.DI.Helper
{
    public class DataverseHelper
    {
        static IOrganizationService Service = null;

        /// <summary>
        /// Rerieve all entities with the given filter conditions
        /// </summary>
        /// <param name="service"></param>
        /// <param name="entityFilters"></param>
        /// <param name="retrieveAsIfPublished"></param>
        /// <returns></returns>
        public static List<EntityMetadata> RetrieveAllEntities(IOrganizationService service, List<EntityFilters> entityFilters = null, bool retrieveAsIfPublished = true)
        {
            Service = service;

            if (entityFilters == null)
            {
                entityFilters = new List<EntityFilters>() { EntityFilters.Default };
            }

            // build the bitwise or list of the entity filters
            var filters = entityFilters.Aggregate<EntityFilters, EntityFilters>(0, (current, item) => current | item);

            var req = new RetrieveAllEntitiesRequest()
            {
                EntityFilters = filters,
                RetrieveAsIfPublished = retrieveAsIfPublished
            };
            var resp = (RetrieveAllEntitiesResponse)service.Execute(req);

            // set the itemsource of the itembox equal to entity metadata that is customizable (takes out systemjobs and stuff like that)
            var entities = resp.EntityMetadata.Where(x => x.IsCustomizable.Value == true).ToList<EntityMetadata>();

            return entities;
        }

        public static List<DependencyReport> GetDependencyList(IOrganizationService service, Guid objectId, int componentType)
        {
            List<DependencyReport> lstReport = new List<DependencyReport>();
            Service = service;

            var dependencyRequest = new RetrieveDependentComponentsRequest
            {
                ObjectId = objectId,
                ComponentType = componentType
            };

            var dependencyResponse = (RetrieveDependentComponentsResponse)service.Execute(dependencyRequest);

            // If there are no dependent components, we can ignore this component.
            if (dependencyResponse.EntityCollection.Entities.Any() == false)
                return lstReport;

            lstReport = ProcessDependencyList(service, dependencyResponse.EntityCollection);

            return lstReport;
        }

        public static List<DependencyReport> ProcessDependencyList(IOrganizationService service, EntityCollection dependencyEC)
        {
            List<DependencyReport> lstReport = new List<DependencyReport>();
            Service = service;

            // If there are no dependent components, we can ignore this component.
            if (dependencyEC.Entities.Any() == false)
                return lstReport;

            Parallel.ForEach(dependencyEC.Entities,
                new ParallelOptions { MaxDegreeOfParallelism = 10 },
                (dependentEntity) =>
                {
                    DependencyReport dr = GenerateDependencyReport(dependentEntity);
                    if (!dr.SkipAdding)
                    {
                        lstReport.Add(dr);
                    }
                });

            return lstReport;
        }

        #region Private Methods

        private static DependencyReport GenerateDependencyReport(Entity dependency)
        {
            DependencyReport dependencyReport = new DependencyReport();

            var lstComponentTypes = System.Enum.GetValues(typeof(Enum.ComponentType)).Cast<Enum.ComponentType>()
                                    .ToList()
                                    .Select(ct => new
                                    {
                                        DisplayValue = ct.GetAttribute<DI.CustomAttributes.DisplayAttribute>().Name,
                                        Component = (int)ct
                                    });
            /*foreach (var ct in lstComponentTypes)
            {
                var text = ct.GetAttribute<DI.CustomAttributes.DisplayAttribute>();

                if (((OptionSetValue)dependency["dependentcomponenttype"]).Value == (int)ct)
                {
                    dependencyReport.DependentComponentType = text.Name;
                }
                if (((OptionSetValue)dependency["requiredcomponenttype"]).Value == (int)ct)
                {
                    dependencyReport.RequiredComponentType = text.Name;
                }
            }*/

            var dependentOptionSet = ((OptionSetValue)dependency["dependentcomponenttype"]).Value;
            var requiredOptionSet = ((OptionSetValue)dependency["requiredcomponenttype"]).Value;

            var dependentType = lstComponentTypes.FirstOrDefault(dct => dependentOptionSet == dct.Component);
            var requiredType = lstComponentTypes.FirstOrDefault(dct => requiredOptionSet == dct.Component);

            dependencyReport.DependentComponentType = dependentType?.DisplayValue;
            dependencyReport.RequiredComponentType = requiredType?.DisplayValue;

            ComponentInfo dependentCI = GetComponentInfo(((OptionSetValue)dependency["dependentcomponenttype"]).Value, (Guid)dependency["dependentcomponentobjectid"]);
            if (dependentCI != null 
                && !string.IsNullOrEmpty(dependencyReport.DependentComponentType)
                && !string.IsNullOrEmpty(dependencyReport.RequiredComponentType))
            {
                dependencyReport.DependentComponentName = dependentCI.Name;
                dependencyReport.DependentDescription = dependentCI.Description;

                if (dependentCI.IsDashboard)
                {
                    dependencyReport.DependentComponentType = "Dashboard";
                }
            }
            else
            {
                dependencyReport.SkipAdding = true;
            }

            ComponentInfo requiredCI = GetComponentInfo(((OptionSetValue)dependency["requiredcomponenttype"]).Value, (Guid)dependency["requiredcomponentobjectid"]);
            if (requiredCI != null)
            {
                dependencyReport.RequiredComponentName = requiredCI.Name;
            }

            // Disabled for testing
            if (!dependencyReport.SkipAdding
                && (string.IsNullOrEmpty(dependencyReport.DependentComponentName)
                || string.IsNullOrEmpty(dependencyReport.RequiredComponentName)))
            {
                dependencyReport.SkipAdding = true;
            }

            return dependencyReport;
        }

        // The name or display name of the component depends on the type of component.
        private static ComponentInfo GetComponentInfo(int componentType, Guid componentId)
        {
            ComponentInfo info = null;

            switch (componentType)
            {
                case (int)Enum.ComponentType.Entity:
                    info = new ComponentInfo();
                    info.Name = componentId.ToString();
                    break;
                case (int)Enum.ComponentType.Attribute:
                    //name = GetAttributeInformation(componentId);
                    info = GetAttributeInformation(componentId);
                    break;
                case (int)Enum.ComponentType.OptionSet:
                    info = GetGlobalOptionSet(componentId);
                    break;
                case (int)Enum.ComponentType.SystemForm:
                    info = GetFormDisplay(componentId);
                    break;
                case (int)Enum.ComponentType.EntityRelationship:
                    info = GetEntityRelationship(componentId);
                    break;
                case (int)Enum.ComponentType.SDKMessageProcessingStep:
                    info = GetSdkMessageProcessingStep(componentId);
                    break;
                case (int)Enum.ComponentType.EntityMap:
                    info = GetEntityMap(componentId);
                    break;
                case (int)Enum.ComponentType.SavedQuery:
                    info = GetSavedQuery(componentId);
                    break;
                case (int)Enum.ComponentType.ModelDrivenApp:
                    info = GetModelDrivenApp(componentId);
                    break;
                case (int)Enum.ComponentType.SiteMap:
                    info = GetSitemap(componentId);
                    break;
                case (int)Enum.ComponentType.MobileOfflineProfile:
                    info = GetMobileProfile(componentId);
                    break;
                case (int)Enum.ComponentType.EmailTemplate:
                    info = GetEmailTemplate(componentId);
                    break;
                case (int)Enum.ComponentType.MailMergeTemplate:
                    info = GetMailMergeTemplate(componentId);
                    break;
                case (int)Enum.ComponentType.Report:
                    info = GetReport(componentId);
                    break;
                case (int)Enum.ComponentType.CanvasApp:
                    info = GetCanvasApp(componentId);
                    break;
                case (int)Enum.ComponentType.Workflow:
                    info = GetWorkflow(componentId);
                    break;
                case (int)Enum.ComponentType.FieldSecurityProfile:
                    info = GetFieldSecurityProfile(componentId);
                    break;
                default:
                    //name = $"{componentType} - Not Implemented";
                    break;
            }

            return info;

        }

        private static ComponentInfo GetAttributeInformation(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                RetrieveAttributeRequest req = new RetrieveAttributeRequest
                {
                    MetadataId = id
                };
                RetrieveAttributeResponse resp = null;

                if (Service is CrmServiceClient svc)
                {
                    resp = (RetrieveAttributeResponse)svc.Execute(req);
                }
                else
                {
                    resp = (RetrieveAttributeResponse)Service.Execute(req);
                }

                AttributeMetadata attmet = resp.AttributeMetadata;
                info.Name = attmet.SchemaName;
                info.Description = $"Entity: {attmet.EntityLogicalName}, Label: {attmet.DisplayName.UserLocalizedLabel.Label}";

                return info;
            }
            catch (Exception ex)
            {
                Telemetry.TrackException(ex);
                return null;
            }
        }

        private static ComponentInfo GetGlobalOptionSet(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                RetrieveOptionSetRequest req = new RetrieveOptionSetRequest
                {
                    MetadataId = id
                };
                RetrieveOptionSetResponse resp = (RetrieveOptionSetResponse)Service.Execute(req);
                OptionSetMetadataBase os = resp.OptionSetMetadata;
                info.Name = os.DisplayName.UserLocalizedLabel.Label;
                info.Description = $"Schema: {os.Name}, Is Global: {os.IsGlobal}";

                return info;
            }
            catch (Exception ex)
            {
                Telemetry.TrackException(ex);
                return null;
            }
        }

        private static ComponentInfo GetFormDisplay(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eForm = Service.Retrieve("systemform", id, new ColumnSet("name", "objecttypecode", "type", "formactivationstate"));
                if (eForm != null && eForm.Contains("type") && eForm.Contains("name"))
                {
                    info.Name = $"{eForm["name"]}";
                    info.Description = $"Entity: {eForm["objecttypecode"]}, Type: {eForm.GetFormattedValue("type")}, Status: {eForm.GetFormattedValue("formactivationstate")}";
                }

                if (eForm.FormattedValues["type"] == "Dashboard")
                {
                    info.IsDashboard = true;
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetSavedQuery(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eSavedQuery = Service.Retrieve("savedquery", id, new ColumnSet("name", "returnedtypecode", "statuscode", "isdefault"));
                if (eSavedQuery != null && eSavedQuery.Contains("name") && eSavedQuery.Contains("returnedtypecode"))
                {
                    info.Name = $"{eSavedQuery["name"]}";
                    info.Description = $"Entity: {eSavedQuery["returnedtypecode"]}, Status: {eSavedQuery.GetFormattedValue("statuscode")}, Is Default: {eSavedQuery["isdefault"]}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetEntityMap(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eEntityMap = Service.Retrieve("entitymap", id, new ColumnSet("sourceentityname", "targetentityname"));
                if (eEntityMap != null && eEntityMap.Contains("sourceentityname") && eEntityMap.Contains("targetentityname"))
                {
                    info.Name = $"Source: {eEntityMap["sourceentityname"]} | Target: {eEntityMap["targetentityname"]}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetModelDrivenApp(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eAppModule = Service.Retrieve("appmodule", id, new ColumnSet("name", "uniquename", "statuscode"));
                if (eAppModule != null && eAppModule.Contains("name"))
                {
                    info.Name = $"{eAppModule["name"]}";
                    info.Description = $"Unique Name: {eAppModule["uniquename"]}, Status: {eAppModule.GetFormattedValue("statuscode")}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetCanvasApp(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eCanvasApp = Service.Retrieve("canvasapp", id, new ColumnSet("name", "displayname"));
                if (eCanvasApp != null && eCanvasApp.Contains("name"))
                {
                    info.Name = $"{eCanvasApp["displayname"]}";
                    info.Description = $"Unique Name: {eCanvasApp["name"]}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetSitemap(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eSitemap = Service.Retrieve("sitemap", id, new ColumnSet(true));
                if (eSitemap != null && eSitemap.Contains("sitemapname"))
                {
                    info.Name = $"{eSitemap["sitemapname"]}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetMobileProfile(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eMobileProfile = Service.Retrieve("mobileofflineprofile", id, new ColumnSet("name"));
                if (eMobileProfile != null && eMobileProfile.Contains("name"))
                {
                    info.Name = $"{eMobileProfile["name"]}";
                }

                return info;
            }
            catch (Exception ex)
            {
                Telemetry.TrackException(ex);
                return null;
            }
        }

        private static ComponentInfo GetEmailTemplate(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eEmailTemplate = Service.Retrieve("template", id, new ColumnSet("title", "templatetypecode", "languagecode"));
                if (eEmailTemplate != null && eEmailTemplate.Contains("title"))
                {
                    info.Name = $"{eEmailTemplate["title"]}";
                    info.Description = $"Type: {eEmailTemplate["templatetypecode"]}, Language: {eEmailTemplate["languagecode"]}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetMailMergeTemplate(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eMailMerge = Service.Retrieve("mailmergetemplate", id, new ColumnSet("name", "languagecode", "templatetypecode"));
                if (eMailMerge != null && eMailMerge.Contains("name"))
                {
                    info.Name = $"{eMailMerge["name"]}";
                    info.Description = $"Type: {eMailMerge["templatetypecode"]}, Language: {eMailMerge["languagecode"]}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetReport(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eReport = Service.Retrieve("report", id, new ColumnSet("name", "languagecode", "reporttypecode"));
                if (eReport != null && eReport.Contains("name"))
                {
                    info.Name = $"{eReport["name"]}";
                    info.Description = $"Type: {eReport.GetFormattedValue("reporttypecode")}, Language: {eReport["languagecode"]}";

                }

                return info;
            }
            catch (Exception ex)
            {
                Telemetry.TrackException(ex);
                return null;
            }
        }

        private static ComponentInfo GetSdkMessageProcessingStep(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eSdkMessage = Service.Retrieve("sdkmessageprocessingstep", id, new ColumnSet("name", "stage", "sdkmessageid", "statuscode"));
                if (eSdkMessage != null && eSdkMessage.Contains("name"))
                {
                    info.Name = $"{eSdkMessage["name"]}";
                    info.Description = $"Stage: {eSdkMessage.GetFormattedValue("stage")}, SDK Message: {eSdkMessage.GetFormattedValue("sdkmessageid")}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetWorkflow(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eWorkflow = Service.Retrieve("workflow", id, new ColumnSet("name", "category", "primaryentity", "statuscode", "scope"));
                if (eWorkflow != null && eWorkflow.Contains("name"))
                {
                    info.Name = $"{eWorkflow["name"]}";
                    info.Description = $"Type: {eWorkflow.GetFormattedValue("category")}, Entity: {eWorkflow.GetFormattedValue("primaryentity")}, Status: {eWorkflow.GetFormattedValue("statuscode")}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetFieldSecurityProfile(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                Entity eFLS = Service.Retrieve("fieldsecurityprofile", id, new ColumnSet("name"));
                if (eFLS != null && eFLS.Contains("name"))
                {
                    info.Name = $"{eFLS["name"]}";
                }

                return info;
            }
            catch (Exception ex) { Telemetry.TrackException(ex); return null; }
        }

        private static ComponentInfo GetEntityRelationship(Guid id)
        {
            try
            {
                ComponentInfo info = new ComponentInfo();
                RetrieveRelationshipRequest req = new RetrieveRelationshipRequest
                {
                    MetadataId = id
                };
                RetrieveRelationshipResponse resp = (RetrieveRelationshipResponse)Service.Execute(req);
                if (resp != null)
                {
                    if (resp.RelationshipMetadata.GetType().Name == "OneToManyRelationshipMetadata")
                    {
                        info.Name = $"{resp.RelationshipMetadata.SchemaName} (1:M)";
                        OneToManyRelationshipMetadata oneToMany = (OneToManyRelationshipMetadata)resp.RelationshipMetadata;
                        info.Description = $"Referenced: {oneToMany.ReferencedEntity} ({oneToMany.ReferencedAttribute}), Referencing: {oneToMany.ReferencingEntity} ({oneToMany.ReferencingAttribute})";
                    }
                    else if (resp.RelationshipMetadata.GetType().Name == "ManyToManyRelationshipMetadata")
                    {
                        info.Name = $"{resp.RelationshipMetadata.SchemaName} (M:M)";
                        ManyToManyRelationshipMetadata manyToMany = (ManyToManyRelationshipMetadata)resp.RelationshipMetadata;
                        info.Description = $"First: {manyToMany.Entity1LogicalName} ({manyToMany.Entity1IntersectAttribute}), Second: {manyToMany.Entity2LogicalName} ({manyToMany.Entity2IntersectAttribute})";
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                Telemetry.TrackException(ex);
                return null;
            }
        }

        #endregion
    }
}
