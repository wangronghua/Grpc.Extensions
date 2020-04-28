﻿using Grpc.Extension.Common;
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Grpc.Core.Interceptors;
using System.Linq;
using Grpc.Core;
using Grpc.Extension.Common.Internal;
using Grpc.Extension.Client.Interceptors;
using Grpc.Extension.Client.Internal;
using System.Threading.Tasks;

namespace Grpc.Extension.Client
{
    /// <summary>
    /// GrpcClient，用于批量调用
    /// </summary>
    public class GrpcClientManager
    {
        private IEnumerable<ClientInterceptor> _clientInterceptors;

        /// <summary>
        /// GrpcClient
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <param name="clientInterceptors"></param>
        public GrpcClientManager(IServiceProvider serviceProvider, IEnumerable<ClientInterceptor> clientInterceptors)
        {
            ServiceProviderAccessor.SetServiceProvider(serviceProvider);
            this._clientInterceptors = clientInterceptors;
        }

        /// <summary>
        /// 获取GrpcClient，用于批量调用
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async Task<T> GetGrpcClient<T>() where T : ClientBase<T>
        {
            var channelManager = ServiceProviderAccessor.GetService<ChannelPool>();
            var bindFlags = BindingFlags.Static | BindingFlags.NonPublic;
            var grpcServiceName = typeof(T).DeclaringType.GetFieldValue<string>("__ServiceName", bindFlags);

            var channel = await channelManager.GetChannel(grpcServiceName);
            var callInvoker = channel.Intercept(_clientInterceptors.ToArray());
            var client = Activator.CreateInstance(typeof(T), callInvoker);

            return client as T;
        }
    }
}
