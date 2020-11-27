﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Extension.Abstract;
using Grpc.Extension.BaseService;
using Grpc.Extension.BaseService.Model;
using Grpc.Extension.Client;
using Grpc.Extension.Common;
using Grpc.Extension.Common.Internal;
using Grpc.Extension.Interceptors;
using Grpc.Extension.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTracing;
using OpenTracing.Util;

namespace Grpc.Extension
{
    /// <summary>
    /// ServerBuilder
    /// </summary>
    public class ServerBuilder
    {
        public static Func<IDisposable> GetScopeFunc;
        public static Func<Type, object> GetServiceFunc;

        private readonly List<ServerInterceptor> _interceptors = new List<ServerInterceptor>();
        private readonly List<ServerServiceDefinition> _serviceDefinitions = new List<ServerServiceDefinition>();
        private readonly List<IGrpcService> _grpcServices = new List<IGrpcService>();
        private readonly GrpcServerOptions _grpcServerOptions;
        private readonly IEnumerable<ServerInterceptor> _serverInterceptors;
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// ServerBuilder
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <param name="grpcServerOptions"></param>
        /// <param name="serverInterceptors"></param>
        /// <param name="grpcServices"></param>
        /// <param name="loggerFactory"></param>
        public ServerBuilder(IServiceProvider serviceProvider,
            IOptions<GrpcServerOptions> grpcServerOptions,
            IEnumerable<ServerInterceptor> serverInterceptors,
            IEnumerable<IGrpcService> grpcServices,
            ILoggerFactory loggerFactory)
        {
            ServiceProviderAccessor.SetServiceProvider(serviceProvider);
            this._grpcServices.AddRange(grpcServices);
            this._grpcServerOptions = grpcServerOptions.Value;
            this._serverInterceptors = serverInterceptors;
            this._loggerFactory = loggerFactory;
        }

        /// <summary>
        /// 注入基本配制
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public ServerBuilder UseOptions(Action<GrpcExtensionsOptions> action)
        {
            action(GrpcExtensionsOptions.Instance);
            return this;
        }

        /// <summary>
        /// 初始化配制
        /// </summary>
        private ServerBuilder InitGrpcOptions()
        {
            var serverOptions = _grpcServerOptions;
            
            //Jaeger配置
            if (serverOptions.Jaeger != null && string.IsNullOrWhiteSpace(serverOptions.Jaeger.ServiceName))
                serverOptions.Jaeger.ServiceName = serverOptions.DiscoveryServiceName;

            #region 默认的客户端配制

            var clientOptions = ServiceProviderAccessor.GetService<IOptions<GrpcClientOptions>>().Value;
            clientOptions.DiscoveryUrl = serverOptions.DiscoveryUrl;
            clientOptions.DefaultErrorCode = serverOptions.DefaultErrorCode;
            clientOptions.Jaeger = serverOptions.Jaeger;
            clientOptions.GrpcCallTimeOut = serverOptions.GrpcCallTimeOut;

            #endregion

            return this;
        }

        /// <summary>
        /// 注入Grpc,Discovery配制
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        public ServerBuilder UseGrpcOptions(Action<GrpcServerOptions> options)
        {
            options(_grpcServerOptions);
            return this;
        }

        /// <summary>
        /// 注入GrpcService
        /// </summary>
        /// <param name="serviceDefinition"></param>
        /// <returns></returns>
        public ServerBuilder UseGrpcService(ServerServiceDefinition serviceDefinition)
        {
            _serviceDefinitions.Add(serviceDefinition);
            return this;
        }

        /// <summary>
        /// 注入IGrpcService
        /// </summary>
        /// <returns></returns>
        public ServerBuilder UseGrpcService()
        {
            return UseGrpcService(_grpcServices);
        }

        /// <summary>
        /// 注入IGrpcService
        /// </summary>
        /// <param name="grpcServices"></param>
        /// <returns></returns>
        private ServerBuilder UseGrpcService(IEnumerable<IGrpcService> grpcServices)
        {
            var builder = ServerServiceDefinition.CreateBuilder();
            //grpcServices.ToList().ForEach(grpc => grpc.RegisterMethod(builder));
            grpcServices.ToList().ForEach(grpc => {
                if (grpc is IGrpcBaseService)
                {
                    GrpcMethodHelper.AutoRegisterMethod(grpc, builder, ServerConsts.BaseServicePackage, ServerConsts.BaseServiceName);
                }
                else
                {
                    GrpcMethodHelper.AutoRegisterMethod(grpc, builder);
                }
            });
            _serviceDefinitions.Add(builder.Build());
            return this;
        }

