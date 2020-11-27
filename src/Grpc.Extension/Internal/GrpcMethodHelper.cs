using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Extension.Abstract;
using Grpc.Extension.BaseService;
using Grpc.Extension.BaseService.Model;
using Grpc.Extension.Common;
using Grpc.Extension.Common.Internal;

namespace Grpc.Extension.Internal
{
    // ReSharper disable once IdentifierTypo
    internal static class GrpcMethodHelper
    {
        // ReSharper disable once InconsistentNaming
        private static readonly MethodInfo buildMethod;
        // ReSharper disable once InconsistentNaming
        private static readonly MethodInfo unaryAddMethod;
        private static readonly MethodInfo clientStreamingAddMethod;
        private static readonly MethodInfo serverStreamingAddMethod;
        private static readonly MethodInfo duplexStreamingAddMethod;

        // ReSharper disable once IdentifierTypo
        static GrpcMethodHelper()
        {
            buildMethod = typeof(GrpcMethodHelper).GetMethod("BuildMethod");
            var methods = typeof(ServerServiceDefinition.Builder).GetMethods().Where(p => p.Name == "AddMethod");
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 2) continue;
                if (parameters[1].ParameterType.Name.Contains("UnaryServerMethod"))
                {
                    unaryAddMethod = method;
                }
                else if (parameters[1].ParameterType.Name.Contains("ClientStreamingServerMethod"))
                {
                    clientStreamingAddMethod = method;
                }
                else if (parameters[1].ParameterType.Name.Contains("ServerStreamingServerMethod"))
                {
                    serverStreamingAddMethod = method;
                }
                else if (parameters[1].ParameterType.Name.Contains("DuplexStreamingServerMethod"))
                {
                    duplexStreamingAddMethod = method;
                }
            }
        }

        /// <summary>
        /// 自动注册服务方法
        /// </summary>
        /// <param name="srv"></param>
        /// <param name="builder"></param>
        /// <param name="package"></param>
        /// <param name="serviceName"></param>
        public static void AutoRegisterMethod(IGrpcService srv, ServerServiceDefinition.Builder builder, string package = null, string serviceName = null)
        {
            var methods = srv.GetType().BaseType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                if (!method.ReturnType.Name.StartsWith("Task")) continue;
                var parameters = method.GetParameters();
                if (parameters.Length < 2 || parameters[parameters.Length-1].ParameterType != typeof(ServerCallContext) ||
                    method.CustomAttributes.Any(x => x.AttributeType == typeof(NotGrpcMethodAttribute))) continue;

                Type inputType = parameters[0].ParameterType;
                Type inputType2 = parameters[1].ParameterType;
                Type outputType = method.ReturnType.IsGenericType ? method.ReturnType.GenericTypeArguments[0] : method.ReturnType;

                var addMethod = unaryAddMethod;
                var serverMethodType = typeof(UnaryServerMethod<,>);
                var methodType = MethodType.Unary;
                var reallyInputType = inputType;
                var reallyOutputType = outputType;
                string interceptorMethodName = "UnaryServerMethod";

                //非一元方法
                if ((inputType.IsGenericType || inputType2.IsGenericType))
                {
                    if (inputType.Name == "IAsyncStreamReader`1")
                    {
                        reallyInputType = inputType.GenericTypeArguments[0];
                        if (inputType2.Name == "IServerStreamWriter`1")//双向流
                        {
                            addMethod = duplexStreamingAddMethod;
                            methodType = MethodType.DuplexStreaming;
                            serverMethodType = typeof(DuplexStreamingServerMethod<,>);
                            reallyOutputType = inputType2.GenericTypeArguments[0];
                            interceptorMethodName = "DuplexStreamingServerMethod";
                        }
                        else//客户端流
                        {
                            addMethod = clientStreamingAddMethod;
                            methodType = MethodType.ClientStreaming;
                            serverMethodType = typeof(ClientStreamingServerMethod<,>);
                            interceptorMethodName = "ClientStreamingServerMethod";
                        }
                    }
                    else if (inputType2.Name == "IServerStreamWriter`1")//服务端流
                    {
                        addMethod = serverStreamingAddMethod;
                        methodType = MethodType.ServerStreaming;
                        serverMethodType = typeof(ServerStreamingServerMethod<,>);
                        reallyOutputType = inputType2.GenericTypeArguments[0];
                        interceptorMethodName = "ServerStreamingServerMethod";
                    }
                }

                var interceptorType = typeof(GrpcServerInterceptor<,>).MakeGenericType(reallyInputType, reallyOutputType);
                var interceptorInstance = Activator.CreateInstance(interceptorType, method, srv.GetType());
                var interceptorMethod = interceptorType.GetMethod(interceptorMethodName);

                var buildMethodResult = buildMethod.MakeGenericMethod(reallyInputType, reallyOutputType)
                    .Invoke(null, new object[] { srv, method.Name, package, serviceName, methodType });
                //Delegate serverMethodDelegate = method.CreateDelegate(serverMethodType
                //.MakeGenericType(reallyInputType, reallyOutputType), method.IsStatic ? null : srv);

                Delegate serverMethodDelegate = interceptorMethod.CreateDelegate(serverMethodType
                .MakeGenericType(reallyInputType, reallyOutputType), interceptorInstance);

                addMethod.MakeGenericMethod(reallyInputType, reallyOutputType).Invoke(builder, new[] { buildMethodResult, serverMethodDelegate });
            }
        }

        /// <summary>
        /// 生成Grpc方法（CodeFirst方式）
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="srv"></param>
        /// <param name="methodName"></param>
        /// <param name="package"></param>
        /// <param name="srvName"></param>
        /// <param name="mType"></param>
        /// <returns></returns>
        public static Method<TRequest, TResponse> BuildMethod<TRequest, TResponse>(this IGrpcService srv,
            string methodName, string package = null, string srvName = null, MethodType mType = MethodType.Unary)
        {
            var serviceName = srvName ??
                              GrpcExtensionsOptions.Instance.GlobalService ??
                              (srv.GetType().FullName.StartsWith("Castle.Proxies.") ? srv.GetType().Name.Substring(0, srv.GetType().Name.Length - 5) : srv.GetType().Name);
            var pkg = package ?? GrpcExtensionsOptions.Instance.GlobalPackage;
            if (!string.IsNullOrWhiteSpace(pkg))
            {
                serviceName = $"{pkg}.{serviceName}";
            }
            #region 为生成proto收集信息
            if (!(srv is IGrpcBaseService) || GrpcExtensionsOptions.Instance.GenBaseServiceProtoEnable)
            {
                ProtoInfo.Methods.Add(new ProtoMethodInfo
                {
                    ServiceName = serviceName,
                    MethodName = methodName,
                    RequestName = typeof(TRequest).Name,
                    ResponseName = typeof(TResponse).Name,
                    MethodType = mType
                });
                ProtoGenerator.AddProto<TRequest>(typeof(TRequest).Name);
                ProtoGenerator.AddProto<TResponse>(typeof(TResponse).Name);
            }
            #endregion
            var request = Marshallers.Create<TRequest>((arg) => ProtobufExtensions.Serialize<TRequest>(arg), data => ProtobufExtensions.Deserialize<TRequest>(data));
            var response = Marshallers.Create<TResponse>((arg) => ProtobufExtensions.Serialize<TResponse>(arg), data => ProtobufExtensions.Deserialize<TResponse>(data));
            return new Method<TRequest, TResponse>(mType, serviceName, methodName, request, response);
        }
    }

    public class GrpcServerInterceptor<TRequest, TResponse>
                where TRequest : class
                where TResponse : class
    {
        private MethodInfo _method;
        private Type _grpcType;
        public GrpcServerInterceptor(MethodInfo method, Type grpcType)
        {
            _method = method;
            _grpcType = grpcType;
        }

        public async Task<TResponse> UnaryServerMethod(TRequest request, ServerCallContext context)
        {
            TResponse result = null;
            await MethodCore(async obj =>
            {
                result = await (obj as Task<TResponse>);
            }, request, context);
            return result;
        }
        public async Task<TResponse> ClientStreamingServerMethod(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context)
        {
            TResponse result = null;
            await MethodCore(async obj =>
            {
                result = await (obj as Task<TResponse>);
            }, requestStream, context);
            return result;
        }

        public Task ServerStreamingServerMethod(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context)
        {
            return MethodCore(obj =>
            {
                return obj as Task;
            }, request, responseStream, context);
        }
        public Task DuplexStreamingServerMethod(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context)
        {
            return MethodCore(obj =>
            {
                return obj as Task;
            }, requestStream, responseStream, context);
        }

        private async Task MethodCore(Func<object, Task> func, params object[] objects)
        {
            if (ServerBuilder.GetScopeFunc == null)
            {
                throw new Exception("ServerBuilder GetScopeFunc 未设置");
            }
            if (ServerBuilder.GetServiceFunc == null)
            {
                throw new Exception("ServerBuilder GetServiceFunc 未设置");
            }
            using (ServerBuilder.GetScopeFunc())
            {
                if (_method.IsStatic)
                {
                    await func(_method.Invoke(null, objects));
                }
                else
                {
                    await func(_method.Invoke(ServerBuilder.GetServiceFunc(_grpcType.BaseType), objects));
                }
            }
        }
    }
}
