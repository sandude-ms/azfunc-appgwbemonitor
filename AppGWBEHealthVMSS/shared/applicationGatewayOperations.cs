﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;

namespace AppGWBEHealthVMSS.shared
{
    class ApplicationGatewayOperations
    {
        public static void CheckApplicationGatewayBEHealth(ApplicationGatewayBackendHealthInner appGw, IVirtualMachineScaleSet scaleSet, int minHealthyServers, ILogger log)
        {
            try
            {
                log.LogInformation("Enumerating Application Gateway Backend Unhealthy Servers");
                 var healthy = new List<ApplicationGatewayBackendHealthServer>();
                var unhealthy = new List<ApplicationGatewayBackendHealthServer>();
                foreach (var server in appGw.BackendAddressPools[0].BackendHttpSettingsCollection[0].Servers)
                {
                    if (server.Health.Value == "Healthy")
                    {
                        healthy.Add(server);
                    }
                    else
                    {
                        unhealthy.Add(server);
                    }
                }
     
                List<string> appGwBadIps = new List<string>();

                if (unhealthy.Count > 0)
                {
                    log.LogInformation("Unhealthy node count = {0}, removing nodes", unhealthy.Count);
                    VmScaleSetOperations.RemoveVMSSInstanceByID(scaleSet, unhealthy.Select(s => s.Address).ToList(), log).ContinueWith(t => log.LogInformation("Delete VMs complete"));
                }
            }
            catch (Exception e)
            {
                log.LogInformation("Error Message: " + e.Message);
                log.LogInformation("HResult: " + e.HResult);
                log.LogInformation("InnerException:" + e.InnerException);
            }

        }
        public static ConnectionInfo GetConcurrentConnectionCountAppGW(IApplicationGateway appGW, IAzure azureClient, ILogger log)
        {
            try
            {
                int avgConcurrentConnections = 0;
                int avgTotalRequests = 0;

                log.LogInformation("Getting Metric Definitions");
                var metricDefs = azureClient.MetricDefinitions.ListByResource(appGW.Id).Where(x => x.Name.LocalizedValue == "Current Connections" || x.Name.LocalizedValue == "Total Requests");
                DateTime recordDateTime = DateTime.Now.ToUniversalTime();

                foreach (var metricDef in metricDefs)
                {
                    var metricCollection = metricDef.DefineQuery().StartingFrom(recordDateTime.AddMinutes(-1)).EndsBefore(recordDateTime).WithAggregation("Maximum").Execute();
                    foreach (var metric in metricCollection.Metrics)
                    {
                        foreach (var timeElement in metric.Timeseries)
                        {
                            foreach (var data in timeElement.Data)
                            {
                                if (metric.Name.Inner.LocalizedValue == "Current Connections")
                                {
                                    log.LogInformation("Concurrent Connections: {0}", data.Maximum);
                                    avgConcurrentConnections = Convert.ToInt32(data.Maximum);
                                }
                                if (metric.Name.Inner.LocalizedValue == "Total Requests")
                                {
                                    log.LogInformation("Total Requests: {0}", data.Maximum);
                                    avgTotalRequests = Convert.ToInt32(data.Maximum);
                                }
                            }
                        }
                    }
                }
                return new ConnectionInfo { CurrentConnections = avgConcurrentConnections, TotalRequests = avgTotalRequests };
            }
            catch (Exception e)
            {
                
                log.LogError(e, "Error Getting metrics: " + e.ToString());
                throw;
            }
            
        }

        public static Tuple<int, int> GetHealthyAndUnhealthyNodeCounts(ApplicationGatewayBackendHealthInner appGw, ILogger log)
        {
            var healthy = 0;
            var unhealthy = 0;
            foreach (var h in appGw.BackendAddressPools[0].BackendHttpSettingsCollection[0].Servers.Select(s=>s.Health.Value))
            {
                switch (h.ToLower())
                {
                    case "healthy":
                        healthy++;
                        break;
                    default:
                        unhealthy++;
                        break;
                }
            }
            return new Tuple<int, int>(healthy, unhealthy);
        }
    }
}
