﻿using Consul;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using Grpc.Extension.Abstract.Discovery;
using System.Threading.Tasks;

namespace Grpc.Extension.Discovery.Consul
{
    /// <summary>
    /// Consul服务发现
    /// </summary>
    public class ConsulServiceDiscovery : IServiceDiscovery
    {
        private ConcurrentDictionary<string, ConsulClient> _consulClients = new ConcurrentDictionary<string, ConsulClient>();
        private ConcurrentDictionary<string, ulong> _lastIndexs = new ConcurrentDictionary<string, ulong>();

        public event ServiceChangedEvent ServiceChanged;

        /// <summary>
        /// 从consul获取可用的节点信息
        /// </summary>
        public async Task<List<string>> GetEndpoints(string serviceName, string consulUrl, string consulTag)
        {
            var client = CreateConsulClient(consulUrl);
            var res =  await client.Health.Service(serviceName, consulTag , true);
            //更新LastIndex
            UpdateLastIndex(serviceName, res);
            //PollForChanges
            _ = Task.Run(() => PollForChanges(serviceName, consulUrl, consulTag));  
            return res.Response.Select(q => $"{q.Service.Address}:{q.Service.Port}").ToList();
        }        
        
        private ConsulClient CreateConsulClient(string consulUrl)
        {
            return _consulClients.GetOrAdd(consulUrl, (url) => new ConsulClient(conf => conf.Address = new Uri(url)));
        }

        /// <summary>
        /// 拉取改变
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="consulUrl"></param>
        /// <param name="consulTag"></param>
        /// <returns></returns>
        private async Task PollForChanges(string serviceName, string consulUrl, string consulTag)
        {
            var client = CreateConsulClient(consulUrl);
            while (true)
            {
                _lastIndexs.TryGetValue(serviceName, out var lastIndex);
                var queryOptions = new QueryOptions() { WaitIndex = lastIndex };
                var res = await client.Health.Service(serviceName, consulTag, true, queryOptions);
                if (res != null && UpdateLastIndex(serviceName,res))
                {
                    ServiceChanged?.Invoke(serviceName, res.Response.Select(q => $"{q.Service.Address}:{q.Service.Port}").ToList());
                }
            }
        }

        /// <summary>
        /// 更新LastIndex
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="queryResult"></param>
        /// <returns></returns>
        private bool UpdateLastIndex(string serviceName, QueryResult queryResult)
        {
            _lastIndexs.TryGetValue(serviceName, out var lastIndex);
            if (queryResult.LastIndex > lastIndex)
            {
                _lastIndexs.AddOrUpdate(serviceName, queryResult.LastIndex, (k, v) => queryResult.LastIndex);
                return true;
            }
            
            return false;
        }

    }
}