        /// <summary>
        /// CodeFirst生成proto文件
        /// </summary>
        /// <param name="dir">生成目录</param>
        /// <param name="spiltProto">是否拆分service和message协议</param>
        /// <returns></returns>
        public ServerBuilder UseProtoGenerate(string dir,bool spiltProto = true)
        {
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ProtoGenerator.Gen(dir, spiltProto);

            return this;
        }

        /// <summary>
        /// 使用DashBoard(提供基础服务)
        /// </summary>
        /// <returns></returns>
        public ServerBuilder UseDashBoard()
        {
            var serviceBinder = new GrpcServiceBinder();
            
            foreach (var serviceDefinition in _serviceDefinitions)
            {
                var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                /*
                //生成Grpc元数据信息(1.19以前可以反射处理)
                var callHandlers = serviceDefinition.GetPropertyValue<IDictionary>("CallHandlers", bindingFlags);
                GrpcServiceExtension.BuildMeta(callHandlers);
                */
                //生成Grpc元数据信息(1.19以后使用自定义serviceBinder)
                var bindMethodInfo = serviceDefinition.GetType().GetMethodInfo("BindService", bindingFlags);
                bindMethodInfo.Invoke(serviceDefinition, new[] { serviceBinder });
            }
            //注册基础服务
            UseGrpcService(new List<IGrpcService> { new CmdService(), new MetaService() });
            return this;
        }

        /// <summary>
        /// 注入服务端中间件
        /// </summary>
        /// <param name="interceptor"></param>
        /// <returns></returns>
        private ServerBuilder UseInterceptor(ServerInterceptor interceptor)
        {
            _interceptors.Add(interceptor);
            return this;
        }

        /// <summary>
        /// 注入服务端中间件
        /// </summary>
        /// <param name="interceptors"></param>
        /// <returns></returns>
        private ServerBuilder UseInterceptor(IEnumerable<ServerInterceptor> interceptors)
        {
            _interceptors.AddRange(interceptors);
            return this;
        }

        /// <summary>
        /// 使用LoggerFactory
        /// </summary>
        /// <returns></returns>
        private ServerBuilder UseLoggerFactory()
        {
            var _logger = _loggerFactory.CreateLogger<ServerBuilder>();
            var _loggerAccess = _loggerFactory.CreateLogger("grpc.access");

            LoggerAccessor.Instance.LoggerError += (ex, type) => _logger.LogError(ex.ToString());
            LoggerAccessor.Instance.LoggerMonitor += (msg, type) => _loggerAccess.LogInformation(msg);

            return this;
        }

        /// <summary>
        /// 配制日志(默认使用LoggerFactory)
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public ServerBuilder UseLogger(Action<LoggerAccessor> action)
        {
            action(LoggerAccessor.Instance);
            return this;
        }

        /// <summary>
        /// 有Jaeger配制就使用Jaeger
        /// </summary>
        /// <returns></returns>
        private void UseJaeger()
        {
            var jaeger = _grpcServerOptions.Jaeger;
            if (jaeger?.CheckConfig() == true)
            {
                var tracer = ServiceProviderAccessor.GetService<ITracer>();
                if (tracer != null) GlobalTracer.Register(tracer);
            }
        }

        /// <summary>
        /// 构建Server
        /// </summary>
        /// <returns></returns>
        public Server Build()
        {
            //检查服务配制
            if (string.IsNullOrWhiteSpace(_grpcServerOptions.ServiceAddress))
                throw new ArgumentException(@"GrpcServer:ServiceAddress is null");

            //初始化配制,注入中间件,GrpcService
            this.InitGrpcOptions()//初始化配制
                .UseInterceptor(_serverInterceptors)//注入中间件
                .UseLoggerFactory()//使用LoggerFactory
                .UseJaeger();

            Server server = new Server(_grpcServerOptions.ChannelOptions);
            //使用拦截器
            var serviceDefinitions = ApplyInterceptor(_serviceDefinitions, _interceptors);
            //添加服务定义
            foreach (var serviceDefinition in serviceDefinitions)
            {
                server.Services.Add(serviceDefinition);
            }
            //添加服务IPAndPort
            //var ip = NetHelper.GetLocalIp();
            //var port = NetHelper.GetPort(_grpcServerOptions.ServiceAddress);
            var ipport = NetHelper.GetIPAndPort(_grpcServerOptions.ServiceAddress);

            server.Ports.Add(new ServerPort(ipport.Item1, ipport.Item2, ServerCredentials.Insecure));

            return server;
        }

        private static IEnumerable<ServerServiceDefinition> ApplyInterceptor(IEnumerable<ServerServiceDefinition> serviceDefinitions, IEnumerable<Interceptor> interceptors)
        {
            return serviceDefinitions.Select(serviceDefinition => serviceDefinition.Intercept(interceptors.ToArray()));
        }
    }
}
