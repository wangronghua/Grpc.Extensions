﻿using Grpc.Core;
using Grpc.Extension.Abstract.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Grpc.Extension
{
    /// <summary>
    /// GrpcServerOptions
    /// </summary>
    public class GrpcServerOptions
    {
        /// <summary>
        /// Grpc服务地址(192.168.*.*:0)
        /// </summary>
        public string ServiceAddress { get; set; }

        /// <summary>
        /// 是否启用服务注册和服务发现
        /// </summary>
        public bool EnableDiscovery { get; set; } = true;

        /// <summary>
        /// 服务注册地址(http://192.168.8.6:8500)
        /// </summary>
        public string DiscoveryUrl { get; set; }

        /// <summary>
        /// 服务注册名
        /// </summary>
        public string DiscoveryServiceName { get; set; }

        /// <summary>
        /// 服务注册Tags(可用于版本标记)
        /// </summary>
        public string DiscoveryServiceTags { get; set; }

        /// <summary>
        /// 服务TTL(秒)
        /// </summary>
        public int DiscoveryTTLInterval { get; set; } = 10;

        /// <summary>
        /// 默认错误码
        /// </summary>
        public int DefaultErrorCode
        {
            get { return GrpcErrorCode.DefaultErrorCode; }
            set { GrpcErrorCode.DefaultErrorCode = value; }
        }

        /// <summary>
        /// ChannelOption
        /// </summary>
        [JsonIgnore]
        public IEnumerable<ChannelOption> ChannelOptions { get; set; }

        /// <summary>
        /// JaegerOptions
        /// </summary>
        public JaegerOptions Jaeger { get; set; }

        /// <summary>
        /// Grpc客户端调用超时时间(单位:秒)
        /// </summary>
        public double GrpcCallTimeOut { get; set; } = 10;
    }

}
